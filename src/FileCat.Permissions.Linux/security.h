#ifndef FILECAT_PERMISSIONS_SECURITY_H
#define FILECAT_PERMISSIONS_SECURITY_H

#include "subject.h"

enum {
    FC_SECURITY_ACCESS = 1,
    FC_SECURITY_DEFAULTS = 2,
    FC_SECURITY_CAPABILITIES = 4,
    FC_SECURITY_FLAGS = 8,
    FC_SECURITY_ALL = 15,
    FC_ACL_MAX_ENTRIES = 4096,
    FC_ACL_MAX_BYTES = 4 + 8 * FC_ACL_MAX_ENTRIES,
    FC_CAPABILITY_MAX_BYTES = 24,
    FC_SECURITY_REVISION_BYTES = 32
};

enum fc_security_status {
    FC_SECURITY_UNREQUESTED,
    FC_SECURITY_PRESENT,
    FC_SECURITY_ABSENT,
    FC_SECURITY_NOT_APPLICABLE,
    FC_SECURITY_DENIED,
    FC_SECURITY_UNSUPPORTED,
    FC_SECURITY_INCOMPLETE,
    FC_SECURITY_OPAQUE,
    FC_SECURITY_FAILED
};

struct fc_acl_entry {
    uint16_t tag, rights;
    uint32_t id;
};

struct fc_acl_evidence {
    enum fc_security_status status;
    int error;
    size_t length, entries;
    unsigned char bytes[FC_ACL_MAX_BYTES];
};

struct fc_capability_evidence {
    enum fc_security_status status;
    int error;
    size_t length;
    unsigned char bytes[FC_CAPABILITY_MAX_BYTES];
    unsigned version, effective;
    uint64_t permitted, inheritable;
    uint32_t root_id;
};

struct fc_flag_evidence {
    enum fc_security_status status;
    int error;
    uint32_t bits, known;
};

struct fc_security_snapshot {
    struct fc_subject_state subject;
    unsigned sections;
    struct fc_acl_evidence access, defaults;
    struct fc_capability_evidence capabilities;
    struct fc_flag_evidence flags;
    unsigned char revision[FC_SECURITY_REVISION_BYTES];
};

/* Decode native bytes already copied into the corresponding evidence buffer. */
void fc_acl_decode(struct fc_acl_evidence *acl, size_t length);
int fc_acl_entry_at(const struct fc_acl_evidence *acl, size_t index, struct fc_acl_entry *entry);
void fc_capabilities_decode(struct fc_capability_evidence *capabilities, size_t length);

/* The already admitted subject retains its metadata pin while the selected sections are read. */
int fc_security_capture(const struct fc_subject *subject, unsigned sections, struct fc_security_snapshot *snapshot);
/* The mechanism supplies the reviewed snapshot and determines the dependencies and effect. */
int fc_security_check_write(const struct fc_subject *subject, const struct fc_security_snapshot *reviewed,
    unsigned required, uint32_t required_flags, uid_t requester, int strictly_restrictive);

#endif
