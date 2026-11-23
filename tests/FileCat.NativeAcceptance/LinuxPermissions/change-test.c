#define _GNU_SOURCE
#include "access-effect.h"
#include "change-dispatch.h"

#include <assert.h>
#include <errno.h>
#include <fcntl.h>
#include <grp.h>
#include <linux/fs.h>
#include <linux/posix_acl.h>
#include <stdio.h>
#include <string.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <sys/wait.h>
#include <sys/xattr.h>
#include <unistd.h>

#ifndef STATX_MNT_ID_UNIQUE
#define STATX_MNT_ID_UNIQUE 0x4000U
#endif

static size_t encode_acl(unsigned char *bytes, const struct fc_acl_entry *entries, size_t count)
{
    size_t length = 4 + 8 * count;
    memset(bytes, 0, length);
    bytes[0] = 2;
    for (size_t i = 0; i < count; ++i) {
        unsigned char *row = bytes + 4 + 8 * i;
        row[0] = (unsigned char)entries[i].tag;
        row[2] = (unsigned char)entries[i].rights;
        for (unsigned offset = 0; offset < 4; ++offset)
            row[4 + offset] = (unsigned char)(entries[i].id >> (8 * offset));
    }
    return length;
}

static uint32_t sample(uint32_t *state, unsigned bound)
{
    *state ^= *state << 13;
    *state ^= *state >> 17;
    *state ^= *state << 5;
    return *state % bound;
}

enum { ids = 4, max_local_entries = 4 + 2 * ids };

struct oracle_access {
    uint32_t owner, group;
    struct fc_acl_entry entries[max_local_entries];
    size_t count;
};

static void generate(uint32_t *seed, struct oracle_access *acl, struct fc_security_snapshot *snapshot)
{
    acl->owner = sample(seed, ids);
    acl->group = sample(seed, ids);
    unsigned owner = sample(seed, 8), group = sample(seed, 8), other = sample(seed, 8);
    int extended = sample(seed, 3) != 0;
    unsigned mask = extended ? sample(seed, 8) : 7;
    acl->count = 0;
    acl->entries[acl->count++] = (struct fc_acl_entry) { ACL_USER_OBJ, (uint16_t)owner, UINT32_MAX };
    for (uint32_t id = 0; extended && id < ids; ++id) {
        if (sample(seed, 2))
            acl->entries[acl->count++] = (struct fc_acl_entry) { ACL_USER, (uint16_t)sample(seed, 8), id };
    }
    acl->entries[acl->count++] = (struct fc_acl_entry) { ACL_GROUP_OBJ, (uint16_t)group, UINT32_MAX };
    for (uint32_t id = 0; extended && id < ids; ++id) {
        if (sample(seed, 2))
            acl->entries[acl->count++] = (struct fc_acl_entry) { ACL_GROUP, (uint16_t)sample(seed, 8), id };
    }
    if (extended)
        acl->entries[acl->count++] = (struct fc_acl_entry) { ACL_MASK, (uint16_t)mask, UINT32_MAX };
    acl->entries[acl->count++] = (struct fc_acl_entry) { ACL_OTHER, (uint16_t)other, UINT32_MAX };

    snapshot->sections = FC_SECURITY_ACCESS;
    snapshot->subject.facts.stx_uid = acl->owner;
    snapshot->subject.facts.stx_gid = acl->group;
    snapshot->subject.facts.stx_mode = (uint16_t)(S_IFREG | (owner << 6) | ((extended ? mask : group) << 3) | other);
    snapshot->access.status = FC_SECURITY_ABSENT;
    snapshot->access.entries = 0;
    snapshot->access.length = 0;
    if (extended) {
        size_t length = encode_acl(snapshot->access.bytes, acl->entries, acl->count);
        fc_acl_decode(&snapshot->access, length);
        assert(snapshot->access.status == FC_SECURITY_PRESENT);
    }
}

/* Directly evaluate each identity, group membership set and requested permission. */
static int permits(const struct oracle_access *acl, uint32_t uid, unsigned groups, unsigned requested)
{
    unsigned mask = 7;
    for (size_t i = 0; i < acl->count; ++i) {
        if (acl->entries[i].tag == ACL_MASK)
            mask = acl->entries[i].rights;
    }
    if (mask == 0) {
        unsigned rights = acl->entries[acl->count - 1].rights;
        if (uid == acl->owner)
            rights = acl->entries[0].rights;
        else if (groups & (1u << acl->group))
            rights = 0;
        return (rights & requested) == requested;
    }
    int matched_group = 0;
    for (size_t i = 0; i < acl->count; ++i) {
        struct fc_acl_entry entry = acl->entries[i];
        if (entry.tag == ACL_USER_OBJ && acl->owner == uid)
            return (entry.rights & requested) == requested;
        if (entry.tag == ACL_USER && entry.id == uid)
            return (entry.rights & mask & requested) == requested;
        if (entry.tag == ACL_GROUP_OBJ || entry.tag == ACL_GROUP) {
            uint32_t id = entry.tag == ACL_GROUP_OBJ ? acl->group : entry.id;
            if (groups & (1u << id)) {
                matched_group = 1;
                if ((entry.rights & mask & requested) == requested)
                    return 1;
            }
        }
        if (entry.tag == ACL_OTHER)
            return !matched_group && (entry.rights & requested) == requested;
    }
    return 0;
}

static void access_oracle(void)
{
    uint32_t seed = 0x57ab2109;
    struct fc_security_snapshot before = { 0 }, after = { 0 };
    unsigned narrow = 0, wide = 0;
    for (unsigned pair = 0; pair < 1024; ++pair) {
        struct oracle_access old, new;
        generate(&seed, &old, &before);
        generate(&seed, &new, &after);
        for (unsigned direction = 0; direction < 2; ++direction) {
            int wider = 0;
            for (uint32_t uid = 0; uid <= ids; ++uid) {
                for (unsigned groups = 0; groups < (1u << ids); ++groups) {
                    for (unsigned request = 1; request <= 7; ++request)
                        wider |= !permits(&old, uid, groups, request) && permits(&new, uid, groups, request);
                }
            }
            assert(fc_access_widens(direction ? &after : &before, direction ? &before : &after) == wider);
            wide += (unsigned)wider;
            narrow += (unsigned)!wider;
            struct oracle_access swap = old;
            old = new;
            new = swap;
        }
    }
    assert(narrow > 0 && wide > 0);
    printf("PASS 2048 directed POSIX comparisons match the finite credential oracle (%u widening, %u non-widening)\n",
        wide, narrow);
}

static void bind_subject(const char *path, struct fc_subject *subject)
{
    int descriptor = open(path, O_PATH | O_NOFOLLOW | O_CLOEXEC);
    assert(descriptor >= 0);
    struct statx facts;
    unsigned mask = STATX_TYPE | STATX_INO | STATX_MNT_ID_UNIQUE;
    assert(statx(descriptor, "", AT_EMPTY_PATH, mask, &facts) == 0 && (facts.stx_mask & mask) == mask);
    struct fc_subject_identity identity = {
        facts.stx_mnt_id, facts.stx_ino, facts.stx_dev_major, facts.stx_dev_minor, facts.stx_mode & S_IFMT
    };
    struct fc_subject_state state;
    assert(fc_subject_bind(subject, descriptor, path, &identity, &state) == 0);
    assert(close(descriptor) == 0);
}

static void require_observed(const struct fc_subject *subject, const struct fc_change_projection *projected)
{
    struct fc_security_snapshot observed;
    assert(fc_security_capture(subject, projected->after.sections, &observed) == 0);
    assert(fc_change_matches(projected, &observed) == 1);
    observed.subject.facts.stx_mode ^= S_IRUSR;
    assert(fc_change_matches(projected, &observed) == 0);
}

/* The private-bus integration test exercises the full process challenge. These setter fixtures
   supply only the lifetime handles that the dispatcher uses after authorization. */
static struct fc_peer lifetime_guard(int *partner)
{
    int channels[2];
    assert(socketpair(AF_UNIX, SOCK_SEQPACKET | SOCK_CLOEXEC, 0, channels) == 0);
    int process = (int)syscall(SYS_pidfd_open, getpid(), 0);
    assert(process >= 0);
    *partner = channels[1];
    return (struct fc_peer) { .socket = channels[0], .process = process, .uid = 1000, .verified = 1 };
}

static void require_dispatch(const struct fc_peer *peer, const struct fc_subject *subject,
    const struct fc_security_snapshot *before, const struct fc_change *change,
    const struct fc_change_projection *projected)
{
    struct fc_write_result result;
    fc_change_dispatch(peer, subject, before, change, projected, &result);
    if (result.state != FC_WRITE_VERIFIED)
        fprintf(stderr, "Setter %d state %d, operation %d, verification %d\n",
            change->kind, result.state, result.operation_error, result.verification_error);
    assert(result.state == FC_WRITE_VERIFIED && result.has_observation);
    require_observed(subject, projected);
}

static void native_access(const char *path, const struct fc_peer *peer)
{
    assert(geteuid() == 1000);
    struct fc_subject subject = FC_SUBJECT_INIT;
    struct fc_security_snapshot before;
    struct fc_change_projection projected;
    bind_subject(path, &subject);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0 && before.access.status == FC_SECURITY_PRESENT);
    struct fc_change change = { .kind = FC_CHANGE_MODE, .value.mode = 0600 };
    assert(fc_change_admit(&subject, &before, &change, getuid(), &projected) == 0 && projected.strictly_restrictive);
    require_dispatch(peer, &subject, &before, &change, &projected);
    puts("PASS ext4 chmod preserves configured named rights and restricts the ACL mask as projected");

    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0);
    static const struct fc_acl_entry basic[] = {
        { ACL_USER_OBJ, 4, UINT32_MAX }, { ACL_GROUP_OBJ, 0, UINT32_MAX }, { ACL_OTHER, 0, UINT32_MAX }
    };
    unsigned char raw[4 + 8 * (sizeof(basic) / sizeof(*basic))];
    size_t size = encode_acl(raw, basic, sizeof(basic) / sizeof(*basic));
    change = (struct fc_change) { .kind = FC_CHANGE_ACCESS_ACL, .value.acl = { raw, size } };
    assert(fc_change_admit(&subject, &before, &change, getuid(), &projected) == 0 &&
        projected.strictly_restrictive && projected.after.access.status == FC_SECURITY_ABSENT);
    require_dispatch(peer, &subject, &before, &change, &projected);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0);
    assert(fc_change_project(&before, &change, &projected) == -EALREADY);
    puts("PASS ext4 collapses a three-entry ACL with the projected mode and refuses a repeated no-op");

    struct fc_change lock = { .kind = FC_CHANGE_FLAGS, .value.flags.set = FS_IMMUTABLE_FL };
    assert(fc_change_admit(&subject, &before, &lock, peer->uid, &projected) == 0 && projected.strictly_restrictive);
    struct fc_write_result result;
    fc_change_dispatch(peer, &subject, &before, &lock, &projected, &result);
    assert(result.state == FC_WRITE_REJECTED && result.operation_error == -EPERM && result.has_observation);
    assert(memcmp(before.revision, result.observed.revision, sizeof(before.revision)) == 0);
    puts("PASS denied immutable ioctl is reported rejected after unchanged native read-back");
    struct fc_security_snapshot protected = projected.after;
    lock.value.flags.set = 0;
    lock.value.flags.clear = FS_IMMUTABLE_FL;
    assert(fc_change_project(&protected, &lock, &projected) == 0 && !projected.strictly_restrictive);
    protected.flags.known = FS_IMMUTABLE_FL | FS_APPEND_FL;
    assert(fc_change_project(&protected, &lock, &projected) == -EOPNOTSUPP);
    assert(fc_change_project(&protected, &change, &projected) == -EPERM);
    lock.value.flags.clear = FS_NODUMP_FL;
    assert(fc_change_project(&before, &lock, &projected) == -EINVAL);
    puts("PASS protection projection only admits typed bits, requires the complete flag word, and identifies unlock as widening");
    fc_subject_close(&subject);
}

static void native_mode_zero(const char *path, const struct fc_peer *peer)
{
    struct fc_subject subject = FC_SUBJECT_INIT;
    struct fc_security_snapshot before;
    struct fc_change_projection projected;
    bind_subject(path, &subject);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0 &&
        (before.subject.facts.stx_mode & 0777) == 0 && before.flags.known != UINT32_MAX);

    static const struct fc_acl_entry named[] = {
        { ACL_USER_OBJ, 0, UINT32_MAX }, { ACL_USER, 4, 12345 }, { ACL_GROUP_OBJ, 0, UINT32_MAX },
        { ACL_MASK, 4, UINT32_MAX }, { ACL_OTHER, 0, UINT32_MAX }
    };
    unsigned char raw[4 + 8 * (sizeof(named) / sizeof(*named))];
    size_t size = encode_acl(raw, named, sizeof(named) / sizeof(*named));
    struct fc_change change = { .kind = FC_CHANGE_ACCESS_ACL, .value.acl = { raw, size } };
    assert(fc_change_admit(&subject, &before, &change, peer->uid, &projected) == 0);
    require_dispatch(peer, &subject, &before, &change, &projected);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0 &&
        before.access.status == FC_SECURITY_PRESENT && (before.subject.facts.stx_mode & 0700) == 0);
    change = (struct fc_change) { .kind = FC_CHANGE_MODE, .value.mode = 0600 };
    assert(fc_change_admit(&subject, &before, &change, peer->uid, &projected) == 0);
    require_dispatch(peer, &subject, &before, &change, &projected);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0 &&
        before.access.status == FC_SECURITY_PRESENT);
    change = (struct fc_change) { .kind = FC_CHANGE_ACCESS_ACL, .value.acl = { NULL, 0 } };
    assert(fc_change_admit(&subject, &before, &change, peer->uid, &projected) == 0);
    require_dispatch(peer, &subject, &before, &change, &projected);
    puts("PASS held ACL set/remove and mode repair work without a content-readable starting file");

    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0);
    change = (struct fc_change) { .kind = FC_CHANGE_MODE, .value.mode = 0400 };
    assert(fc_change_admit(&subject, &before, &change, peer->uid, &projected) == 0);
    int partner;
    struct fc_peer disconnected = lifetime_guard(&partner);
    assert(close(partner) == 0);
    struct fc_write_result result;
    fc_change_dispatch(&disconnected, &subject, &before, &change, &projected, &result);
    assert(result.state == FC_WRITE_NOT_SENT && result.operation_error == -ESRCH && !result.has_observation);
    fc_peer_close(&disconnected);
    struct stat actual;
    assert(stat(path, &actual) == 0 && (actual.st_mode & 0777) == 0600);
    puts("PASS disconnected requester stops before the setter");

    projected.after.subject.facts.stx_mode ^= S_IRUSR; /* Inject unexpected read-back expectations. */
    fc_change_dispatch(peer, &subject, &before, &change, &projected, &result);
    assert(result.state == FC_WRITE_UNCERTAIN && result.operation_error == 0 &&
        result.verification_error == -ESTALE && result.has_observation);
    assert(stat(path, &actual) == 0 && (actual.st_mode & 0777) == 0400);
    puts("PASS successful setter with an unexpected observed state is reported uncertain");
    fc_subject_close(&subject);
}

static void native_default(const char *path, const struct fc_peer *peer)
{
    struct fc_subject subject = FC_SUBJECT_INIT;
    struct fc_security_snapshot before;
    struct fc_change_projection projected;
    bind_subject(path, &subject);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0 &&
        before.defaults.status == FC_SECURITY_ABSENT);
    unsigned mode = before.subject.facts.stx_mode;
    static const struct fc_acl_entry basic[] = {
        { ACL_USER_OBJ, 7, UINT32_MAX }, { ACL_GROUP_OBJ, 4, UINT32_MAX }, { ACL_OTHER, 0, UINT32_MAX }
    };
    unsigned char raw[4 + 8 * (sizeof(basic) / sizeof(*basic))];
    size_t size = encode_acl(raw, basic, sizeof(basic) / sizeof(*basic));
    struct fc_change change = { .kind = FC_CHANGE_DEFAULT_ACL, .value.acl = { raw, size } };
    assert(fc_change_admit(&subject, &before, &change, peer->uid, &projected) == 0);
    require_dispatch(peer, &subject, &before, &change, &projected);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0 &&
        before.defaults.status == FC_SECURITY_PRESENT && before.access.status == FC_SECURITY_ABSENT &&
        before.subject.facts.stx_mode == mode);
    change.value.acl.bytes = NULL;
    change.value.acl.length = 0;
    assert(fc_change_admit(&subject, &before, &change, peer->uid, &projected) == 0);
    require_dispatch(peer, &subject, &before, &change, &projected);
    puts("PASS directory default ACL set/remove preserves current access ACL and mode");
    fc_subject_close(&subject);
}

static void require_benny_read(const char *path, int expected)
{
    pid_t child = fork();
    assert(child >= 0);
    if (child == 0) {
        if (setgroups(0, NULL) != 0 || setgid(1000) != 0 || setuid(1000) != 0)
            _exit(2);
        if (access(path, R_OK) == 0)
            _exit(0);
        _exit(errno == EACCES ? 1 : 2);
    }
    int status;
    assert(waitpid(child, &status, 0) == child && WIFEXITED(status));
    assert(WEXITSTATUS(status) == !expected);
}

static void native_privileged(const char *link, const char *privileged, const struct fc_peer *peer)
{
    assert(geteuid() == 0);
    struct fc_subject subject = FC_SUBJECT_INIT;
    struct fc_security_snapshot before;
    struct fc_change_projection projected;
    bind_subject(link, &subject);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0 &&
        before.subject.facts.stx_nlink == 2 && before.subject.facts.stx_uid == 0 &&
        before.access.status == FC_SECURITY_PRESENT);
    struct fc_change change = { .kind = FC_CHANGE_ACCESS_ACL, .value.acl = { NULL, 0 } };
    assert(fc_change_admit(&subject, &before, &change, 1000, &projected) == -EMLINK &&
        !projected.strictly_restrictive);
    puts("PASS removing a named restriction that exposes Other is refused on a root-owned hard link");

    require_benny_read(link, 0);
    change = (struct fc_change) { .kind = FC_CHANGE_MODE, .value.mode = 0604 };
    assert(fc_change_admit(&subject, &before, &change, 1000, &projected) == -EMLINK &&
        !projected.strictly_restrictive);
    /* Exercise the kernel rule directly in this test-owned fixture, under the fixture's root authority. */
    assert(chmod(link, 0604) == 0);
    require_benny_read(link, 1);
    require_observed(&subject, &projected);
    puts("PASS zeroing the mask can expose Other in the Linux VFS and is refused by the hard-link guard");

    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0);
    change.value.mode = 0600;
    assert(fc_change_admit(&subject, &before, &change, 1000, &projected) == 0 && projected.strictly_restrictive);
    require_dispatch(peer, &subject, &before, &change, &projected);
    require_benny_read(link, 0);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0);
    change.value.mode = 0604;
    assert(fc_change_admit(&subject, &before, &change, 1000, &projected) == -EMLINK);
    puts("PASS an added read grant is refused while a pure removal is admitted and verified on a root-owned hard link");
    fc_subject_close(&subject);

    bind_subject(privileged, &subject);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0 &&
        (before.subject.facts.stx_mode & 06000) == 06000 && before.capabilities.status == FC_SECURITY_PRESENT);
    change = (struct fc_change) { .kind = FC_CHANGE_OWNERSHIP, .value.ownership = { UINT32_MAX, 1000 } };
    assert(fc_change_admit(&subject, &before, &change, 1000, &projected) == 0 &&
        !projected.strictly_restrictive && projected.set_id_cleared == 06000 &&
        projected.capabilities_cleared && projected.after.subject.facts.stx_uid == 0);
    require_dispatch(peer, &subject, &before, &change, &projected);
    puts("PASS Linux group-only reassignment clears both reviewed execution bits and the file capability");

    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0 && (before.flags.bits & FS_NODUMP_FL));
    uint32_t original = before.flags.bits;
    uint32_t protection = FS_IMMUTABLE_FL | FS_APPEND_FL;
    change = (struct fc_change) { .kind = FC_CHANGE_FLAGS, .value.flags.set = protection };
    assert(fc_change_admit(&subject, &before, &change, peer->uid, &projected) == 0);
    require_dispatch(peer, &subject, &before, &change, &projected);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0 &&
        (before.flags.bits & protection) == protection);
    change.value.flags.set = 0;
    change.value.flags.clear = protection;
    assert(fc_change_admit(&subject, &before, &change, peer->uid, &projected) == 0);
    require_dispatch(peer, &subject, &before, &change, &projected);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &before) == 0 && before.flags.bits == original);
    puts("PASS real immutable and append-only set/clear preserves the unrelated flag word");
    fc_subject_close(&subject);
}

int main(int argc, char **argv)
{
    alarm(20);
    if (argc != 4)
        return 2;
    int partner;
    struct fc_peer peer = lifetime_guard(&partner);
    if (strcmp(argv[1], "root") == 0) {
        native_privileged(argv[2], argv[3], &peer);
    } else {
        access_oracle();
        native_access(argv[1], &peer);
        native_mode_zero(argv[2], &peer);
        native_default(argv[3], &peer);
    }
    fc_peer_close(&peer);
    assert(close(partner) == 0);
    return 0;
}
