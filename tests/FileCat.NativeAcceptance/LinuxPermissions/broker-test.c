#define _GNU_SOURCE
#include "broker.h"
#include "broker-wire.h"
#include "consent.h"

#include <assert.h>
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <stdio.h>
#include <string.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/wait.h>
#include <unistd.h>

#ifndef STATX_MNT_ID_UNIQUE
#define STATX_MNT_ID_UNIQUE 0x4000U
#endif

#define AUTHORITY_NAME "org.freedesktop.PolicyKit1"
#define AUTHORITY_PATH "/org/freedesktop/PolicyKit1/Authority"
#define AUTHORITY_INTERFACE "org.freedesktop.PolicyKit1.Authority"

struct approver {
    unsigned checks, canceled;
    int notify;
    char held[FC_CONSENT_NONCE_LENGTH + 1];
};

static int check_authorization(sd_bus_message *request, void *userdata, sd_bus_error *error)
{
    (void)error;
    struct approver *approver = userdata;
    assert(sd_bus_message_skip(request, "(sa{sv})") > 0);
    const char *action;
    assert(sd_bus_message_read(request, "s", &action) == 1);
    assert(sd_bus_message_enter_container(request, 'a', "{ss}") == 1);
    int hold = 0, dismiss = 0;
    int result;
    const char *key, *value, *digest = NULL;
    while ((result = sd_bus_message_read(request, "{ss}", &key, &value)) > 0) {
        if (!strcmp(key, "filecat.digest"))
            digest = value;
        if (!strcmp(key, "filecat.target")) {
            hold = strstr(value, "/hold\"") != NULL ||
                (strstr(value, "/cancel-apply\"") != NULL && strcmp(action, "org.filecat.permissions.apply") == 0);
            dismiss = strstr(value, "/dismiss-apply\"") != NULL && strcmp(action, "org.filecat.permissions.apply") == 0;
        }
    }
    assert(result == 0 && digest && strlen(digest) == FC_CONSENT_DIGEST_LENGTH);
    assert(sd_bus_message_exit_container(request) == 1);
    uint32_t flags;
    const char *nonce;
    assert(sd_bus_message_read(request, "us", &flags, &nonce) == 1 && flags <= 1);
    ++approver->checks;
    if (hold && flags) {
        assert(strlen(nonce) == FC_CONSENT_NONCE_LENGTH && !approver->held[0]);
        memcpy(approver->held, nonce, sizeof(approver->held));
        if (approver->notify >= 0)
            assert(write(approver->notify, "a", 1) == 1);
        else
            puts("PROMPT");
        return 1;
    }
    if (dismiss && flags)
        return sd_bus_reply_method_return(request, "(bba{ss})", 0, 0, 2, "filecat.digest", digest, "polkit.dismissed", "true");
    return sd_bus_reply_method_return(request, "(bba{ss})", flags != 0, flags == 0, 1, "filecat.digest", digest);
}

static int cancel_authorization(sd_bus_message *request, void *userdata, sd_bus_error *error)
{
    (void)error;
    struct approver *approver = userdata;
    const char *nonce;
    assert(sd_bus_message_read(request, "s", &nonce) == 1 && strcmp(nonce, approver->held) == 0);
    approver->held[0] = 0;
    ++approver->canceled;
    if (approver->notify >= 0)
        assert(write(approver->notify, "c", 1) == 1);
    else
        puts("CANCELED");
    return 1;
}

static const sd_bus_vtable authority_methods[] = {
    SD_BUS_VTABLE_START(0),
    SD_BUS_METHOD("CheckAuthorization", "(sa{sv})sa{ss}us", "(bba{ss})", check_authorization, SD_BUS_VTABLE_UNPRIVILEGED),
    SD_BUS_METHOD("CancelCheckAuthorization", "s", "", cancel_authorization, SD_BUS_VTABLE_UNPRIVILEGED),
    SD_BUS_VTABLE_END
};

struct executable {
    int pin;
    char path[PATH_MAX];
};

static int verify_peer(const struct fc_bus_peer *peer, void *userdata)
{
    struct executable *executable = userdata;
    return fc_bus_peer_check_client(peer, executable->pin, executable->path);
}

static int begin(sd_bus *bus, unsigned char token[FC_BROKER_TOKEN_BYTES])
{
    sd_bus_message *reply = NULL;
    assert(sd_bus_call_method(bus, FC_BROKER_NAME, FC_BROKER_PATH, FC_BROKER_INTERFACE, "Begin", NULL, &reply,
        "u", (uint32_t)FC_BROKER_VERSION) > 0);
    const void *bytes;
    size_t size;
    int borrowed;
    assert(sd_bus_message_read_array(reply, 'y', &bytes, &size) == 1 && size == FC_BROKER_TOKEN_BYTES);
    memcpy(token, bytes, size);
    assert(sd_bus_message_read(reply, "h", &borrowed) == 1);
    int endpoint = fcntl(borrowed, F_DUPFD_CLOEXEC, 3);
    assert(endpoint >= 0);
    sd_bus_message_unref(reply);
    unsigned char response[FC_BROKER_TOKEN_BYTES];
    assert(recv(endpoint, response, sizeof(response), 0) == sizeof(response) && !memcmp(response, token, size));
    assert(send(endpoint, response, size, MSG_NOSIGNAL) == (ssize_t)size);
    return endpoint;
}

static sd_bus_message *operation(sd_bus *bus, const char *method, const unsigned char token[FC_BROKER_TOKEN_BYTES],
    const char *path, int wrong_identity)
{
    int descriptor = open(path, O_PATH | O_NOFOLLOW | O_CLOEXEC);
    assert(descriptor >= 0);
    struct statx facts;
    unsigned fields = STATX_TYPE | STATX_INO | STATX_MNT_ID_UNIQUE;
    assert(statx(descriptor, "", AT_EMPTY_PATH, fields, &facts) == 0 && (facts.stx_mask & fields) == fields);
    sd_bus_message *message = NULL;
    assert(sd_bus_message_new_method_call(bus, &message, FC_BROKER_NAME, FC_BROKER_PATH, FC_BROKER_INTERFACE, method) == 0);
    assert(sd_bus_message_append(message, "u", (uint32_t)FC_BROKER_VERSION) > 0);
    assert(sd_bus_message_append_array(message, 'y', token, FC_BROKER_TOKEN_BYTES) >= 0);
    assert(sd_bus_message_append_array(message, 'y', path, strlen(path)) >= 0);
    assert(sd_bus_message_append(message, "h(ttuuq)u", descriptor, (uint64_t)facts.stx_mnt_id,
        (uint64_t)facts.stx_ino + (uint64_t)wrong_identity, facts.stx_dev_major, facts.stx_dev_minor,
        facts.stx_mode & S_IFMT, (uint32_t)FC_SECURITY_ALL) > 0);
    assert(close(descriptor) == 0);
    return message;
}

static sd_bus_message *apply_operation(sd_bus *bus, const unsigned char token[FC_BROKER_TOKEN_BYTES], const char *path,
    const unsigned char revision[FC_SECURITY_REVISION_BYTES], int wrong_variant)
{
    sd_bus_message *message = operation(bus, "Apply", token, path, 0);
    assert(sd_bus_message_append_array(message, 'y', revision, FC_SECURITY_REVISION_BYTES) >= 0);
    assert(sd_bus_message_append(message, "u", (uint32_t)1) > 0);
    if (wrong_variant)
        assert(sd_bus_message_append(message, "v", "s", "bad mode type") > 0);
    else
        assert(sd_bus_message_append(message, "v", "u", (uint32_t)0400) > 0);
    return message;
}

static int cancel(sd_bus *bus, const unsigned char token[FC_BROKER_TOKEN_BYTES])
{
    sd_bus_message *message = NULL;
    assert(sd_bus_message_new_method_call(bus, &message, FC_BROKER_NAME, FC_BROKER_PATH, FC_BROKER_INTERFACE,
        "Cancel") == 0);
    assert(sd_bus_message_append(message, "u", (uint32_t)FC_BROKER_VERSION) > 0);
    assert(sd_bus_message_append_array(message, 'y', token, FC_BROKER_TOKEN_BYTES) >= 0);
    sd_bus_message *reply = NULL;
    int result = sd_bus_call(bus, message, UINT64_C(3000000), NULL, &reply);
    if (result >= 0) {
        int canceled;
        assert(sd_bus_message_read(reply, "b", &canceled) == 1 && canceled);
    }
    sd_bus_message_unref(reply);
    sd_bus_message_unref(message);
    return result;
}

static void read_snapshot(sd_bus_message *message, uint32_t mode, unsigned char revision[FC_SECURITY_REVISION_BYTES])
{
    assert(sd_bus_message_enter_container(message, 'r', FC_WIRE_SNAPSHOT_FIELDS) == 1);
    uint32_t sections, measured;
    assert(sd_bus_message_read(message, "u", &sections) == 1 && sections == FC_SECURITY_ALL);
    assert(sd_bus_message_skip(message, "(ttuuq)") > 0);
    assert(sd_bus_message_enter_container(message, 'r', "uuuuttttxuxu") == 1);
    assert(sd_bus_message_read(message, "u", &measured) == 1 && (measured & 07777) == mode);
    assert(sd_bus_message_skip(message, "uuuttttxuxu") > 0 && sd_bus_message_exit_container(message) == 1);
    const void *bytes;
    size_t size;
    assert(sd_bus_message_read_array(message, 'y', &bytes, &size) == 1 && size == FC_SECURITY_REVISION_BYTES);
    memcpy(revision, bytes, size);
    assert(sd_bus_message_skip(message, "(uiay)(uiay)(uiay)(uiuu)") > 0);
    assert(sd_bus_message_exit_container(message) == 1);
}

static void expect_refused(sd_bus *bus, sd_bus_message *message)
{
    sd_bus_error error = SD_BUS_ERROR_NULL;
    assert(sd_bus_call(bus, message, UINT64_C(3000000), &error, NULL) < 0 && sd_bus_error_is_set(&error));
    sd_bus_error_free(&error);
    sd_bus_message_unref(message);
}

static int received_error(sd_bus_message *message, void *userdata, sd_bus_error *error)
{
    (void)error;
    int *result = userdata;
    *result = sd_bus_message_get_errno(message);
    assert(*result != 0);
    return 1;
}

static void expect_signal(int descriptor, char expected)
{
    char actual = 0;
    ssize_t size = read(descriptor, &actual, 1);
    if (size != 1 || actual != expected)
        fprintf(stderr, "Expected fixture signal %c, received %d (%zd bytes)\n", expected, actual, size);
    assert(size == 1 && actual == expected);
}

static void interrupt_request(sd_bus *bus, const char *path, int notify, int explicit_cancel)
{
    unsigned char token[FC_BROKER_TOKEN_BYTES];
    int endpoint = begin(bus, token);
    sd_bus_message *message = operation(bus, "Inspect", token, path, 0);
    sd_bus_slot *slot = NULL;
    int error = 0;
    assert(sd_bus_call_async(bus, &slot, message, received_error, &error, UINT64_C(3000000)) > 0);
    assert(sd_bus_flush(bus) >= 0);
    expect_signal(notify, 'a');
    if (explicit_cancel)
        assert(cancel(bus, token) > 0);
    assert(close(endpoint) == 0);
    expect_signal(notify, 'c');
    while (!error) {
        int waiting = sd_bus_process(bus, NULL);
        assert(waiting >= 0);
        if (!waiting)
            assert(sd_bus_wait(bus, 3000000) >= 0);
    }
    sd_bus_slot_unref(slot);
    sd_bus_message_unref(message);
    assert(cancel(bus, token) < 0);
}

static void client(const char *path, const char *hold_path, int control, int notify)
{
    expect_signal(control, 'r');
    sd_bus *bus = NULL, *other = NULL;
    assert(sd_bus_open_user(&bus) >= 0 && sd_bus_open_user(&other) >= 0);
    unsigned char prior[FC_BROKER_TOKEN_BYTES], token[FC_BROKER_TOKEN_BYTES];
    unsigned char revision[FC_SECURITY_REVISION_BYTES], written[FC_SECURITY_REVISION_BYTES];
    int endpoint = begin(bus, token);
    assert(cancel(other, token) < 0);
    assert(sd_bus_call_method(bus, FC_BROKER_NAME, FC_BROKER_PATH, FC_BROKER_INTERFACE, "Begin", NULL, NULL,
        "u", (uint32_t)FC_BROKER_VERSION) < 0);
    sd_bus_message *message = operation(bus, "Inspect", token, path, 0), *reply = NULL;
    assert(sd_bus_call(bus, message, UINT64_C(3000000), NULL, &reply) > 0);
    read_snapshot(reply, 0600, revision);
    sd_bus_message_unref(reply);
    sd_bus_message_unref(message);
    assert(close(endpoint) == 0);
    memcpy(prior, token, sizeof(prior));

    endpoint = begin(bus, token);
    expect_refused(bus, operation(bus, "Inspect", prior, path, 0));
    message = apply_operation(bus, token, path, revision, 0);
    assert(sd_bus_call(bus, message, UINT64_C(3000000), NULL, &reply) > 0);
    uint32_t state;
    int32_t setter_error, verification_error;
    assert(sd_bus_message_read(reply, "uii", &state, &setter_error, &verification_error) == 1 &&
        state == FC_WRITE_VERIFIED && setter_error == 0 && verification_error == 0);
    assert(sd_bus_message_enter_container(reply, 'a', FC_WIRE_SNAPSHOT) == 1);
    read_snapshot(reply, 0400, written);
    assert(sd_bus_message_at_end(reply, 0) > 0 && sd_bus_message_exit_container(reply) == 1);
    assert(memcmp(revision, written, sizeof(revision)) != 0);
    struct stat actual;
    assert(stat(path, &actual) == 0 && (actual.st_mode & 07777) == 0400);
    assert(close(endpoint) == 0);
    sd_bus_message_unref(reply);
    sd_bus_message_unref(message);
    puts("PASS authentic executable sends a descriptor, reads the reviewed revision, applies once and verifies the typed outcome");

    endpoint = begin(bus, token);
    expect_refused(bus, apply_operation(bus, token, path, revision, 0));
    assert(close(endpoint) == 0);
    endpoint = begin(bus, token);
    expect_refused(bus, apply_operation(bus, token, path, written, 1));
    assert(close(endpoint) == 0);
    endpoint = begin(bus, token);
    expect_refused(bus, operation(bus, "Inspect", token, path, 1));
    assert(close(endpoint) == 0);
    puts("PASS stale revision, wrong variant, mismatched identity, reused token and foreign connection are refused");

    interrupt_request(bus, hold_path, notify, 1);
    interrupt_request(bus, hold_path, notify, 0);
    puts("PASS explicit cancellation and challenge-channel closure cancel the outstanding polkit request");

    endpoint = begin(other, token);
    message = operation(other, "Inspect", token, hold_path, 0);
    sd_bus_slot *pending = NULL;
    int no_response = 0;
    assert(sd_bus_call_async(other, &pending, message, received_error, &no_response, UINT64_C(3000000)) > 0);
    assert(sd_bus_flush(other) >= 0);
    expect_signal(notify, 'a');
    sd_bus_slot_unref(pending);
    sd_bus_message_unref(message);
    sd_bus_flush_close_unref(other);
    expect_signal(notify, 'c');
    assert(close(endpoint) == 0);
    puts("PASS D-Bus departure cancels the pending prompt even while the challenge channel is held");

    endpoint = begin(bus, token);
    sleep(6);
    expect_refused(bus, operation(bus, "Inspect", token, path, 0));
    assert(close(endpoint) == 0);
    endpoint = begin(bus, token);
    assert(cancel(bus, token) > 0 && close(endpoint) == 0);
    puts("PASS expired challenge is refused and its request slot is reclaimed");
    sd_bus_flush_close_unref(bus);
}

static void system_smoke(const char *path)
{
    sd_bus *bus = NULL;
    sd_bus_creds *owner = NULL;
    assert(sd_bus_open_system(&bus) >= 0);
    unsigned char token[FC_BROKER_TOKEN_BYTES];
    int endpoint = begin(bus, token);
    assert(sd_bus_get_name_creds(bus, FC_BROKER_NAME, SD_BUS_CREDS_EUID, &owner) >= 0);
    uid_t uid;
    assert(sd_bus_creds_get_euid(owner, &uid) == 0 && uid == 0);
    sd_bus_creds_unref(owner);
    sd_bus_message *message = operation(bus, "Inspect", token, path, 0);
    int refused = sd_bus_call(bus, message, UINT64_C(5000000), NULL, NULL);
    if (refused != -EACCES && refused != -EPERM)
        fprintf(stderr, "Expected uninstalled client to be refused, got %d\n", refused);
    assert(refused == -EACCES || refused == -EPERM);
    sd_bus_message_unref(message);
    assert(close(endpoint) == 0);
    sd_bus_flush_close_unref(bus);
    puts("PASS production broker activates as root over the system bus and refuses an uninstalled requester");
}

static void open_fixture_bus(sd_bus **bus, const char *address)
{
    assert(sd_bus_new(bus) >= 0 && sd_bus_set_address(*bus, address) >= 0);
    assert(sd_bus_set_bus_client(*bus, 1) >= 0 && sd_bus_negotiate_fds(*bus, 1) >= 0 && sd_bus_start(*bus) >= 0);
}

static int serve_managed(const char *address, const char *executable_path)
{
    assert(geteuid() == 0);
    alarm(70);
    struct executable executable;
    size_t length = strlen(executable_path);
    assert(length < sizeof(executable.path));
    memcpy(executable.path, executable_path, length + 1);
    executable.pin = open(executable.path, O_PATH | O_NOFOLLOW | O_CLOEXEC);
    assert(executable.pin >= 0);
    sd_bus *bus = NULL, *authority = NULL;
    sd_event *event = NULL;
    sd_bus_slot *authority_slot = NULL;
    struct fc_broker *broker = NULL;
    struct approver approver = { .notify = -1 };
    open_fixture_bus(&bus, address);
    open_fixture_bus(&authority, address);
    assert(sd_event_new(&event) >= 0 && sd_bus_attach_event(bus, event, 0) >= 0 && sd_bus_attach_event(authority, event, 0) >= 0);
    assert(sd_bus_add_object_vtable(authority, &authority_slot, AUTHORITY_PATH, AUTHORITY_INTERFACE,
        authority_methods, &approver) >= 0 && sd_bus_request_name(authority, AUTHORITY_NAME, 0) > 0);
    assert(fc_broker_start(bus, event, verify_peer, &executable, &broker) == 0);
    puts("READY");
    assert(sd_event_loop(event) == 0);
    printf("Fixture completed %u polkit checks and canceled %u pending prompts\n", approver.checks, approver.canceled);
    fc_broker_free(broker);
    sd_bus_slot_unref(authority_slot);
    sd_bus_flush_close_unref(bus);
    sd_bus_flush_close_unref(authority);
    sd_event_unref(event);
    close(executable.pin);
    return 0;
}

int main(int argc, char **argv)
{
    setvbuf(stdout, NULL, _IONBF, 0);
    if (argc == 4 && strcmp(argv[1], "serve-managed") == 0)
        return serve_managed(argv[2], argv[3]);
    assert(argc == 3 && getuid() == 1000);
    alarm(25);
    if (strcmp(argv[1], "system") == 0) {
        system_smoke(argv[2]);
        return 0;
    }
    int control[2], notify[2];
    assert(pipe2(control, O_CLOEXEC) == 0 && pipe2(notify, O_CLOEXEC) == 0);
    pid_t child = fork();
    assert(child >= 0);
    if (child == 0) {
        close(control[1]);
        close(notify[1]);
        client(argv[1], argv[2], control[0], notify[0]);
        assert(fflush(stdout) == 0);
        _exit(0);
    }
    close(control[0]);
    close(notify[0]);

    struct executable executable;
    ssize_t size = readlink("/proc/self/exe", executable.path, sizeof(executable.path));
    assert(size > 0 && (size_t)size < sizeof(executable.path));
    executable.path[size] = 0;
    executable.pin = open(executable.path, O_PATH | O_NOFOLLOW | O_CLOEXEC);
    assert(executable.pin >= 0);
    sd_bus *bus = NULL, *authority = NULL;
    sd_event *event = NULL;
    sd_bus_slot *authority_slot = NULL;
    struct fc_broker *broker = NULL;
    struct approver approver = { .notify = notify[1] };
    assert(sd_bus_open_user(&bus) >= 0 && sd_bus_open_user(&authority) >= 0 && sd_event_new(&event) >= 0);
    assert(sd_bus_attach_event(bus, event, 0) >= 0 && sd_bus_attach_event(authority, event, 0) >= 0);
    assert(sd_bus_add_object_vtable(authority, &authority_slot, AUTHORITY_PATH, AUTHORITY_INTERFACE,
        authority_methods, &approver) >= 0);
    assert(sd_bus_request_name(authority, AUTHORITY_NAME, 0) > 0);
    assert(fc_broker_start(bus, event, verify_peer, &executable, &broker) == 0);
    assert(write(control[1], "r", 1) == 1);
    int status;
    pid_t exited;
    while ((exited = waitpid(child, &status, WNOHANG)) == 0)
        assert(sd_event_run(event, 20000) >= 0);
    assert(exited == child && status == 0 && approver.checks == 10 && approver.canceled == 3);
    fc_broker_free(broker);
    sd_bus_slot_unref(authority_slot);
    sd_bus_flush_close_unref(bus);
    sd_bus_flush_close_unref(authority);
    sd_event_unref(event);
    close(executable.pin);
    close(control[1]);
    close(notify[1]);
    return 0;
}
