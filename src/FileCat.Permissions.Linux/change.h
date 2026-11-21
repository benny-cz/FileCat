#ifndef FILECAT_PERMISSIONS_CHANGE_H
#define FILECAT_PERMISSIONS_CHANGE_H

#include "security.h"

enum fc_change_kind {
    FC_CHANGE_MODE,
    FC_CHANGE_OWNERSHIP,
    FC_CHANGE_ACCESS_ACL,
    FC_CHANGE_DEFAULT_ACL,
    FC_CHANGE_FLAGS
};

struct fc_change {
    enum fc_change_kind kind;
    union {
        unsigned mode;
        struct { uint32_t uid, gid; } ownership; /* UINT32_MAX keeps that ID. */
        struct { const unsigned char *bytes; size_t length; } acl;
        struct { uint32_t set, clear; } flags;
    } value;
};

struct fc_change_projection {
    /* Predicted security facts. A fresh observation supplies the actual revision after a write. */
    struct fc_security_snapshot after;
    unsigned required;
    uint32_t required_flags;
    unsigned set_id_cleared;
    int capabilities_cleared;
    int strictly_restrictive;
};

/* Project one ext4 change using the native credential that will dispatch it. */
int fc_change_project(const struct fc_security_snapshot *before, const struct fc_change *change,
    struct fc_change_projection *projection);
int fc_change_admit(const struct fc_subject *subject, const struct fc_security_snapshot *before,
    const struct fc_change *change, uid_t requester, struct fc_change_projection *projection);
int fc_change_matches(const struct fc_change_projection *projection, const struct fc_security_snapshot *observed);

#endif
