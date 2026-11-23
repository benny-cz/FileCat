#define _GNU_SOURCE
#include "change-dispatch.h"
#include "subject-alias.h"

#include <errno.h>
#include <fcntl.h>
#include <linux/fs.h>
#include <string.h>
#include <sys/ioctl.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <sys/xattr.h>
#include <unistd.h>

/* Ubuntu's libc headers may predate the native x64 syscall. Other ABIs use their own headers. */
#if !defined(SYS_fchmodat2) && defined(__x86_64__) && !defined(__ILP32__)
#define SYS_fchmodat2 452
#endif

struct dispatch {
    const struct fc_peer *peer;
    const struct fc_security_snapshot *before;
    const struct fc_change *change;
    int strictly_restrictive, attempted;
};

static int change_acl(const char *alias, struct dispatch *operation)
{
    int result = fc_peer_live(operation->peer);
    if (result < 0)
        return result;
    const struct fc_change *change = operation->change;
    const char *name = change->kind == FC_CHANGE_ACCESS_ACL ? "system.posix_acl_access" : "system.posix_acl_default";
    operation->attempted = 1;
    result = change->value.acl.length == 0 ? removexattr(alias, name) :
        setxattr(alias, name, change->value.acl.bytes, change->value.acl.length, 0);
    return result < 0 ? -errno : 0;
}

static int change_flags(const char *alias, struct dispatch *operation)
{
    const struct fc_security_snapshot *before = operation->before;
    if (before->flags.known != UINT32_MAX || before->flags.status != FC_SECURITY_PRESENT)
        return -EOPNOTSUPP;
    int descriptor = open(alias, O_RDONLY | O_NONBLOCK | O_NOCTTY | O_CLOEXEC);
    if (descriptor < 0)
        return -errno;
    int result = fc_subject_verify_proc_alias(descriptor, "", &before->subject);
    uint32_t native;
    if (result == 0) {
        if (ioctl(descriptor, FS_IOC_GETFLAGS, &native) < 0)
            result = -errno;
        else if (native != before->flags.bits)
            result = -ESTALE;
    }
    if (result == 0)
        result = fc_peer_live(operation->peer);
    if (result == 0) {
        const struct fc_change *change = operation->change;
        uint32_t updated = (native & ~change->value.flags.clear) | change->value.flags.set;
        operation->attempted = 1;
        if (ioctl(descriptor, FS_IOC_SETFLAGS, &updated) < 0)
            result = -errno;
    }
    close(descriptor);
    return result;
}

static int metadata_change(int directory, const char *alias, void *argument)
{
    (void)directory;
    struct dispatch *operation = argument;
    if (operation->change->kind == FC_CHANGE_FLAGS)
        return change_flags(alias, operation);
    return change_acl(alias, operation);
}

static int execute(const struct fc_subject *subject, struct dispatch *operation)
{
    int result = fc_subject_check_write(subject, &operation->before->subject, operation->peer->uid,
        operation->strictly_restrictive);
    if (result < 0)
        return result;
    result = fc_peer_live(operation->peer);
    if (result < 0)
        return result;
    const struct fc_change *change = operation->change;
    switch (change->kind) {
    case FC_CHANGE_MODE:
#ifdef SYS_fchmodat2
        operation->attempted = 1;
        return syscall(SYS_fchmodat2, subject->pin, "", change->value.mode, AT_EMPTY_PATH) < 0 ? -errno : 0;
#else
        return -EOPNOTSUPP;
#endif
    case FC_CHANGE_OWNERSHIP:
        operation->attempted = 1;
        return fchownat(subject->pin, "", change->value.ownership.uid, change->value.ownership.gid, AT_EMPTY_PATH) < 0 ?
            -errno : 0;
    case FC_CHANGE_ACCESS_ACL:
    case FC_CHANGE_DEFAULT_ACL:
    case FC_CHANGE_FLAGS:
        return fc_subject_with_alias(subject, &operation->before->subject, metadata_change, operation);
    }
    return -EINVAL;
}

void fc_change_dispatch(const struct fc_peer *peer, const struct fc_subject *subject,
    const struct fc_security_snapshot *before, const struct fc_change *change,
    const struct fc_change_projection *projected, struct fc_write_result *result)
{
    *result = (struct fc_write_result) { .state = FC_WRITE_NOT_SENT };
    struct dispatch operation = {
        .peer = peer, .before = before, .change = change,
        .strictly_restrictive = projected->strictly_restrictive
    };
    result->operation_error = execute(subject, &operation);
    if (!operation.attempted)
        return;

    /* A syscall error alone is insufficient evidence about the inode. Always try native read-back. */
    result->state = FC_WRITE_UNCERTAIN;
    result->verification_error = fc_security_capture(subject, projected->after.sections, &result->observed);
    if (result->verification_error < 0)
        return;
    result->has_observation = 1;
    if (result->operation_error == 0 && fc_change_matches(projected, &result->observed)) {
        result->state = FC_WRITE_VERIFIED;
    } else if (result->operation_error < 0 &&
               memcmp(before->revision, result->observed.revision, sizeof(before->revision)) == 0) {
        result->state = FC_WRITE_REJECTED;
    } else {
        result->verification_error = -ESTALE;
    }
}
