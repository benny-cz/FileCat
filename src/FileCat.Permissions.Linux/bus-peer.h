#ifndef FILECAT_PERMISSIONS_BUS_PEER_H
#define FILECAT_PERMISSIONS_BUS_PEER_H

#include "peer.h"
#include "installation.h"
#include <systemd/sd-bus.h>

struct fc_bus_peer {
    sd_bus_message *request;
    struct fc_peer process;
};

#define FC_BUS_PEER_INIT { .process = FC_PEER_INIT }

/* The request arrived on the mechanism's authenticated bus. Return the challenge endpoint to that sender. */
int fc_bus_peer_begin(struct fc_bus_peer *peer, sd_bus_message *request, int *endpoint);
int fc_bus_peer_finish(struct fc_bus_peer *peer);
int fc_bus_peer_live(const struct fc_bus_peer *peer);
int fc_bus_peer_check_client(const struct fc_bus_peer *peer, int executable, const char *path);
int fc_bus_peer_check_installation(const struct fc_bus_peer *peer, const struct fc_installation *installation);
void fc_bus_peer_close(struct fc_bus_peer *peer);

#endif
