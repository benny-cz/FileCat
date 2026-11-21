#ifndef FILECAT_PERMISSIONS_CHANGE_DISPATCH_H
#define FILECAT_PERMISSIONS_CHANGE_DISPATCH_H

#include "change.h"
#include "peer.h"

enum fc_write_state {
    FC_WRITE_NOT_SENT,
    FC_WRITE_REJECTED,
    FC_WRITE_VERIFIED,
    FC_WRITE_UNCERTAIN
};

struct fc_write_result {
    enum fc_write_state state;
    int operation_error, verification_error;
    int has_observation;
    struct fc_security_snapshot observed;
};

/* The authorization caller supplies the unchanged baseline and the projection it has just rechecked. */
void fc_change_dispatch(const struct fc_peer *peer, const struct fc_subject *subject,
    const struct fc_security_snapshot *before, const struct fc_change *change,
    const struct fc_change_projection *projected, struct fc_write_result *result);

#endif
