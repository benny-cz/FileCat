#ifndef FILECAT_PERMISSIONS_POLKIT_REQUEST_H
#define FILECAT_PERMISSIONS_POLKIT_REQUEST_H

#include "bus-peer.h"
#include "change-dispatch.h"
#include "consent.h"

struct fc_polkit_request;
enum { FC_POLKIT_NO_AUTHENTICATION_AGENT = 1 };
typedef void (*fc_polkit_complete)(void *userdata, int result);

/* The peer/consent stay live until the request is freed. Completion is called by the service's bus loop. */
int fc_polkit_start(const struct fc_bus_peer *peer, struct fc_consent *consent,
    fc_polkit_complete complete, void *userdata, struct fc_polkit_request **pending);
/* One approval permits exactly its typed operation. Apply reports an outcome once admitted. */
int fc_polkit_inspect(struct fc_polkit_request *pending, const struct fc_subject *subject,
    struct fc_security_snapshot *observed);
int fc_polkit_apply(struct fc_polkit_request *pending, const struct fc_subject *subject,
    struct fc_write_result *result);
/* Freeing an interactive request cancels its outstanding graphical prompt. */
void fc_polkit_free(struct fc_polkit_request *pending);

#endif
