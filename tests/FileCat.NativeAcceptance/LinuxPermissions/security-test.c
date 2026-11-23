#define _GNU_SOURCE
#include "security.h"

#include <assert.h>
#include <errno.h>
#include <fcntl.h>
#include <linux/fs.h>
#include <linux/posix_acl.h>
#include <stdio.h>
#include <string.h>
#include <sys/ioctl.h>
#include <sys/stat.h>
#include <sys/xattr.h>
#include <unistd.h>

#ifndef STATX_MNT_ID_UNIQUE
#define STATX_MNT_ID_UNIQUE 0x4000U
#endif

static void acl_row(unsigned char *bytes, size_t index, unsigned tag, unsigned rights, uint32_t uid)
{
    unsigned char *row = bytes + 4 + 8 * index;
    row[0] = (unsigned char)tag;
    row[1] = 0;
    row[2] = (unsigned char)rights;
    row[3] = 0;
    for (unsigned i = 0; i < 4; ++i)
        row[4 + i] = (unsigned char)(uid >> (8 * i));
}

static void codecs(void)
{
    struct fc_acl_evidence acl = { .bytes = { 2, 0, 0, 0 } };
    acl_row(acl.bytes, 0, ACL_USER_OBJ, 6, UINT32_MAX);
    acl_row(acl.bytes, 1, ACL_USER, 4, 12345);
    acl_row(acl.bytes, 2, ACL_GROUP_OBJ, 4, UINT32_MAX);
    acl_row(acl.bytes, 3, ACL_MASK, 4, UINT32_MAX);
    acl_row(acl.bytes, 4, ACL_OTHER, 0, UINT32_MAX);
    fc_acl_decode(&acl, 44);
    struct fc_acl_entry entry;
    assert(acl.status == FC_SECURITY_PRESENT && acl.entries == 5);
    assert(fc_acl_entry_at(&acl, 1, &entry) == 0 && entry.tag == ACL_USER && entry.id == 12345 && entry.rights == 4);
    acl.bytes[0] = 3;
    fc_acl_decode(&acl, 44);
    assert(acl.status == FC_SECURITY_OPAQUE && acl.length == 44 && acl.bytes[0] == 3);
    acl.bytes[0] = 2;
    acl_row(acl.bytes, 1, 3, 4, 12345);
    fc_acl_decode(&acl, 44);
    assert(acl.status == FC_SECURITY_OPAQUE && acl.length == 44);
    acl_row(acl.bytes, 1, ACL_USER, 8, 12345);
    fc_acl_decode(&acl, 44);
    assert(acl.status == FC_SECURITY_INCOMPLETE);
    acl_row(acl.bytes, 1, ACL_USER, 4, UINT32_MAX);
    fc_acl_decode(&acl, 44);
    assert(acl.status == FC_SECURITY_INCOMPLETE);
    acl_row(acl.bytes, 1, ACL_USER, 4, 12345);
    acl_row(acl.bytes, 3, ACL_OTHER, 0, UINT32_MAX);
    fc_acl_decode(&acl, 36);
    assert(acl.status == FC_SECURITY_INCOMPLETE);
    fc_acl_decode(&acl, 35);
    assert(acl.status == FC_SECURITY_INCOMPLETE);
    acl_row(acl.bytes, 2, ACL_USER, 4, 12345);
    acl_row(acl.bytes, 3, ACL_GROUP_OBJ, 4, UINT32_MAX);
    acl_row(acl.bytes, 4, ACL_MASK, 4, UINT32_MAX);
    acl_row(acl.bytes, 5, ACL_OTHER, 0, UINT32_MAX);
    fc_acl_decode(&acl, 52);
    assert(acl.status == FC_SECURITY_INCOMPLETE && acl.entries == 0);
    fc_acl_decode(&acl, sizeof(acl.bytes) + 1);
    assert(acl.status == FC_SECURITY_INCOMPLETE && acl.error == E2BIG && acl.length == 0);
    puts("PASS ACL codec accepts canonical entries and distinguishes opaque and malformed attributes");

    struct fc_capability_evidence capability = {
        .bytes = { 1, 0, 0, 3, 0, 4, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0xe8, 3, 0, 0 }
    };
    fc_capabilities_decode(&capability, 24);
    assert(capability.status == FC_SECURITY_PRESENT && capability.version == 3 && capability.effective &&
        capability.permitted == ((UINT64_C(2) << 32) | 1024) && capability.inheritable == 1 &&
        capability.root_id == 1000);
    capability.bytes[3] = 2;
    fc_capabilities_decode(&capability, 20);
    assert(capability.status == FC_SECURITY_PRESENT && capability.version == 2);
    capability.bytes[3] = 1;
    fc_capabilities_decode(&capability, 12);
    assert(capability.status == FC_SECURITY_PRESENT && capability.version == 1 && capability.permitted == 1024);
    capability.bytes[0] = 2;
    fc_capabilities_decode(&capability, 12);
    assert(capability.status == FC_SECURITY_OPAQUE);
    capability.bytes[0] = 1;
    fc_capabilities_decode(&capability, 13);
    assert(capability.status == FC_SECURITY_INCOMPLETE);
    puts("PASS capability codec preserves versioned IDs and both sets of bits");
}

static void bind(const char *path, struct fc_subject *subject)
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
    close(descriptor);
}

static void revise_named_user(const char *path, const char *attribute, struct fc_acl_evidence *before)
{
    struct fc_acl_entry user;
    assert(fc_acl_entry_at(before, 1, &user) == 0 && user.tag == ACL_USER && user.id == 12345);
    unsigned char revised[FC_ACL_MAX_BYTES];
    memcpy(revised, before->bytes, before->length);
    acl_row(revised, 1, ACL_USER, user.rights ^ ACL_WRITE, user.id);
    assert(setxattr(path, attribute, revised, before->length, XATTR_REPLACE) == 0);
}

static void acl_snapshot(const char *path)
{
    struct fc_subject subject = FC_SUBJECT_INIT;
    struct fc_security_snapshot reviewed, observed;
    char cwd[FC_SUBJECT_PATH_BYTES], after[FC_SUBJECT_PATH_BYTES];
    assert(getcwd(cwd, sizeof(cwd)));
    bind(path, &subject);
    assert(fc_security_capture(&subject, FC_SECURITY_ACCESS, &reviewed) == 0);
    assert(reviewed.access.status == FC_SECURITY_PRESENT && reviewed.defaults.status == FC_SECURITY_UNREQUESTED &&
        reviewed.capabilities.status == FC_SECURITY_UNREQUESTED && reviewed.flags.status == FC_SECURITY_UNREQUESTED);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &observed) == 0);
    assert(memcmp(reviewed.revision, observed.revision, sizeof(reviewed.revision)) != 0);
    assert((observed.subject.facts.stx_mode & 0777) == 0 &&
        observed.defaults.status == FC_SECURITY_NOT_APPLICABLE && observed.capabilities.status == FC_SECURITY_ABSENT);
    assert(observed.flags.status == FC_SECURITY_PRESENT && observed.flags.known == (FS_IMMUTABLE_FL | FS_APPEND_FL) &&
        observed.flags.error == EACCES);
    assert(getcwd(after, sizeof(after)) && strcmp(cwd, after) == 0);
    assert(fc_security_check_write(&subject, &reviewed, FC_SECURITY_DEFAULTS, 0, getuid(), 0) == -EINVAL);
    assert(fc_security_check_write(&subject, &observed, FC_SECURITY_FLAGS, UINT32_MAX, getuid(), 0) == -EOPNOTSUPP);
    assert(fc_security_check_write(&subject, &observed, FC_SECURITY_ACCESS | FC_SECURITY_FLAGS,
        FS_IMMUTABLE_FL | FS_APPEND_FL, getuid(), 0) == 0);
    puts("PASS mode-000 ACL read through verified procfs with independent section and flag evidence");

    revise_named_user(path, "system.posix_acl_access", &reviewed.access);
    assert(fc_security_check_write(&subject, &reviewed, FC_SECURITY_ACCESS, 0, getuid(), 0) == -ESTALE);
    assert(fc_security_capture(&subject, FC_SECURITY_ACCESS, &observed) == 0 &&
        observed.subject.facts.stx_mode == reviewed.subject.facts.stx_mode);
    struct fc_acl_entry entry;
    assert(fc_acl_entry_at(&observed.access, 1, &entry) == 0 && entry.rights == (ACL_READ | ACL_WRITE));
    fc_subject_close(&subject);
    puts("PASS a named ACL change with identical visible mode invalidates the review");
}

static void defaults_snapshot(const char *path)
{
    struct fc_subject subject = FC_SUBJECT_INIT;
    struct fc_security_snapshot reviewed, observed;
    bind(path, &subject);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &reviewed) == 0);
    assert(reviewed.access.status == FC_SECURITY_ABSENT && reviewed.defaults.status == FC_SECURITY_PRESENT &&
        reviewed.capabilities.status == FC_SECURITY_NOT_APPLICABLE);
    revise_named_user(path, "system.posix_acl_default", &reviewed.defaults);
    assert(fc_security_check_write(&subject, &reviewed, FC_SECURITY_DEFAULTS, 0, getuid(), 0) == -ESTALE);
    assert(fc_security_capture(&subject, FC_SECURITY_DEFAULTS, &observed) == 0 &&
        observed.subject.facts.stx_mode == reviewed.subject.facts.stx_mode);
    fc_subject_close(&subject);
    puts("PASS directory default ACL evidence and drift are independent of current access");
}

static void flags_snapshot(const char *path)
{
    struct fc_subject subject = FC_SUBJECT_INIT;
    struct fc_security_snapshot reviewed, observed;
    bind(path, &subject);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &reviewed) == 0);
    assert(reviewed.access.status == FC_SECURITY_ABSENT && reviewed.flags.status == FC_SECURITY_PRESENT &&
        reviewed.flags.known == UINT32_MAX);
    int data = open(path, O_RDONLY | O_CLOEXEC | O_NONBLOCK), flags;
    assert(data >= 0 && ioctl(data, FS_IOC_GETFLAGS, &flags) == 0 && (uint32_t)flags == reviewed.flags.bits);
    int changed = flags ^ FS_NODUMP_FL;
    assert(ioctl(data, FS_IOC_SETFLAGS, &changed) == 0);
    assert(fc_security_check_write(&subject, &reviewed, FC_SECURITY_FLAGS, UINT32_MAX, getuid(), 0) == -ESTALE);
    assert(fc_security_capture(&subject, FC_SECURITY_FLAGS, &observed) == 0 && observed.flags.bits == (uint32_t)changed);
    assert(ioctl(data, FS_IOC_SETFLAGS, &flags) == 0);
    close(data);
    fc_subject_close(&subject);
    puts("PASS complete inode flag word is retained and an unrelated flag change invalidates review");
}

static void root_snapshot(const char *path)
{
    assert(geteuid() == 0);
    struct fc_subject subject = FC_SUBJECT_INIT;
    struct fc_security_snapshot reviewed, observed;
    bind(path, &subject);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &reviewed) == 0);
    assert((reviewed.subject.facts.stx_mode & 0777) == 0 && reviewed.flags.known == UINT32_MAX &&
        (reviewed.flags.bits & FS_IMMUTABLE_FL) && reviewed.capabilities.status == FC_SECURITY_PRESENT &&
        reviewed.capabilities.version == 3 && reviewed.capabilities.root_id == 1000 &&
        reviewed.capabilities.permitted == 1024 && reviewed.capabilities.effective);
    int data = open(path, O_RDONLY | O_CLOEXEC | O_NONBLOCK), flags;
    assert(data >= 0 && ioctl(data, FS_IOC_GETFLAGS, &flags) == 0);
    flags &= ~FS_IMMUTABLE_FL;
    assert(ioctl(data, FS_IOC_SETFLAGS, &flags) == 0);
    close(data);
    assert(fc_security_check_write(&subject, &reviewed, FC_SECURITY_FLAGS, UINT32_MAX, 1000, 0) == -ESTALE);
    assert(fc_security_capture(&subject, FC_SECURITY_ALL, &reviewed) == 0);
    assert(removexattr(path, "security.capability") == 0);
    assert(fc_security_check_write(&subject, &reviewed, FC_SECURITY_CAPABILITIES, 0, 1000, 0) == -ESTALE);
    assert(fc_security_capture(&subject, FC_SECURITY_CAPABILITIES, &observed) == 0 &&
        observed.capabilities.status == FC_SECURITY_ABSENT);
    fc_subject_close(&subject);
    puts("PASS privileged mode-000 observation retains real immutable and namespace capability evidence");
}

int main(int argc, char **argv)
{
    alarm(20);
    if (argc == 4) {
        assert(geteuid() != 0);
        codecs();
        acl_snapshot(argv[1]);
        defaults_snapshot(argv[2]);
        flags_snapshot(argv[3]);
    } else if (argc == 3 && strcmp(argv[1], "root") == 0)
        root_snapshot(argv[2]);
    else
        return 2;
    return 0;
}
