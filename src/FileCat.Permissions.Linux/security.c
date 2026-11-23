#define _GNU_SOURCE
#include "security.h"
#include "subject-alias.h"

#include <endian.h>
#include <errno.h>
#include <fcntl.h>
#include <linux/fs.h>
#include <linux/posix_acl.h>
#include <openssl/evp.h>
#include <string.h>
#include <sys/ioctl.h>
#include <sys/stat.h>
#include <sys/xattr.h>
#include <unistd.h>

_Static_assert(FS_IMMUTABLE_FL == STATX_ATTR_IMMUTABLE && FS_APPEND_FL == STATX_ATTR_APPEND,
    "Linux ioctl and statx inode protection bits");

static enum fc_security_status error_status(int error)
{
    switch (error) {
    case ENODATA: return FC_SECURITY_ABSENT;
    case EACCES:
    case EPERM: return FC_SECURITY_DENIED;
    case EOPNOTSUPP:
    case ENOTTY:
    case ENOSYS: return FC_SECURITY_UNSUPPORTED;
    case E2BIG:
    case ERANGE: return FC_SECURITY_INCOMPLETE;
    default: return FC_SECURITY_FAILED;
    }
}

static void read_acl(const char *alias, const char *attribute, struct fc_acl_evidence *acl)
{
    ssize_t size = getxattr(alias, attribute, acl->bytes, sizeof(acl->bytes));
    if (size >= 0) {
        fc_acl_decode(acl, (size_t)size);
        return;
    }
    int error = errno;
    acl->error = error == ENODATA ? 0 : error;
    acl->status = error_status(error);
}

static void read_capabilities(const char *alias, struct fc_capability_evidence *caps)
{
    ssize_t size = getxattr(alias, "security.capability", caps->bytes, sizeof(caps->bytes));
    if (size >= 0) {
        fc_capabilities_decode(caps, (size_t)size);
        return;
    }
    int error = errno;
    caps->error = error == ENODATA ? 0 : error;
    caps->status = error_status(error);
}

static int read_flags(const char *alias, const struct fc_subject_state *subject, struct fc_flag_evidence *flags)
{
    const uint32_t protection = FS_IMMUTABLE_FL | FS_APPEND_FL;
    const uint32_t known = (uint32_t)subject->facts.stx_attributes_mask & protection;
    const uint32_t observed = (uint32_t)subject->facts.stx_attributes & known;
    const int descriptor = open(alias, O_RDONLY | O_NONBLOCK | O_CLOEXEC);
    int native_flags = 0, error = 0;
    if (descriptor < 0) {
        error = errno;
    } else {
        int result = fc_subject_verify_proc_alias(descriptor, "", subject);
        if (result < 0) {
            close(descriptor);
            return result;
        }
        if (ioctl(descriptor, FS_IOC_GETFLAGS, &native_flags) < 0)
            error = errno;
        close(descriptor);
    }
    if (!error && ((uint32_t)native_flags & known) != observed)
        return -ESTALE;
    flags->error = error;
    flags->known = error ? known : UINT32_MAX;
    flags->bits = error ? observed : (uint32_t)native_flags;
    flags->status = flags->known ? FC_SECURITY_PRESENT : error_status(error);
    return 0;
}

static int read_sections(int directory, const char *alias, void *argument)
{
    struct fc_security_snapshot *snapshot = argument;
    int directory_subject = S_ISDIR(snapshot->subject.facts.stx_mode);
    if (snapshot->sections & FC_SECURITY_ACCESS)
        read_acl(alias, "system.posix_acl_access", &snapshot->access);
    if (snapshot->sections & FC_SECURITY_DEFAULTS) {
        if (directory_subject)
            read_acl(alias, "system.posix_acl_default", &snapshot->defaults);
        else
            snapshot->defaults.status = FC_SECURITY_NOT_APPLICABLE;
    }
    if (snapshot->sections & FC_SECURITY_CAPABILITIES) {
        if (directory_subject)
            snapshot->capabilities.status = FC_SECURITY_NOT_APPLICABLE;
        else
            read_capabilities(alias, &snapshot->capabilities);
    }
    if (snapshot->sections & FC_SECURITY_FLAGS) {
        int result = read_flags(alias, &snapshot->subject, &snapshot->flags);
        if (result < 0)
            return result;
    }
    return fc_subject_verify_proc_alias(directory, alias, &snapshot->subject);
}

static int matches_mode(const struct fc_acl_evidence *acl, unsigned mode)
{
    unsigned owner = 0, group = 0, other = 0;
    for (size_t i = 0; i < acl->entries; ++i) {
        struct fc_acl_entry entry;
        if (fc_acl_entry_at(acl, i, &entry) < 0)
            return 0;
        switch (entry.tag) {
        case ACL_USER_OBJ: owner = entry.rights; break;
        case ACL_GROUP_OBJ:
        case ACL_MASK: group = entry.rights; break;
        case ACL_OTHER: other = entry.rights; break;
        default: break;
        }
    }
    return ((owner << 6) | (group << 3) | other) == (mode & 0777);
}

static int add_values(EVP_MD_CTX *context, const uint64_t *values, size_t count)
{
    for (size_t i = 0; i < count; ++i) {
        uint64_t encoded = htole64(values[i]);
        if (EVP_DigestUpdate(context, &encoded, sizeof(encoded)) != 1)
            return 0;
    }
    return 1;
}

static int add_bytes(EVP_MD_CTX *context, enum fc_security_status status, int error,
    const unsigned char *bytes, size_t length)
{
    const uint64_t header[] = { (unsigned)status, (unsigned)error, length };
    return add_values(context, header, sizeof(header) / sizeof(header[0])) &&
        EVP_DigestUpdate(context, bytes, length) == 1;
}

static int make_revision(struct fc_security_snapshot *snapshot)
{
    static const char domain[] = "FileCat Linux security revision v1";
    const struct statx *facts = &snapshot->subject.facts;
    const uint64_t subject[] = {
        snapshot->sections, facts->stx_mnt_id, facts->stx_dev_major, facts->stx_dev_minor, facts->stx_ino,
        facts->stx_mode, facts->stx_uid, facts->stx_gid, facts->stx_nlink, facts->stx_size,
        (uint64_t)facts->stx_ctime.tv_sec, facts->stx_ctime.tv_nsec,
        (uint64_t)facts->stx_mtime.tv_sec, facts->stx_mtime.tv_nsec,
        facts->stx_attributes_mask, facts->stx_attributes, snapshot->subject.mount_attributes
    };
    const uint64_t flags[] = {
        (unsigned)snapshot->flags.status, (unsigned)snapshot->flags.error, snapshot->flags.known, snapshot->flags.bits
    };
    EVP_MD_CTX *digest = EVP_MD_CTX_new();
    if (!digest)
        return -ENOMEM;
    unsigned length = 0;
    int success = EVP_DigestInit_ex(digest, EVP_sha256(), NULL) == 1 &&
        EVP_DigestUpdate(digest, domain, sizeof(domain) - 1) == 1 &&
        add_values(digest, subject, sizeof(subject) / sizeof(subject[0])) &&
        add_bytes(digest, snapshot->access.status, snapshot->access.error, snapshot->access.bytes, snapshot->access.length) &&
        add_bytes(digest, snapshot->defaults.status, snapshot->defaults.error, snapshot->defaults.bytes, snapshot->defaults.length) &&
        add_bytes(digest, snapshot->capabilities.status, snapshot->capabilities.error,
            snapshot->capabilities.bytes, snapshot->capabilities.length) &&
        add_values(digest, flags, sizeof(flags) / sizeof(flags[0])) &&
        EVP_DigestFinal_ex(digest, snapshot->revision, &length) == 1 && length == FC_SECURITY_REVISION_BYTES;
    EVP_MD_CTX_free(digest);
    return success ? 0 : -EIO;
}

int fc_security_capture(const struct fc_subject *subject, unsigned sections, struct fc_security_snapshot *snapshot)
{
    if ((sections & ~(unsigned)FC_SECURITY_ALL) != 0)
        return -EINVAL;
    memset(snapshot, 0, sizeof(*snapshot));
    snapshot->sections = sections;
    int result = fc_subject_observe(subject, &snapshot->subject);
    if (result < 0)
        return result;
    if (sections != 0) {
        result = fc_subject_with_alias(subject, &snapshot->subject, read_sections, snapshot);
        if (result < 0)
            return result;
        struct fc_subject_state current;
        result = fc_subject_observe(subject, &current);
        if (result < 0)
            return result;
        if (!fc_subject_state_matches(&snapshot->subject, &current) ||
            (snapshot->access.status == FC_SECURITY_PRESENT &&
             !matches_mode(&snapshot->access, current.facts.stx_mode)))
            return -ESTALE;
    }
    return make_revision(snapshot);
}

static int known(enum fc_security_status status)
{
    return status == FC_SECURITY_PRESENT || status == FC_SECURITY_ABSENT || status == FC_SECURITY_NOT_APPLICABLE;
}

int fc_security_check_write(const struct fc_subject *subject, const struct fc_security_snapshot *reviewed,
    unsigned required, uint32_t required_flags, uid_t requester, int strictly_restrictive)
{
    if ((required & ~reviewed->sections) != 0 || (required_flags && !(required & FC_SECURITY_FLAGS)))
        return -EINVAL;
    if (((required & FC_SECURITY_ACCESS) && !known(reviewed->access.status)) ||
        ((required & FC_SECURITY_DEFAULTS) && !known(reviewed->defaults.status)) ||
        ((required & FC_SECURITY_CAPABILITIES) && !known(reviewed->capabilities.status)) ||
        ((required & FC_SECURITY_FLAGS) && (reviewed->flags.status != FC_SECURITY_PRESENT ||
            (reviewed->flags.known & required_flags) != required_flags)))
        return -EOPNOTSUPP;
    struct fc_security_snapshot current;
    int result = fc_security_capture(subject, reviewed->sections, &current);
    if (result < 0)
        return result;
    if (memcmp(reviewed->revision, current.revision, sizeof(current.revision)) != 0)
        return -ESTALE;
    return fc_subject_check_write(subject, &current.subject, requester, strictly_restrictive);
}
