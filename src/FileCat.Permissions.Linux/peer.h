#ifndef FILECAT_PERMISSIONS_PEER_H
#define FILECAT_PERMISSIONS_PEER_H

#include <stdint.h>
#include <sys/types.h>
#include <time.h>

/* One D-Bus request owns this proof and its socket pair. */
struct fc_peer {
    int socket;
    int process;
    pid_t pid;
    uid_t uid;
    gid_t gid;
    struct timespec expires;
    unsigned char challenge[32];
    int verified;
};

#define FC_PEER_INIT { .socket = -1, .process = -1 }

/* Return an owned endpoint for delivery to the unique D-Bus sender. PID and UID come from the bus. */
int fc_peer_begin(struct fc_peer *peer, uint32_t bus_pid, uint32_t bus_uid, int *endpoint);
/* Returns -EAGAIN until a reply arrives. Other errors terminate the proof. */
int fc_peer_finish(struct fc_peer *peer);
/* Call again before each authorized operation. The caller must also verify the installed image and namespaces. */
int fc_peer_live(const struct fc_peer *peer);
void fc_peer_close(struct fc_peer *peer);

#endif
