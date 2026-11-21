#define _GNU_SOURCE
#include "change.h"
#include "access-effect.h"

#include <errno.h>
#include <linux/capability.h>
#include <linux/fs.h>
#include <linux/posix_acl.h>
#include <stdlib.h>
#include <string.h>
#include <sys/fsuid.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <unistd.h>

static const uint32_t protection = FS_IMMUTABLE_FL | FS_APPEND_FL;
enum { set_id = S_ISUID | S_ISGID, special = set_id | S_ISVTX };

static int cap_fsetid(void)
{
    struct __user_cap_header_struct version = { .version = _LINUX_CAPABILITY_VERSION_3, .pid = 0 };
    struct __user_cap_data_struct caps[_LINUX_CAPABILITY_U32S_3];
    if (syscall(SYS_capget, &version, caps) < 0)
        return -errno;
    return (caps[0].effective & (1u << CAP_FSETID)) != 0;
}

static int may_keep_setgid(uint32_t group)
{
    int capable = cap_fsetid();
    if (capable != 0)
        return capable;
    if ((gid_t)setfsgid((gid_t)-1) == group)
        return 1;
    int count = getgroups(0, NULL);
    if (count < 0)
        return -errno;
    if (count == 0)
        return 0;
    if (count > 65536)
        return -E2BIG;
    gid_t *groups = calloc((size_t)count, sizeof(*groups));
    if (!groups)
        return -ENOMEM;
    int read_count = getgroups(count, groups);
    int result = read_count != count ? -EAGAIN : 0;
    for (int i = 0; i < read_count && result == 0; ++i) {
        if (groups[i] == group)
            result = 1;
    }
    free(groups);
    return result;
}

static int known_acl(const struct fc_acl_evidence *acl)
{
    return acl->status == FC_SECURITY_PRESENT || acl->status == FC_SECURITY_ABSENT;
}

static int same_acl(const struct fc_acl_evidence *a, const struct fc_acl_evidence *b)
{
    return a->status == b->status && a->length == b->length && memcmp(a->bytes, b->bytes, a->length) == 0;
}

static int same_caps(const struct fc_capability_evidence *a, const struct fc_capability_evidence *b)
{
    return a->status == b->status && a->length == b->length && memcmp(a->bytes, b->bytes, a->length) == 0;
}

static int chmod_projection(struct fc_security_snapshot *state, unsigned permissions)
{
    struct statx *facts = &state->subject.facts;
    if ((permissions & ~07777u) != 0)
        return -EINVAL;
    if (permissions & S_ISGID) {
        int allowed = may_keep_setgid(facts->stx_gid);
        if (allowed < 0)
            return allowed;
        if (!allowed && !(facts->stx_mode & S_ISGID))
            return -EPERM;
        if (!allowed)
            permissions &= ~(unsigned)S_ISGID;
    }
    struct fc_acl_evidence *acl = &state->access;
    int has_mask = 0;
    for (size_t i = 0; i < acl->entries; ++i) {
        struct fc_acl_entry entry;
        if (fc_acl_entry_at(acl, i, &entry) < 0)
            return -EBADMSG;
        has_mask |= entry.tag == ACL_MASK;
    }
    for (size_t i = 0; i < acl->entries; ++i) {
        struct fc_acl_entry entry;
        if (fc_acl_entry_at(acl, i, &entry) < 0)
            return -EBADMSG;
        if (entry.tag == ACL_USER_OBJ)
            entry.rights = (uint16_t)((permissions >> 6) & 7);
        else if (entry.tag == ACL_MASK || (entry.tag == ACL_GROUP_OBJ && !has_mask))
            entry.rights = (uint16_t)((permissions >> 3) & 7);
        else if (entry.tag == ACL_OTHER)
            entry.rights = (uint16_t)(permissions & 7);
        acl->bytes[4 + 8 * i + 2] = (unsigned char)entry.rights;
    }
    facts->stx_mode = (uint16_t)((facts->stx_mode & S_IFMT) | permissions);
    return 0;
}

static int chown_projection(struct fc_security_snapshot *state, uint32_t uid, uint32_t gid)
{
    struct statx *facts = &state->subject.facts;
    uint32_t old_gid = facts->stx_gid;
    if (uid == UINT32_MAX)
        uid = facts->stx_uid;
    if (gid == UINT32_MAX)
        gid = facts->stx_gid;
    if (uid == facts->stx_uid && gid == old_gid)
        return -EALREADY;
    if (S_ISREG(facts->stx_mode)) {
        unsigned mode = facts->stx_mode;
        int sets_mode = (mode & S_ISUID) != 0;
        mode &= ~(unsigned)S_ISUID;
        if (mode & S_ISGID) {
            int keep_old_group = (mode & S_IXGRP) ? 0 : may_keep_setgid(old_gid);
            if (keep_old_group < 0)
                return keep_old_group;
            if (!keep_old_group) {
                mode &= ~(unsigned)S_ISGID;
                sets_mode = 1;
            }
        }
        /* When chown also changes mode, setattr_copy uses the new owning group. */
        if (sets_mode && (mode & S_ISGID)) {
            int keep_new_group = may_keep_setgid(gid);
            if (keep_new_group < 0)
                return keep_new_group;
            if (!keep_new_group)
                mode &= ~(unsigned)S_ISGID;
        }
        facts->stx_mode = (uint16_t)mode;
        memset(&state->capabilities, 0, sizeof(state->capabilities));
        state->capabilities.status = FC_SECURITY_ABSENT;
    }
    facts->stx_uid = uid;
    facts->stx_gid = gid;
    return 0;
}

static int acl_projection(struct fc_security_snapshot *state, const struct fc_change *change)
{
    int defaults = change->kind == FC_CHANGE_DEFAULT_ACL;
    struct statx *facts = &state->subject.facts;
    if (defaults && !S_ISDIR(facts->stx_mode))
        return -ENOTDIR;
    struct fc_acl_evidence *acl = defaults ? &state->defaults : &state->access;
    size_t length = change->value.acl.length;
    if (length > sizeof(acl->bytes) || (length && !change->value.acl.bytes))
        return -EINVAL;
    if (length == 0) {
        memset(acl, 0, sizeof(*acl));
        acl->status = FC_SECURITY_ABSENT;
        return 0;
    }
    memcpy(acl->bytes, change->value.acl.bytes, length);
    fc_acl_decode(acl, length);
    if (acl->status != FC_SECURITY_PRESENT)
        return -EBADMSG;
    if (defaults)
        return 0;

    unsigned owner = 0, group = 0, other = 0;
    for (size_t i = 0; i < acl->entries; ++i) {
        struct fc_acl_entry entry;
        if (fc_acl_entry_at(acl, i, &entry) < 0)
            return -EBADMSG;
        if (entry.tag == ACL_USER_OBJ)
            owner = entry.rights;
        else if (entry.tag == ACL_GROUP_OBJ || entry.tag == ACL_MASK)
            group = entry.rights;
        else if (entry.tag == ACL_OTHER)
            other = entry.rights;
    }
    unsigned mode = (facts->stx_mode & ~0777u) | (owner << 6) | (group << 3) | other;
    if (mode & S_ISGID) {
        int allowed = may_keep_setgid(facts->stx_gid);
        if (allowed < 0)
            return allowed;
        if (!allowed)
            mode &= ~(unsigned)S_ISGID;
    }
    facts->stx_mode = (uint16_t)mode;
    if (acl->entries == 3) {
        memset(acl, 0, sizeof(*acl));
        acl->status = FC_SECURITY_ABSENT;
    }
    return 0;
}

static int flag_projection(struct fc_security_snapshot *state, uint32_t set, uint32_t clear)
{
    uint32_t affected = set | clear;
    if (affected == 0 || (affected & ~protection) != 0 || (set & clear) != 0)
        return -EINVAL;
    state->flags.bits = (state->flags.bits & ~clear) | set;
    state->subject.facts.stx_attributes = (state->subject.facts.stx_attributes & ~(uint64_t)clear) | set;
    return 0;
}

static int restrictive(const struct fc_security_snapshot *before, const struct fc_security_snapshot *after,
    enum fc_change_kind kind)
{
    const struct statx *old = &before->subject.facts, *new = &after->subject.facts;
    if (((new->stx_mode & special) & ~old->stx_mode) ||
        (S_ISREG(old->stx_mode) && (new->stx_mode & 0111) && !(old->stx_mode & 0111)) ||
        (new->stx_uid != old->stx_uid && (new->stx_uid != 0 || (new->stx_mode & S_ISUID))) ||
        (new->stx_gid != old->stx_gid && (new->stx_gid != 0 || (new->stx_mode & S_ISGID))) ||
        ((new->stx_mode & (S_ISGID | S_IXGRP)) == (S_ISGID | S_IXGRP) && !(old->stx_mode & S_IXGRP)) ||
        ((before->flags.bits & protection) & ~after->flags.bits))
        return 0;
    if (kind == FC_CHANGE_FLAGS)
        return 1;
    /* Defaults govern future children, whose owning identities and creation modes are not yet known. */
    if (kind == FC_CHANGE_DEFAULT_ACL)
        return 0;
    int widens = fc_access_widens(before, after);
    return widens < 0 ? widens : !widens;
}

int fc_change_project(const struct fc_security_snapshot *before, const struct fc_change *change,
    struct fc_change_projection *projection)
{
    if (change->kind < FC_CHANGE_MODE || change->kind > FC_CHANGE_FLAGS)
        return -EINVAL;
    const struct statx *old = &before->subject.facts;
    unsigned required = FC_SECURITY_FLAGS;
    uint32_t required_flags = protection;
    if (change->kind == FC_CHANGE_FLAGS)
        required_flags = UINT32_MAX;
    else if (change->kind == FC_CHANGE_DEFAULT_ACL)
        required |= FC_SECURITY_DEFAULTS;
    else
        required |= FC_SECURITY_ACCESS;
    if (change->kind == FC_CHANGE_OWNERSHIP && S_ISREG(old->stx_mode))
        required |= FC_SECURITY_CAPABILITIES;
    if ((before->sections & required) != required || before->flags.status != FC_SECURITY_PRESENT ||
        (before->flags.known & required_flags) != required_flags ||
        ((required & FC_SECURITY_ACCESS) && !known_acl(&before->access)) ||
        ((required & FC_SECURITY_DEFAULTS) && !known_acl(&before->defaults)) ||
        ((required & FC_SECURITY_CAPABILITIES) && before->capabilities.status != FC_SECURITY_PRESENT &&
            before->capabilities.status != FC_SECURITY_ABSENT))
        return -EOPNOTSUPP;
    if (change->kind != FC_CHANGE_FLAGS && (before->flags.bits & protection))
        return -EPERM;

    *projection = (struct fc_change_projection) {
        .after = *before, .required = required, .required_flags = required_flags
    };
    struct fc_security_snapshot *after = &projection->after;
    memset(after->revision, 0, sizeof(after->revision));
    int result;
    switch (change->kind) {
    case FC_CHANGE_MODE:
        result = chmod_projection(after, change->value.mode);
        break;
    case FC_CHANGE_OWNERSHIP:
        result = chown_projection(after, change->value.ownership.uid, change->value.ownership.gid);
        break;
    case FC_CHANGE_ACCESS_ACL:
    case FC_CHANGE_DEFAULT_ACL:
        result = acl_projection(after, change);
        break;
    case FC_CHANGE_FLAGS:
        result = flag_projection(after, change->value.flags.set, change->value.flags.clear);
        break;
    default:
        return -EINVAL;
    }
    if (result < 0)
        return result;
    const struct statx *new = &after->subject.facts;
    if (old->stx_mode == new->stx_mode && old->stx_uid == new->stx_uid && old->stx_gid == new->stx_gid &&
        same_acl(&before->access, &after->access) && same_acl(&before->defaults, &after->defaults) &&
        same_caps(&before->capabilities, &after->capabilities) && before->flags.bits == after->flags.bits)
        return -EALREADY;

    result = restrictive(before, after, change->kind);
    if (result < 0)
        return result;
    projection->strictly_restrictive = result;
    projection->set_id_cleared = (old->stx_mode & set_id) & ~new->stx_mode;
    projection->capabilities_cleared = before->capabilities.status == FC_SECURITY_PRESENT &&
        after->capabilities.status == FC_SECURITY_ABSENT;
    return 0;
}

int fc_change_admit(const struct fc_subject *subject, const struct fc_security_snapshot *before,
    const struct fc_change *change, uid_t requester, struct fc_change_projection *projection)
{
    int result = fc_change_project(before, change, projection);
    return result < 0 ? result : fc_security_check_write(subject, before, projection->required,
        projection->required_flags, requester, projection->strictly_restrictive);
}

int fc_change_matches(const struct fc_change_projection *projection, const struct fc_security_snapshot *observed)
{
    const struct fc_security_snapshot *after = &projection->after;
    struct fc_subject_state subject = observed->subject;
    /* ctime is assigned by the filesystem. Every other compared subject field must match. */
    subject.facts.stx_ctime = after->subject.facts.stx_ctime;
    if (after->sections != observed->sections || !fc_subject_state_matches(&after->subject, &subject) ||
        !same_acl(&after->access, &observed->access) || !same_acl(&after->defaults, &observed->defaults) ||
        !same_caps(&after->capabilities, &observed->capabilities))
        return 0;
    return after->flags.status == observed->flags.status &&
        (after->flags.known & observed->flags.known) == after->flags.known &&
        ((after->flags.bits ^ observed->flags.bits) & after->flags.known) == 0;
}
