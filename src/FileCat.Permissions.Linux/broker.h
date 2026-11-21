#ifndef FILECAT_PERMISSIONS_BROKER_H
#define FILECAT_PERMISSIONS_BROKER_H

#include "bus-peer.h"
#include <systemd/sd-event.h>

struct fc_broker;
typedef int (*fc_broker_verify_peer)(const struct fc_bus_peer *peer, void *argument);

/* Run on the single event loop to which the broker's bus connection is attached. */
int fc_broker_start(sd_bus *bus, sd_event *event, fc_broker_verify_peer verify, void *argument,
    struct fc_broker **service);
void fc_broker_free(struct fc_broker *service);

#endif
