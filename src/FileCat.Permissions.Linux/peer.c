#define _GNU_SOURCE
#include "peer.h"

#include <errno.h>
#include <limits.h>
#include <poll.h>
#include <string.h>
#include <sys/random.h>
#include <sys/socket.h>
#include <unistd.h>

/* Linux x64 UAPI on the 6.8 floor. The Ubuntu userspace headers can predate the socket option. */
#ifndef SO_PASSPIDFD
#define SO_PASSPIDFD 76
#endif
#ifndef SCM_PIDFD
#define SCM_PIDFD 0x04
#endif

enum { challenge_seconds = 5 };

static int still_connected(const struct fc_peer *peer)
{
    struct pollfd descriptors[] = {
        { .fd = peer->process, .events = POLLIN },
        { .fd = peer->socket, .events = 0 }
    };
    int r = poll(descriptors, 2, 0);
    if (r < 0)
        return -errno;
    return r == 0 ? 0 : -ESRCH;
}

int fc_peer_begin(struct fc_peer *peer, uint32_t bus_pid, uint32_t bus_uid, int *endpoint)
{
    *endpoint = -1;
    if (peer->socket >= 0 || bus_pid == 0 || bus_pid > INT_MAX || bus_uid == 0 || (uid_t)bus_uid == (uid_t)-1)
        return -EINVAL;

    int pair[2];
    if (socketpair(AF_UNIX, SOCK_SEQPACKET | SOCK_CLOEXEC, 0, pair) < 0)
        return -errno;

    struct fc_peer pending = FC_PEER_INIT;
    pending.pid = (pid_t)bus_pid;
    pending.uid = (uid_t)bus_uid;
    pending.socket = pair[0];
    const int enable = 1;
    int r;
    if (setsockopt(pending.socket, SOL_SOCKET, SO_PASSCRED, &enable, sizeof(enable)) < 0 ||
        setsockopt(pending.socket, SOL_SOCKET, SO_PASSPIDFD, &enable, sizeof(enable)) < 0 ||
        clock_gettime(CLOCK_MONOTONIC, &pending.expires) < 0) {
        r = -errno;
        goto failed;
    }
    pending.expires.tv_sec += challenge_seconds;
    ssize_t bytes = getrandom(pending.challenge, sizeof(pending.challenge), GRND_NONBLOCK);
    if (bytes != sizeof(pending.challenge)) {
        r = bytes < 0 ? -errno : -EIO;
        goto failed;
    }
    bytes = send(pending.socket, pending.challenge, sizeof(pending.challenge), MSG_DONTWAIT | MSG_NOSIGNAL);
    if (bytes != sizeof(pending.challenge)) {
        r = bytes < 0 ? -errno : -EPROTO;
        goto failed;
    }
    *peer = pending;
    *endpoint = pair[1];
    return 0;

failed:
    close(pair[1]);
    fc_peer_close(&pending);
    return r;
}

static int read_response(struct fc_peer *peer)
{
    unsigned char response[sizeof(peer->challenge)];
    struct iovec body = { .iov_base = response, .iov_len = sizeof(response) };
    union {
        struct cmsghdr align;
        unsigned char bytes[CMSG_SPACE(sizeof(struct ucred)) + CMSG_SPACE(sizeof(int)) + CMSG_SPACE(sizeof(int) * 4)];
    } control;
    struct msghdr message = {
        .msg_iov = &body, .msg_iovlen = 1,
        .msg_control = control.bytes, .msg_controllen = sizeof(control.bytes)
    };
    ssize_t bytes = recvmsg(peer->socket, &message, MSG_DONTWAIT | MSG_CMSG_CLOEXEC);
    if (bytes < 0)
        return -errno;

    int present = 0, unexpected = 0, process = -1;
    struct ucred credential;
    for (struct cmsghdr *field = CMSG_FIRSTHDR(&message); field; field = CMSG_NXTHDR(&message, field)) {
        if (field->cmsg_level == SOL_SOCKET && field->cmsg_type == SCM_CREDENTIALS &&
            field->cmsg_len == CMSG_LEN(sizeof(credential)) && !present) {
            memcpy(&credential, CMSG_DATA(field), sizeof(credential));
            present = 1;
        } else if (field->cmsg_level == SOL_SOCKET && field->cmsg_type == SCM_PIDFD &&
                   field->cmsg_len == CMSG_LEN(sizeof(process)) && process < 0) {
            memcpy(&process, CMSG_DATA(field), sizeof(process));
        } else {
            if (field->cmsg_level == SOL_SOCKET && (field->cmsg_type == SCM_RIGHTS || field->cmsg_type == SCM_PIDFD) &&
                field->cmsg_len >= CMSG_LEN(0)) {
                size_t count = (field->cmsg_len - CMSG_LEN(0)) / sizeof(int);
                for (size_t i = 0; i < count; ++i) {
                    int descriptor;
                    memcpy(&descriptor, CMSG_DATA(field) + i * sizeof(descriptor), sizeof(descriptor));
                    close(descriptor);
                }
            }
            unexpected = 1;
        }
    }
    peer->process = process;
    if (bytes != sizeof(response) || (message.msg_flags & (MSG_TRUNC | MSG_CTRUNC)) || !present || process < 0 || unexpected)
        return -EPROTO;
    if (credential.pid != peer->pid || credential.uid != peer->uid || credential.gid == (gid_t)-1 ||
        memcmp(peer->challenge, response, sizeof(response)) != 0)
        return -EACCES;
    peer->gid = credential.gid;
    return still_connected(peer);
}

int fc_peer_finish(struct fc_peer *peer)
{
    if (peer->socket < 0 || peer->verified)
        return -EINVAL;

    struct timespec now;
    int r;
    if (clock_gettime(CLOCK_MONOTONIC, &now) < 0)
        r = -errno;
    else if (now.tv_sec > peer->expires.tv_sec ||
             (now.tv_sec == peer->expires.tv_sec && now.tv_nsec >= peer->expires.tv_nsec))
        r = -ETIMEDOUT;
    else
        r = read_response(peer);

    if (r == 0) {
        explicit_bzero(peer->challenge, sizeof(peer->challenge));
        peer->verified = 1;
    } else if (r != -EAGAIN) {
        fc_peer_close(peer);
    }
    return r;
}

int fc_peer_live(const struct fc_peer *peer)
{
    return peer->verified ? still_connected(peer) : -EACCES;
}

void fc_peer_close(struct fc_peer *peer)
{
    if (peer->process >= 0)
        close(peer->process);
    if (peer->socket >= 0)
        close(peer->socket);
    explicit_bzero(peer->challenge, sizeof(peer->challenge));
    *peer = (struct fc_peer) FC_PEER_INIT;
}
