#ifndef FILECAT_PERMISSIONS_BROKER_WIRE_H
#define FILECAT_PERMISSIONS_BROKER_WIRE_H

#include "change-dispatch.h"
#include <systemd/sd-bus.h>

#define FC_BROKER_NAME "org.filecat.Permissions"
#define FC_BROKER_PATH "/org/filecat/Permissions"
#define FC_BROKER_INTERFACE "org.filecat.Permissions1"
#define FC_BROKER_OUTCOME_UNAVAILABLE "org.filecat.Permissions1.OutcomeUnavailable"
#define FC_BROKER_NO_AUTHENTICATION_AGENT "org.filecat.Permissions1.NoAuthenticationAgent"

#define FC_WIRE_BEGIN "u"
#define FC_WIRE_BEGIN_REPLY "ayh"
#define FC_WIRE_CANCEL "uay"
#define FC_WIRE_INSPECT "uayayh(ttuuq)u"
#define FC_WIRE_APPLY FC_WIRE_INSPECT "ayuv"
#define FC_WIRE_SNAPSHOT_FIELDS "u(ttuuq)(uuuuttttxuxu)ay(uiay)(uiay)(uiay)(uiuu)"
#define FC_WIRE_SNAPSHOT "(" FC_WIRE_SNAPSHOT_FIELDS ")"
#define FC_WIRE_APPLY_REPLY "uiia" FC_WIRE_SNAPSHOT

enum { FC_BROKER_VERSION = 1, FC_BROKER_TOKEN_BYTES = 32 };

struct fc_wire_operation {
    char path[FC_SUBJECT_PATH_BYTES];
    struct fc_subject_identity identity;
    int descriptor; /* Borrowed from the method message until subject binding duplicates it. */
    unsigned sections;
    unsigned char revision[FC_SECURITY_REVISION_BYTES];
    struct fc_change change;
    unsigned char acl[FC_ACL_MAX_BYTES];
};

int fc_wire_read_version(sd_bus_message *message);
int fc_wire_read_token(sd_bus_message *message, unsigned char token[FC_BROKER_TOKEN_BYTES]);
/* The caller has already consumed the version and matching request token. */
int fc_wire_read_operation(sd_bus_message *message, int apply, struct fc_wire_operation *operation);
int fc_wire_reply_begin(sd_bus_message *call, const unsigned char token[FC_BROKER_TOKEN_BYTES], int endpoint);
int fc_wire_reply_inspect(sd_bus_message *call, const struct fc_security_snapshot *observed);
int fc_wire_reply_apply(sd_bus_message *call, const struct fc_write_result *result);

#endif
