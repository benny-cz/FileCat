#define _GNU_SOURCE
#include "broker.h"
#include "broker-wire.h"
#include "polkit-request.h"

#include <errno.h>
#include <stdlib.h>
#include <string.h>
#include <sys/epoll.h>
#include <unistd.h>

enum { active_limit = 8 };
static const uint64_t idle_usec = UINT64_C(30000000);
static const uint64_t timer_accuracy_usec = UINT64_C(20000);

struct request {
    struct fc_broker *broker;
    size_t index;
    unsigned char token[FC_BROKER_TOKEN_BYTES];
    struct fc_bus_peer peer;
    struct fc_subject subject;
    sd_bus_message *operation;
    struct fc_consent *consent;
    struct fc_polkit_request *authorization;
    sd_event_source *deadline, *process, *channel;
};

struct fc_broker {
    struct request *active[active_limit];
    sd_event *event;
    sd_event_source *idle;
    sd_bus_slot *methods, *departures;
    fc_broker_verify_peer verify;
    void *verify_argument;
    unsigned count;
};

static const char *sender_of(const struct request *request)
{
    return sd_bus_message_get_sender(request->peer.request);
}

static struct request *find_request(struct fc_broker *service, const char *sender)
{
    if (sender && sender[0] == ':') {
        for (size_t i = 0; i < active_limit; ++i) {
            struct request *request = service->active[i];
            if (request && strcmp(sender_of(request), sender) == 0)
                return request;
        }
    }
    return NULL;
}

static void free_request(struct request *request)
{
    sd_event_source_unref(request->deadline);
    sd_event_source_unref(request->process);
    sd_event_source_unref(request->channel);
    fc_polkit_free(request->authorization);
    fc_consent_free(request->consent);
    fc_subject_close(&request->subject);
    sd_bus_message_unref(request->operation);
    fc_bus_peer_close(&request->peer);
    free(request);
}

static void finish_request(struct request *request)
{
    struct fc_broker *service = request->broker;
    service->active[request->index] = NULL;
    --service->count;
    free_request(request);
    if (service->count == 0 && service->idle &&
        (sd_event_source_set_time_relative(service->idle, idle_usec) < 0 ||
         sd_event_source_set_enabled(service->idle, SD_EVENT_ONESHOT) < 0))
        sd_event_exit(service->event, 1);
}

static void abort_request(struct request *request, int error)
{
    if (request->operation && error)
        sd_bus_reply_method_errno(request->operation, -error, NULL);
    finish_request(request);
}

static int expired(sd_event_source *source, uint64_t usec, void *userdata)
{
    (void)source;
    (void)usec;
    abort_request(userdata, -ETIMEDOUT);
    return 0;
}

static int requester_exited(sd_event_source *source, int descriptor, uint32_t events, void *userdata)
{
    (void)source;
    (void)descriptor;
    (void)events;
    abort_request(userdata, -ESRCH);
    return 0;
}

static int sender_departed(sd_bus_message *message, void *userdata, sd_bus_error *error)
{
    (void)error;
    const char *name, *old_owner, *new_owner;
    if (sd_bus_message_read(message, "sss", &name, &old_owner, &new_owner) == 1 &&
        name[0] == ':' && strcmp(name, old_owner) == 0 && !new_owner[0]) {
        struct request *request = find_request(userdata, name);
        if (request)
            abort_request(request, 0);
    }
    return 0;
}

static int begin(sd_bus_message *message, void *userdata, sd_bus_error *error)
{
    (void)error;
    struct fc_broker *service = userdata;
    int result = fc_wire_read_version(message);
    if (result < 0)
        return result;
    if (sd_bus_message_get_expect_reply(message) != 1)
        return -EINVAL;
    if (find_request(service, sd_bus_message_get_sender(message)))
        return -EALREADY;
    if (service->count >= active_limit)
        return -EBUSY;
    size_t index = 0;
    while (service->active[index])
        ++index;

    struct request *request = calloc(1, sizeof(*request));
    if (!request)
        return -ENOMEM;
    *request = (struct request) {
        .broker = service, .index = index, .peer = FC_BUS_PEER_INIT, .subject = FC_SUBJECT_INIT
    };
    int endpoint;
    result = fc_bus_peer_begin(&request->peer, message, &endpoint);
    if (result < 0) {
        free_request(request);
        return result;
    }
    service->active[index] = request;
    ++service->count;
    result = sd_event_source_set_enabled(service->idle, SD_EVENT_OFF);
    if (result >= 0) {
        const struct timespec *deadline = &request->peer.process.expires;
        uint64_t usec = (uint64_t)deadline->tv_sec * UINT64_C(1000000) + (uint64_t)deadline->tv_nsec / 1000;
        result = sd_event_add_time(service->event, &request->deadline, CLOCK_MONOTONIC, usec, timer_accuracy_usec,
            expired, request);
    }
    if (result >= 0) {
        _Static_assert(FC_BROKER_TOKEN_BYTES == sizeof(request->peer.process.challenge), "Challenge token size");
        memcpy(request->token, request->peer.process.challenge, sizeof(request->token));
        result = fc_wire_reply_begin(message, request->token, endpoint);
    }
    close(endpoint);
    if (result < 0)
        finish_request(request);
    return result;
}

static int read_request(sd_bus_message *message, struct fc_broker *service, struct request **matched)
{
    unsigned char token[FC_BROKER_TOKEN_BYTES];
    int result = fc_wire_read_version(message);
    if (result == 0)
        result = fc_wire_read_token(message, token);
    if (result < 0)
        return result;
    const char *sender = sd_bus_message_get_sender(message);
    struct request *request = find_request(service, sender);
    if (!request || memcmp(request->token, token, sizeof(token)) != 0)
        return -EACCES;
    *matched = request;
    return 0;
}

static int cancel(sd_bus_message *message, void *userdata, sd_bus_error *error)
{
    (void)error;
    struct request *request;
    int result = read_request(message, userdata, &request);
    if (result < 0)
        return result;
    abort_request(request, -ECANCELED);
    return sd_bus_reply_method_return(message, "b", 1);
}

static void authorized(void *userdata, int decision)
{
    struct request *request = userdata;
    if (decision == FC_POLKIT_NO_AUTHENTICATION_AGENT) {
        sd_bus_reply_method_errorf(request->operation, FC_BROKER_NO_AUTHENTICATION_AGENT,
            "No system authentication agent is available. Open FileCat in a desktop session with an authentication agent and try again.");
        finish_request(request);
        return;
    }
    if (decision == 0)
        decision = request->broker->verify(&request->peer, request->broker->verify_argument);
    if (decision < 0) {
        abort_request(request, decision);
        return;
    }

    int result;
    if (fc_consent_inspect_sections(request->consent)) {
        struct fc_security_snapshot observed;
        result = fc_polkit_inspect(request->authorization, &request->subject, &observed);
        if (result == 0)
            result = fc_wire_reply_inspect(request->operation, &observed);
    } else {
        struct fc_write_result outcome;
        result = fc_polkit_apply(request->authorization, &request->subject, &outcome);
        if (result == 0) {
            /* Publish only to the originating connection, even when native dispatch completed. */
            if (fc_bus_peer_live(&request->peer) == 0 && fc_wire_reply_apply(request->operation, &outcome) < 0)
                sd_bus_reply_method_errorf(request->operation, FC_BROKER_OUTCOME_UNAVAILABLE,
                    "The permissions broker could not return a verified outcome.");
            finish_request(request);
            return;
        }
    }
    abort_request(request, result < 0 ? result : 0);
}

static int start_authorization(struct request *request, sd_bus_message *message, int apply)
{
    struct fc_wire_operation operation = { 0 };
    int result = fc_wire_read_operation(message, apply, &operation);
    if (result < 0)
        return result;
    result = fc_bus_peer_finish(&request->peer);
    if (result == -EAGAIN)
        return -EACCES;
    if (result < 0)
        return result;
    struct fc_broker *service = request->broker;
    result = service->verify(&request->peer, service->verify_argument);
    if (result < 0)
        return result;

    struct fc_subject_state bound;
    result = fc_subject_bind(&request->subject, operation.descriptor, operation.path, &operation.identity, &bound);
    if (result < 0)
        return result;
    struct fc_security_snapshot reviewed;
    result = fc_security_capture(&request->subject, apply ? operation.sections : 0, &reviewed);
    if (result < 0)
        return result;
    if (!fc_subject_state_matches(&bound, &reviewed.subject) ||
        (apply && memcmp(reviewed.revision, operation.revision, sizeof(operation.revision)) != 0))
        return -ESTALE;
    const struct fc_consent_requester requester = {
        sender_of(request), request->peer.process.uid, request->peer.process.pid
    };
    result = fc_consent_prepare(&requester, &request->subject, &reviewed, apply ? 0 : operation.sections,
        apply ? &operation.change : NULL, &request->consent);
    if (result < 0)
        return result;

    request->deadline = sd_event_source_unref(request->deadline);
    result = sd_event_add_time_relative(service->event, &request->deadline, CLOCK_BOOTTIME,
        FC_CONSENT_SECONDS * UINT64_C(1000000), timer_accuracy_usec, expired, request);
    if (result >= 0)
        result = sd_event_add_io(service->event, &request->process, request->peer.process.process, EPOLLIN,
            requester_exited, request);
    if (result >= 0)
        result = sd_event_add_io(service->event, &request->channel, request->peer.process.socket,
            EPOLLRDHUP | EPOLLHUP | EPOLLERR, requester_exited, request);
    if (result < 0)
        return result;
    request->operation = sd_bus_message_ref(message);
    return fc_polkit_start(&request->peer, request->consent, authorized, request, &request->authorization);
}

static int inspect_or_apply(sd_bus_message *message, void *userdata, sd_bus_error *error)
{
    (void)error;
    if (sd_bus_message_get_expect_reply(message) != 1)
        return -EINVAL;
    struct request *request;
    int result = read_request(message, userdata, &request);
    if (result < 0)
        return result;
    if (request->operation)
        return -EALREADY;
    int apply = sd_bus_message_is_method_call(message, FC_BROKER_INTERFACE, "Apply") > 0;
    result = start_authorization(request, message, apply);
    if (result < 0) {
        finish_request(request);
        return result;
    }
    return 1;
}

static const sd_bus_vtable broker_methods[] = {
    SD_BUS_VTABLE_START(0),
    SD_BUS_METHOD("Begin", FC_WIRE_BEGIN, FC_WIRE_BEGIN_REPLY, begin, SD_BUS_VTABLE_UNPRIVILEGED),
    SD_BUS_METHOD("Inspect", FC_WIRE_INSPECT, FC_WIRE_SNAPSHOT, inspect_or_apply, SD_BUS_VTABLE_UNPRIVILEGED),
    SD_BUS_METHOD("Apply", FC_WIRE_APPLY, FC_WIRE_APPLY_REPLY, inspect_or_apply, SD_BUS_VTABLE_UNPRIVILEGED),
    SD_BUS_METHOD("Cancel", FC_WIRE_CANCEL, "b", cancel, SD_BUS_VTABLE_UNPRIVILEGED),
    SD_BUS_VTABLE_END
};

int fc_broker_start(sd_bus *bus, sd_event *event, fc_broker_verify_peer verify, void *argument,
    struct fc_broker **service)
{
    *service = NULL;
    if (!verify)
        return -EINVAL;
    struct fc_broker *broker = calloc(1, sizeof(*broker));
    if (!broker)
        return -ENOMEM;
    broker->event = event;
    broker->verify = verify;
    broker->verify_argument = argument;
    int result = sd_event_add_time_relative(event, &broker->idle, CLOCK_MONOTONIC, idle_usec, timer_accuracy_usec,
        NULL, NULL);
    if (result >= 0)
        result = sd_bus_add_object_vtable(bus, &broker->methods, FC_BROKER_PATH, FC_BROKER_INTERFACE,
            broker_methods, broker);
    if (result >= 0)
        result = sd_bus_match_signal(bus, &broker->departures, "org.freedesktop.DBus", "/org/freedesktop/DBus",
            "org.freedesktop.DBus", "NameOwnerChanged", sender_departed, broker);
    if (result >= 0)
        result = sd_bus_request_name(bus, FC_BROKER_NAME, 0);
    if (result < 0) {
        fc_broker_free(broker);
        return result;
    }
    *service = broker;
    return 0;
}

void fc_broker_free(struct fc_broker *service)
{
    if (!service)
        return;
    sd_bus_slot_unref(service->methods);
    sd_bus_slot_unref(service->departures);
    sd_event_source_unref(service->idle);
    for (size_t i = 0; i < active_limit; ++i) {
        struct request *request = service->active[i];
        if (request)
            free_request(request);
    }
    free(service);
}
