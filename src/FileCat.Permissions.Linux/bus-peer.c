#include "bus-peer.h"
#include "client-identity.h"

#include <errno.h>

static int sender_credentials(sd_bus_message *request, pid_t *pid, uid_t *uid)
{
    const char *sender = sd_bus_message_get_sender(request);
    sd_bus *bus = sd_bus_message_get_bus(request);
    if (!bus || !sender || sender[0] != ':' || sd_bus_service_name_is_valid(sender) <= 0)
        return -EPROTO;

    sd_bus_creds *credentials = NULL;
    int r = sd_bus_get_name_creds(bus, sender, SD_BUS_CREDS_PID | SD_BUS_CREDS_EUID, &credentials);
    if (r >= 0)
        r = sd_bus_creds_get_pid(credentials, pid);
    if (r >= 0)
        r = sd_bus_creds_get_euid(credentials, uid);
    sd_bus_creds_unref(credentials);
    return r;
}

int fc_bus_peer_begin(struct fc_bus_peer *peer, sd_bus_message *request, int *endpoint)
{
    *endpoint = -1;
    if (peer->request)
        return -EALREADY;

    pid_t pid;
    uid_t uid;
    int r = sender_credentials(request, &pid, &uid);
    if (r < 0)
        return r;
    r = fc_peer_begin(&peer->process, (uint32_t)pid, (uint32_t)uid, endpoint);
    if (r < 0)
        return r;
    peer->request = sd_bus_message_ref(request);
    return 0;
}

int fc_bus_peer_finish(struct fc_bus_peer *peer)
{
    if (!peer->request)
        return -EINVAL;
    int r = fc_peer_finish(&peer->process);
    if (r == -EAGAIN)
        return r;
    if (r == 0)
        r = fc_bus_peer_live(peer);
    if (r < 0)
        fc_bus_peer_close(peer);
    return r;
}

int fc_bus_peer_live(const struct fc_bus_peer *peer)
{
    if (!peer->request)
        return -EACCES;
    int r = fc_peer_live(&peer->process);
    if (r < 0)
        return r;

    pid_t pid;
    uid_t uid;
    r = sender_credentials(peer->request, &pid, &uid);
    if (r < 0)
        return r;
    if (pid != peer->process.pid || uid != peer->process.uid)
        return -EACCES;
    return fc_peer_live(&peer->process);
}

int fc_bus_peer_check_client(const struct fc_bus_peer *peer, int executable, const char *path)
{
    int r = fc_bus_peer_live(peer);
    if (r == 0)
        r = fc_client_identity(&peer->process, executable, path);
    return r == 0 ? fc_bus_peer_live(peer) : r;
}

int fc_bus_peer_check_installation(const struct fc_bus_peer *peer, const struct fc_installation *installation)
{
    int r = fc_bus_peer_live(peer);
    if (r == 0)
        r = fc_client_installed_identity(&peer->process, installation);
    return r == 0 ? fc_bus_peer_live(peer) : r;
}

void fc_bus_peer_close(struct fc_bus_peer *peer)
{
    fc_peer_close(&peer->process);
    peer->request = sd_bus_message_unref(peer->request);
}
