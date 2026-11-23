#define _GNU_SOURCE
#include "consent.h"

#include <endian.h>
#include <errno.h>
#include <inttypes.h>
#include <linux/fs.h>
#include <linux/posix_acl.h>
#include <openssl/evp.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/random.h>
#include <sys/stat.h>
#include <systemd/sd-bus.h>
#include <time.h>

/* The graphical agent must be qualified with the maximum-size prompt before enabling it. */
enum { sender_capacity = 256, target_capacity = 513, changes_capacity = 769, presentation_limit = 1024 };

struct fc_consent {
    struct fc_consent_details details;
    char sender[sender_capacity];
    char requester[36], target[target_capacity], scope[110], changes[changes_capacity];
    char nonce[FC_CONSENT_NONCE_LENGTH + 1], digest[FC_CONSENT_DIGEST_LENGTH + 1];
    char path[FC_SUBJECT_PATH_BYTES];
    struct timespec expires;
    uid_t uid;
    pid_t pid;
    unsigned inspect_sections, reviewed_sections;
    int authorization_started;
    unsigned char revision[FC_SECURITY_REVISION_BYTES];
    struct fc_change change;
    unsigned char acl[];
};

struct buffer {
    char *text;
    size_t capacity, length;
    int overflow;
};

__attribute__((format(printf, 2, 3)))
static void append(struct buffer *out, const char *format, ...)
{
    if (out->overflow)
        return;
    size_t available = out->capacity - out->length;
    va_list args;
    va_start(args, format);
    int length = vsnprintf(out->text + out->length, available, format, args);
    va_end(args);
    if (length < 0 || (size_t)length >= available)
        out->overflow = 1;
    else
        out->length += (size_t)length;
}

/* An undecodable sequence consumes only its first byte, so every name is displayed reversibly. */
static size_t utf8(const unsigned char *bytes, size_t remaining, unsigned *scalar)
{
    unsigned first = bytes[0];
    if (first < 0x80) {
        *scalar = first;
        return 1;
    }
    unsigned count, value, minimum;
    if (first >= 0xc2 && first <= 0xdf) {
        count = 2; value = first & 0x1f; minimum = 0x80;
    } else if (first >= 0xe0 && first <= 0xef) {
        count = 3; value = first & 0x0f; minimum = 0x800;
    } else if (first >= 0xf0 && first <= 0xf4) {
        count = 4; value = first & 0x07; minimum = 0x10000;
    } else {
        return 0;
    }
    if (count > remaining)
        return 0;
    for (unsigned i = 1; i < count; ++i) {
        if ((bytes[i] & 0xc0u) != 0x80)
            return 0;
        value = (value << 6) | (bytes[i] & 0x3fu);
    }
    if (value < minimum || value > 0x10ffff || (value >= 0xd800 && value <= 0xdfff))
        return 0;
    *scalar = value;
    return count;
}

static int escaped(unsigned scalar)
{
    return scalar < 0x20 || (scalar >= 0x7f && scalar <= 0x9f) ||
        scalar == 0x061c || (scalar >= 0x200b && scalar <= 0x200f) ||
        (scalar >= 0x2028 && scalar <= 0x202e) || (scalar >= 0x2060 && scalar <= 0x206f) ||
        scalar == 0xfeff || scalar == '"' || scalar == '\\' || scalar == '<' || scalar == '>' || scalar == '&';
}

static int format_target(char *text, size_t capacity, const char *path, size_t length)
{
    struct buffer out = { .text = text, .capacity = capacity };
    append(&out, "\nTarget \"");
    for (size_t i = 0; i < length && !out.overflow;) {
        unsigned scalar;
        size_t count = utf8((const unsigned char *)path + i, length - i, &scalar);
        if (count == 0) {
            append(&out, "\\x%02X", (unsigned)(unsigned char)path[i++]);
            continue;
        }
        if (escaped(scalar))
            append(&out, "\\u%04X", scalar);
        else
            append(&out, "%.*s", (int)count, path + i);
        i += count;
    }
    append(&out, "\"");
    return out.overflow ? -E2BIG : 0;
}

static const char *acl_name(uint16_t tag)
{
    switch (tag) {
    case ACL_USER_OBJ: return "owner";
    case ACL_USER: return "UID";
    case ACL_GROUP_OBJ: return "owning group";
    case ACL_GROUP: return "GID";
    case ACL_MASK: return "mask";
    case ACL_OTHER: return "other";
    default: return NULL;
    }
}

static void append_rights(struct buffer *out, const struct fc_acl_entry *entry)
{
    if (!entry) {
        append(out, "absent");
        return;
    }
    append(out, "%c%c%c", entry->rights & ACL_READ ? 'r' : '-', entry->rights & ACL_WRITE ? 'w' : '-',
        entry->rights & ACL_EXECUTE ? 'x' : '-');
}

static void append_acl_rule(struct buffer *out, const struct fc_acl_entry *old, const struct fc_acl_entry *new)
{
    const struct fc_acl_entry *entry = old ? old : new;
    append(out, "\n  %s", acl_name(entry->tag));
    if (entry->id != UINT32_MAX)
        append(out, " %" PRIu32, entry->id);
    append(out, " ");
    append_rights(out, old);
    append(out, " → ");
    append_rights(out, new);
}

static int compare_entries(const struct fc_acl_entry *old, const struct fc_acl_entry *new)
{
    if (old->tag != new->tag)
        return (old->tag > new->tag) - (old->tag < new->tag);
    return (old->id > new->id) - (old->id < new->id);
}

static int append_acl(struct buffer *out, const char *label, const struct fc_acl_evidence *old,
    const struct fc_acl_evidence *new)
{
    if (old->status == new->status && old->length == new->length && memcmp(old->bytes, new->bytes, old->length) == 0)
        return 0;
    if ((old->status != FC_SECURITY_PRESENT && old->status != FC_SECURITY_ABSENT) ||
        (new->status != FC_SECURITY_PRESENT && new->status != FC_SECURITY_ABSENT))
        return -EBADMSG;
    append(out, "\n%s%s", label, new->status == FC_SECURITY_ABSENT ? " (remove attribute)" : "");
    size_t left = 0, right = 0;
    while (!out->overflow && (left < old->entries || right < new->entries)) {
        int has_old = left < old->entries, has_new = right < new->entries;
        struct fc_acl_entry a, b;
        if ((has_old && fc_acl_entry_at(old, left, &a) < 0) || (has_new && fc_acl_entry_at(new, right, &b) < 0))
            return -EBADMSG;
        int order = has_old && has_new ? compare_entries(&a, &b) : (has_old ? -1 : 1);
        if (order != 0 || a.rights != b.rights)
            append_acl_rule(out, order <= 0 ? &a : NULL, order >= 0 ? &b : NULL);
        left += order <= 0;
        right += order >= 0;
    }
    return 0;
}

static void append_protection(struct buffer *out, const char *label, uint32_t mask, uint32_t old, uint32_t new)
{
    if ((old ^ new) & mask)
        append(out, "\n%s %s → %s", label, old & mask ? "on" : "off", new & mask ? "on" : "off");
}

static int format_change(char text[changes_capacity], const struct fc_security_snapshot *before,
    const struct fc_change_projection *projection)
{
    text[0] = 0;
    struct buffer out = { .text = text, .capacity = changes_capacity };
    const struct fc_security_snapshot *after = &projection->after;
    const struct statx *old = &before->subject.facts, *new = &after->subject.facts;
    if (old->stx_uid != new->stx_uid)
        append(&out, "\nOwner UID %" PRIu32 " → %" PRIu32, old->stx_uid, new->stx_uid);
    if (old->stx_gid != new->stx_gid)
        append(&out, "\nOwning GID %" PRIu32 " → %" PRIu32, old->stx_gid, new->stx_gid);
    if (old->stx_mode != new->stx_mode)
        append(&out, "\nMode %04o → %04o", old->stx_mode & 07777u, new->stx_mode & 07777u);
    unsigned added = (new->stx_mode & 07000u) & ~(unsigned)old->stx_mode;
    if (added)
        append(&out, "\nAdd%s%s%s", added & S_ISUID ? " set-user-ID" : "",
            added & S_ISGID ? " set-group-ID" : "", added & S_ISVTX ? " sticky bit" : "");
    if (projection->set_id_cleared)
        append(&out, "\nClear%s%s", projection->set_id_cleared & S_ISUID ? " set-user-ID" : "",
            projection->set_id_cleared & S_ISGID ? " set-group-ID" : "");
    if (projection->capabilities_cleared)
        append(&out, "\nClear file capabilities");
    int result = append_acl(&out, "Access ACL", &before->access, &after->access);
    if (result == 0)
        result = append_acl(&out, "Default ACL (for new children)", &before->defaults, &after->defaults);
    if (result < 0)
        return result;
    append_protection(&out, "Immutable", FS_IMMUTABLE_FL, before->flags.bits, after->flags.bits);
    append_protection(&out, "Append-only", FS_APPEND_FL, before->flags.bits, after->flags.bits);
    if (!projection->strictly_restrictive)
        append(&out, "\nThis can grant access or change authority.");
    return out.overflow ? -E2BIG : 0;
}

static void format_inspection(struct fc_consent *consent)
{
    struct buffer out = { .text = consent->changes, .capacity = sizeof(consent->changes) };
    unsigned sections = consent->inspect_sections;
    append(&out, "\nRead owner, group, mode%s%s%s%s.",
        sections & FC_SECURITY_ACCESS ? ", access ACL" : "",
        sections & FC_SECURITY_DEFAULTS ? ", default ACL" : "",
        sections & FC_SECURITY_CAPABILITIES ? ", file capabilities" : "",
        sections & FC_SECURITY_FLAGS ? ", inode flags" : "");
}

static void hex(char *text, const unsigned char *bytes, size_t length)
{
    const char digits[] = "0123456789abcdef";
    for (size_t i = 0; i < length; ++i) {
        text[2 * i] = digits[bytes[i] >> 4];
        text[2 * i + 1] = digits[bytes[i] & 15];
    }
    text[2 * length] = 0;
}

static int hash_field(EVP_MD_CTX *ctx, const void *data, size_t size)
{
    uint64_t length = htole64(size);
    return EVP_DigestUpdate(ctx, &length, sizeof(length)) == 1 && EVP_DigestUpdate(ctx, data, size) == 1;
}

static int seal_digest(struct fc_consent *consent, const struct fc_security_snapshot *before)
{
    static const char domain[] = "FileCat ext4 consent v1";
    const struct statx *facts = &before->subject.facts;
    const struct fc_change *change = &consent->change;
    uint64_t fields[] = {
        (uint64_t)consent->expires.tv_sec, (uint64_t)consent->expires.tv_nsec,
        (uint64_t)consent->uid, (uint64_t)consent->pid, facts->stx_mnt_id, facts->stx_ino,
        facts->stx_dev_major, facts->stx_dev_minor, facts->stx_mode & S_IFMT,
        consent->reviewed_sections, consent->inspect_sections, 1, /* One target. No propagation. */
        consent->inspect_sections ? 0u : (unsigned)change->kind + 1u, 0, 0
    };
    const void *payload = NULL;
    size_t payload_size = 0;
    if (!consent->inspect_sections) {
        switch (change->kind) {
        case FC_CHANGE_MODE: fields[13] = change->value.mode; break;
        case FC_CHANGE_OWNERSHIP: fields[13] = change->value.ownership.uid; fields[14] = change->value.ownership.gid; break;
        case FC_CHANGE_ACCESS_ACL:
        case FC_CHANGE_DEFAULT_ACL:
            payload = change->value.acl.bytes;
            payload_size = change->value.acl.length;
            break;
        case FC_CHANGE_FLAGS: fields[13] = change->value.flags.set; fields[14] = change->value.flags.clear; break;
        }
    }
    for (size_t i = 0; i < sizeof(fields) / sizeof(*fields); ++i)
        fields[i] = htole64(fields[i]);
    EVP_MD_CTX *ctx = EVP_MD_CTX_new();
    if (!ctx)
        return -ENOMEM;
    unsigned char digest[FC_SECURITY_REVISION_BYTES];
    unsigned length = 0;
    int ok = EVP_DigestInit_ex(ctx, EVP_sha256(), NULL) == 1 &&
        hash_field(ctx, domain, sizeof(domain) - 1) && hash_field(ctx, fields, sizeof(fields)) &&
        hash_field(ctx, consent->nonce, FC_CONSENT_NONCE_LENGTH) &&
        hash_field(ctx, consent->sender, strlen(consent->sender)) &&
        hash_field(ctx, consent->path, strlen(consent->path)) &&
        hash_field(ctx, consent->revision, sizeof(consent->revision)) &&
        hash_field(ctx, payload, payload_size) && hash_field(ctx, consent->requester, strlen(consent->requester)) &&
        hash_field(ctx, consent->target, strlen(consent->target)) &&
        hash_field(ctx, consent->scope, strlen(consent->scope)) &&
        hash_field(ctx, consent->changes, strlen(consent->changes)) &&
        EVP_DigestFinal_ex(ctx, digest, &length) == 1 && length == sizeof(digest);
    EVP_MD_CTX_free(ctx);
    if (!ok)
        return -EIO;
    hex(consent->digest, digest, sizeof(digest));
    return 0;
}

static int copy_request(struct fc_consent *consent, const struct fc_consent_requester *requester,
    const struct fc_subject *subject, const struct fc_security_snapshot *before, unsigned inspect_sections)
{
    size_t path_length = strnlen(subject->path, sizeof(subject->path));
    size_t sender_length = strnlen(requester->sender, sizeof(consent->sender));
    if (path_length == 0 || path_length == sizeof(subject->path) || subject->path[0] != '/' ||
        sender_length == sizeof(consent->sender) || requester->sender[0] != ':' ||
        sd_bus_service_name_is_valid(requester->sender) <= 0 || requester->uid == 0 ||
        requester->uid == (uid_t)-1 || requester->pid <= 0)
        return -EINVAL;
    memcpy(consent->sender, requester->sender, sender_length + 1);
    memcpy(consent->path, subject->path, path_length + 1);
    memcpy(consent->revision, before->revision, sizeof(consent->revision));
    consent->uid = requester->uid;
    consent->pid = requester->pid;
    consent->reviewed_sections = before->sections;
    consent->inspect_sections = inspect_sections;
    snprintf(consent->requester, sizeof(consent->requester), "\nRequester UID %ju", (uintmax_t)requester->uid);
    const struct statx *facts = &before->subject.facts;
    struct buffer scope = { .text = consent->scope, .capacity = sizeof(consent->scope) };
    if (S_ISDIR(facts->stx_mode))
        append(&scope, "\nScope One directory. Existing descendants are untouched.");
    else
        append(&scope, "\nScope One file with %" PRIu32 " hard link%s.%s", facts->stx_nlink, facts->stx_nlink == 1 ? "" : "s",
            facts->stx_nlink == 1 ? "" : " Other names are unknown.");
    return scope.overflow ? -E2BIG : format_target(consent->target, sizeof(consent->target), subject->path, path_length);
}

int fc_consent_prepare(const struct fc_consent_requester *requester, const struct fc_subject *subject,
    const struct fc_security_snapshot *before, unsigned inspect_sections, const struct fc_change *change,
    struct fc_consent **prepared)
{
    *prepared = NULL;
    if (!requester->sender || (inspect_sections & ~(unsigned)FC_SECURITY_ALL) ||
        (inspect_sections ? (change != NULL || before->sections != 0) : change == NULL))
        return -EINVAL;
    int carries_acl = change && (change->kind == FC_CHANGE_ACCESS_ACL || change->kind == FC_CHANGE_DEFAULT_ACL);
    size_t acl_size = carries_acl ? change->value.acl.length : 0;
    if (acl_size > FC_ACL_MAX_BYTES || (acl_size && !change->value.acl.bytes))
        return -EINVAL;

    struct fc_consent *consent = calloc(1, sizeof(*consent) + acl_size);
    if (!consent)
        return -ENOMEM;
    int result = copy_request(consent, requester, subject, before, inspect_sections);
    if (result < 0)
        goto failed;
    if (change) {
        consent->change = *change;
        if (carries_acl) {
            if (acl_size)
                memcpy(consent->acl, change->value.acl.bytes, acl_size);
            consent->change.value.acl.bytes = consent->acl;
        }
        struct fc_change_projection projection;
        result = fc_change_admit(subject, before, &consent->change, requester->uid, &projection);
        if (result == 0)
            result = format_change(consent->changes, before, &projection);
    } else {
        struct fc_subject_state current;
        result = fc_subject_observe(subject, &current);
        if (result == 0 && !fc_subject_state_matches(&before->subject, &current))
            result = -ESTALE;
        format_inspection(consent);
    }
    if (result < 0)
        goto failed;
    if (strlen(consent->requester) + strlen(consent->target) + strlen(consent->scope) +
        strlen(consent->changes) > presentation_limit) {
        result = -E2BIG;
        goto failed;
    }
    if (clock_gettime(CLOCK_BOOTTIME, &consent->expires) < 0) {
        result = -errno;
        goto failed;
    }
    consent->expires.tv_sec += FC_CONSENT_SECONDS;
    unsigned char nonce[FC_CONSENT_NONCE_LENGTH / 2];
    ssize_t bytes = getrandom(nonce, sizeof(nonce), GRND_NONBLOCK);
    if (bytes != sizeof(nonce)) {
        result = bytes < 0 ? -errno : -EIO;
        goto failed;
    }
    hex(consent->nonce, nonce, sizeof(nonce));
    result = seal_digest(consent, before);
    if (result < 0)
        goto failed;
    consent->details = (struct fc_consent_details) {
        consent->requester, consent->target, consent->scope, consent->changes, consent->nonce, consent->digest
    };
    *prepared = consent;
    return 0;

failed:
    fc_consent_free(consent);
    return result;
}

const struct fc_consent_details *fc_consent_details(const struct fc_consent *consent)
{
    return &consent->details;
}

const struct fc_change *fc_consent_change(const struct fc_consent *consent)
{
    return consent->inspect_sections ? NULL : &consent->change;
}

unsigned fc_consent_inspect_sections(const struct fc_consent *consent)
{
    return consent->inspect_sections;
}

int fc_consent_requester_matches(const struct fc_consent *consent, const struct fc_consent_requester *requester)
{
    return requester->sender && consent->uid == requester->uid && consent->pid == requester->pid &&
        strcmp(consent->sender, requester->sender) == 0;
}

static int within_deadline(const struct fc_consent *consent)
{
    struct timespec now;
    if (clock_gettime(CLOCK_BOOTTIME, &now) < 0)
        return -errno;
    if (now.tv_sec > consent->expires.tv_sec ||
        (now.tv_sec == consent->expires.tv_sec && now.tv_nsec >= consent->expires.tv_nsec))
        return -ETIMEDOUT;
    return 0;
}

int fc_consent_begin_authorization(struct fc_consent *consent)
{
    if (consent->authorization_started)
        return -EALREADY;
    int result = within_deadline(consent);
    if (result == 0)
        consent->authorization_started = 1;
    return result;
}

int fc_consent_recheck(const struct fc_consent *consent, const struct fc_consent_requester *requester,
    const struct fc_subject *subject, struct fc_security_snapshot *current, struct fc_change_projection *projection)
{
    if (!fc_consent_requester_matches(consent, requester))
        return -EACCES;
    if (strcmp(consent->path, subject->path) != 0)
        return -ESTALE;
    int result = within_deadline(consent);
    if (result < 0)
        return result;
    result = fc_security_capture(subject, consent->reviewed_sections, current);
    if (result < 0)
        return result;
    if (memcmp(consent->revision, current->revision, sizeof(current->revision)) != 0)
        return -ESTALE;
    if (consent->inspect_sections)
        return 0;
    if (!projection)
        return -EINVAL;
    result = fc_change_admit(subject, current, &consent->change, requester->uid, projection);
    if (result < 0)
        return result;
    char changes[changes_capacity];
    result = format_change(changes, current, projection);
    if (result < 0)
        return result;
    return strcmp(changes, consent->changes) == 0 ? 0 : -ESTALE;
}

void fc_consent_free(struct fc_consent *consent)
{
    free(consent);
}
