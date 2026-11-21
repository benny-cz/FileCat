#define _GNU_SOURCE
#include "broker.h"

#include <errno.h>
#include <signal.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

static int verify_installed_peer(const struct fc_bus_peer *peer, void *argument)
{
    int result = fc_installation_configured();
    if (result == 0)
        result = fc_bus_peer_check_installation(peer, argument);
    return result == 0 ? fc_installation_configured() : result;
}

static int open_system_bus(sd_bus **bus)
{
    int result = sd_bus_new(bus);
    if (result >= 0)
        result = sd_bus_set_address(*bus, "unix:path=/run/dbus/system_bus_socket");
    if (result >= 0)
        result = sd_bus_set_bus_client(*bus, 1);
    if (result >= 0)
        result = sd_bus_negotiate_fds(*bus, 1);
    if (result >= 0)
        result = sd_bus_start(*bus);
    return result;
}

static int stop(sd_event_source *source, const struct signalfd_siginfo *info, void *userdata)
{
    (void)source;
    (void)info;
    return sd_event_exit(userdata, 0);
}

static int run(const struct fc_installation *installation)
{
    sigset_t signals;
    if (sigemptyset(&signals) < 0 || sigaddset(&signals, SIGTERM) < 0 || sigaddset(&signals, SIGINT) < 0 ||
        sigprocmask(SIG_BLOCK, &signals, NULL) < 0)
        return -errno;

    sd_bus *bus = NULL;
    sd_event *event = NULL;
    sd_event_source *term = NULL, *interrupt = NULL;
    struct fc_broker *broker = NULL;
    int result = open_system_bus(&bus);
    if (result >= 0)
        result = sd_event_new(&event);
    if (result >= 0)
        result = sd_event_add_signal(event, &term, SIGTERM, stop, event);
    if (result >= 0)
        result = sd_event_add_signal(event, &interrupt, SIGINT, stop, event);
    if (result >= 0)
        result = sd_bus_set_exit_on_disconnect(bus, 1);
    if (result >= 0)
        result = sd_bus_attach_event(bus, event, 0);
    if (result >= 0)
        result = fc_broker_start(bus, event, verify_installed_peer, (void *)installation, &broker);
    if (result >= 0)
        result = sd_event_loop(event);
    fc_broker_free(broker);
    sd_event_source_unref(term);
    sd_event_source_unref(interrupt);
    sd_bus_flush_close_unref(bus);
    sd_event_unref(event);
    return result;
}

int main(int argc, char **argv)
{
    (void)argv;
    if (argc != 1 || geteuid() != 0 || getuid() != 0)
        return 2;
    umask(0077);
    int result = fc_installation_configured();
    if (result < 0) {
        fprintf(stderr, "FileCat permissions broker is not configured (%s)\n", strerror(-result));
        return 1;
    }
    struct fc_installation installation;
    result = fc_installation_open(&installation);
    if (result < 0) {
        fprintf(stderr, "FileCat permissions broker package check failed (%s)\n", strerror(-result));
        return 1;
    }
    result = run(&installation);
    fc_installation_close(&installation);
    if (result < 0) {
        fprintf(stderr, "FileCat permissions broker bus or event setup failed (%s)\n", strerror(-result));
        return 1;
    }
    return result;
}
