#define _GNU_SOURCE
#include "polkit-request.h"

#include <assert.h>
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <linux/posix_acl.h>
#include <stdio.h>
#include <string.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/wait.h>
#include <unistd.h>

#ifndef STATX_MNT_ID_UNIQUE
#define STATX_MNT_ID_UNIQUE 0x4000U
#endif

#define CLIENT_NAME "org.filecat.ConsentTest"
#define CLIENT_PATH "/org/filecat/ConsentTest"
#define AUTHORITY_NAME "org.freedesktop.PolicyKit1"
#define AUTHORITY_PATH "/org/freedesktop/PolicyKit1/Authority"
#define AUTHORITY_INTERFACE "org.freedesktop.PolicyKit1.Authority"

enum answer { permit, implicit, keep, dismiss, wrong_digest, no_agent, hold };

struct fixture {
    const char *sender;
    const struct fc_consent_details *details;
    const char *action;
    unsigned calls, canceled;
    enum answer answer;
    int result, complete;
};

static int challenge(sd_bus_message *request, void *userdata, sd_bus_error *error)
{
    (void)error;
    int endpoint;
    int result = fc_bus_peer_begin(userdata, request, &endpoint);
    if (result < 0)
        return result;
    result = sd_bus_reply_method_return(request, "h", endpoint);
    close(endpoint);
    return result;
}

static const sd_bus_vtable client_methods[] = {
    SD_BUS_VTABLE_START(0),
    SD_BUS_METHOD("Challenge", "", "h", challenge, SD_BUS_VTABLE_UNPRIVILEGED),
    SD_BUS_VTABLE_END
};

static void requester_process(int control)
{
    char signal;
    assert(read(control, &signal, 1) == 1 && signal == 'r');
    sd_bus *bus = NULL;
    sd_bus_message *reply = NULL;
    assert(sd_bus_open_user(&bus) >= 0);
    assert(sd_bus_call_method(bus, CLIENT_NAME, CLIENT_PATH, CLIENT_NAME, "Challenge", NULL, &reply, "") >= 0);
    int endpoint;
    assert(sd_bus_message_read(reply, "h", &endpoint) == 1);
    unsigned char proof[32];
    assert(recv(endpoint, proof, sizeof(proof), 0) == sizeof(proof));
    assert(send(endpoint, proof, sizeof(proof), MSG_NOSIGNAL) == sizeof(proof));
    assert(read(control, &signal, 1) == 1 && signal == 'q');
    sd_bus_message_unref(reply);
    sd_bus_flush_close_unref(bus);
}

static int check_authorization(sd_bus_message *request, void *userdata, sd_bus_error *error)
{
    (void)error;
    struct fixture *fixture = userdata;
    const char *kind, *key, *sender;
    assert(sd_bus_message_enter_container(request, 'r', "sa{sv}") == 1);
    assert(sd_bus_message_read(request, "s", &kind) == 1 && strcmp(kind, "system-bus-name") == 0);
    assert(sd_bus_message_enter_container(request, 'a', "{sv}") == 1);
    assert(sd_bus_message_read(request, "{sv}", &key, "s", &sender) == 1);
    assert(strcmp(key, "name") == 0 && strcmp(sender, fixture->sender) == 0);
    assert(sd_bus_message_exit_container(request) == 1 && sd_bus_message_exit_container(request) == 1);

    const char *action, *value;
    assert(sd_bus_message_read(request, "s", &action) == 1 && strcmp(action, fixture->action) == 0);
    assert(sd_bus_message_enter_container(request, 'a', "{ss}") == 1);
    const char *const keys[] = { "filecat.requester", "filecat.target", "filecat.scope", "filecat.changes", "filecat.digest" };
    const char *const values[] = {
        fixture->details->requester, fixture->details->target, fixture->details->scope,
        fixture->details->changes, fixture->details->digest
    };
    for (size_t i = 0; i < sizeof(keys) / sizeof(*keys); ++i) {
        assert(sd_bus_message_read(request, "{ss}", &key, &value) == 1);
        assert(strcmp(key, keys[i]) == 0 && strcmp(value, values[i]) == 0);
    }
    assert(sd_bus_message_read(request, "{ss}", &key, &value) == 0);
    assert(sd_bus_message_exit_container(request) == 1);
    uint32_t flags;
    const char *nonce;
    assert(sd_bus_message_read(request, "us", &flags, &nonce) == 1);
    assert(flags == fixture->calls && fixture->calls <= 1);
    assert(strcmp(nonce, flags ? fixture->details->nonce : "") == 0);
    ++fixture->calls;
    if (flags == 1 && fixture->answer == hold)
        return 1;
    if (flags == 0 && fixture->answer == keep)
        return sd_bus_reply_method_return(request, "(bba{ss})", 0, 1, 2,
            "filecat.digest", fixture->details->digest, "polkit.retains_authorization_after_challenge", "1");
    if (flags == 1 && fixture->answer == dismiss)
        return sd_bus_reply_method_return(request, "(bba{ss})", 0, 0, 2,
            "filecat.digest", fixture->details->digest, "polkit.dismissed", "true");
    int authorized = flags ? fixture->answer != no_agent : fixture->answer == implicit;
    int is_challenge = !authorized;
    const char *digest = flags && fixture->answer == wrong_digest ? "wrong reviewed request" : fixture->details->digest;
    return sd_bus_reply_method_return(request, "(bba{ss})", authorized, is_challenge, 1, "filecat.digest", digest);
}

static int cancel_authorization(sd_bus_message *request, void *userdata, sd_bus_error *error)
{
    (void)error;
    struct fixture *fixture = userdata;
    const char *nonce;
    assert(sd_bus_message_read(request, "s", &nonce) == 1);
    assert(strcmp(nonce, fixture->details->nonce) == 0 && fixture->answer == hold);
    ++fixture->canceled;
    return 1;
}

static const sd_bus_vtable authority_methods[] = {
    SD_BUS_VTABLE_START(0),
    SD_BUS_METHOD("CheckAuthorization", "(sa{sv})sa{ss}us", "(bba{ss})", check_authorization,
        SD_BUS_VTABLE_UNPRIVILEGED),
    SD_BUS_METHOD("CancelCheckAuthorization", "s", "", cancel_authorization, SD_BUS_VTABLE_UNPRIVILEGED),
    SD_BUS_VTABLE_END
};

static void bind_subject(const char *path, struct fc_subject *subject)
{
    int descriptor = open(path, O_PATH | O_NOFOLLOW | O_CLOEXEC);
    assert(descriptor >= 0);
    struct statx facts;
    unsigned mask = STATX_TYPE | STATX_INO | STATX_MNT_ID_UNIQUE;
    assert(statx(descriptor, "", AT_EMPTY_PATH, mask, &facts) == 0 && (facts.stx_mask & mask) == mask);
    struct fc_subject_identity identity = {
        facts.stx_mnt_id, facts.stx_ino, facts.stx_dev_major, facts.stx_dev_minor, facts.stx_mode & S_IFMT
    };
    struct fc_subject_state state;
    assert(fc_subject_bind(subject, descriptor, path, &identity, &state) == 0);
    assert(close(descriptor) == 0);
}

static void completed(void *userdata, int result)
{
    struct fixture *fixture = userdata;
    assert(!fixture->complete);
    fixture->complete = 1;
    fixture->result = result;
}

static void service_bus(sd_bus *mechanism, sd_bus *authority)
{
    int a = sd_bus_process(authority, NULL), m = sd_bus_process(mechanism, NULL);
    assert(a >= 0 && m >= 0);
    if (a == 0 && m == 0)
        usleep(1000);
}

static void acl_row(unsigned char *bytes, size_t index, unsigned tag, unsigned rights, uint32_t id)
{
    unsigned char *entry = bytes + 4 + 8 * index;
    entry[0] = (unsigned char)tag;
    entry[2] = (unsigned char)rights;
    for (unsigned offset = 0; offset < 4; ++offset)
        entry[4 + offset] = (unsigned char)(id >> (8 * offset));
}

static void show_acl_grants(const struct fc_bus_peer *peer, const struct fc_subject *subject)
{
    struct fc_consent_requester requester = { sd_bus_message_get_sender(peer->request), peer->process.uid, peer->process.pid };
    struct fc_security_snapshot before;
    assert(fc_security_capture(subject, FC_SECURITY_ALL, &before) == 0 && before.access.status == FC_SECURITY_ABSENT);
    unsigned char bytes[4 + 8 * 64];
    const uint32_t first_user = 12345;
    const unsigned counts[] = { 1, 60 };
    for (size_t item = 0; item < sizeof(counts) / sizeof(*counts); ++item) {
        unsigned named = counts[item];
        memset(bytes, 0, sizeof(bytes));
        bytes[0] = 2;
        acl_row(bytes, 0, ACL_USER_OBJ, 6, UINT32_MAX);
        for (unsigned row = 0; row < named; ++row)
            acl_row(bytes, row + 1, ACL_USER, ACL_READ, first_user + row);
        acl_row(bytes, named + 1, ACL_GROUP_OBJ, 0, UINT32_MAX);
        acl_row(bytes, named + 2, ACL_MASK, ACL_READ, UINT32_MAX);
        acl_row(bytes, named + 3, ACL_OTHER, 0, UINT32_MAX);
        struct fc_change change = { .kind = FC_CHANGE_ACCESS_ACL, .value.acl = { bytes, 4 + 8 * (named + 4) } };
        struct fc_consent *consent;
        int result = fc_consent_prepare(&requester, subject, &before, 0, &change, &consent);
        if (named != 1) {
            assert(result == -E2BIG && !consent);
            continue;
        }
        assert(result == 0);
        const char *prompt = fc_consent_details(consent)->changes;
        assert(strstr(prompt, "Mode 0600 → 0640") && strstr(prompt, "Access ACL"));
        assert(strstr(prompt, "UID 12345 absent → r--") && strstr(prompt, "mask absent → r--"));
        memset(bytes, 0, sizeof(bytes));
        assert(fc_consent_change(consent)->value.acl.bytes[0] == 2);
        struct fc_change_projection checked;
        assert(fc_consent_recheck(consent, &requester, subject, &before, &checked) == 0);
        fc_consent_free(consent);
    }
    puts("PASS named ACL grant is shown and copied, and oversized grants are refused without truncation");
}

static void run(sd_bus *mechanism, sd_bus *authority, struct fixture *fixture, const struct fc_bus_peer *proof,
    const struct fc_subject *subject, enum answer answer, int inspect, int drift, int expected)
{
    struct fc_security_snapshot before;
    assert(fc_security_capture(subject, inspect ? 0 : FC_SECURITY_ALL, &before) == 0);
    struct fc_consent_requester requester = { sd_bus_message_get_sender(proof->request), proof->process.uid, proof->process.pid };
    struct fc_change change = { .kind = FC_CHANGE_MODE, .value.mode = 04604 };
    struct fc_consent *consent;
    assert(fc_consent_prepare(&requester, subject, &before, inspect ? FC_SECURITY_ACCESS : 0,
        inspect ? NULL : &change, &consent) == 0);
    *fixture = (struct fixture) {
        .sender = requester.sender, .details = fc_consent_details(consent), .answer = answer,
        .action = inspect ? "org.filecat.permissions.inspect" : "org.filecat.permissions.apply"
    };
    const struct fc_consent_details *details = fixture->details;
    assert(strncmp(details->target, "\nTarget \"", 9) == 0 && strchr(details->target + 1, '\n') == NULL);
    assert(strstr(details->target, "\\u000A") && strcmp(details->requester, "\nRequester UID 1000") == 0);
    assert(strstr(details->target, "\\u202E") && strstr(details->target, "$(filecat.digest)"));
    assert(strstr(details->target, "\\xFF") && strstr(details->target, "\\u003C"));
    assert(strstr(details->scope, "2 hard links") && strstr(details->scope, "Other names are unknown"));
    if (inspect) {
        assert(strstr(details->changes, "access ACL") && !fc_consent_change(consent));
    } else {
        assert(strstr(details->changes, "Mode 0600 → 4604") && strstr(details->changes, "Add set-user-ID"));
        change.value.mode = 0600;
        assert(fc_consent_change(consent)->value.mode == 04604);
    }

    struct fc_consent *other;
    assert(fc_consent_prepare(&requester, subject, &before, inspect ? FC_SECURITY_ACCESS : 0,
        fc_consent_change(consent), &other) == 0);
    assert(strcmp(fc_consent_details(other)->nonce, details->nonce) != 0 &&
        strcmp(fc_consent_details(other)->digest, details->digest) != 0);
    fc_consent_free(other);

    struct fc_polkit_request *pending, *duplicate;
    assert(fc_polkit_start(proof, consent, completed, fixture, &pending) == 0);
    assert(fc_polkit_start(proof, consent, completed, fixture, &duplicate) == -EALREADY && !duplicate);
    struct fc_write_result written;
    struct fc_security_snapshot inspected;
    if (inspect) {
        assert(fc_polkit_inspect(pending, subject, &inspected) == -EACCES);
        assert(fc_polkit_apply(pending, subject, &written) == -EINVAL);
    } else {
        assert(fc_polkit_apply(pending, subject, &written) == -EACCES && written.state == FC_WRITE_NOT_SENT);
        assert(fc_polkit_inspect(pending, subject, &inspected) == -EINVAL);
    }
    if (answer == hold) {
        while (fixture->calls < 2)
            service_bus(mechanism, authority);
        fc_polkit_free(pending);
        while (!fixture->canceled)
            service_bus(mechanism, authority);
        assert(!fixture->complete);
    } else {
        while (!fixture->complete)
            service_bus(mechanism, authority);
        if (fixture->result != expected)
            fprintf(stderr, "Unexpected polkit result %d vs %d, calls %u\n", fixture->result, expected, fixture->calls);
        assert(fixture->result == expected);
        assert(fixture->calls == (answer == implicit || answer == keep ? 1u : 2u));
        if (drift)
            assert(chmod(subject->path, 0700) == 0);
        int claimed = inspect ? fc_polkit_inspect(pending, subject, &inspected) :
            fc_polkit_apply(pending, subject, &written);
        assert(claimed == (expected ? -EACCES : (drift ? -ESTALE : 0)));
        if (inspect) {
            assert(claimed == 0 && inspected.sections == FC_SECURITY_ACCESS &&
                inspected.access.status == FC_SECURITY_ABSENT &&
                inspected.defaults.status == FC_SECURITY_UNREQUESTED && inspected.flags.status == FC_SECURITY_UNREQUESTED);
            assert(fc_subject_state_matches(&before.subject, &inspected.subject));
            assert(fc_polkit_inspect(pending, subject, &inspected) == -EACCES);
        } else {
            if (claimed == 0) {
                assert(written.state == FC_WRITE_VERIFIED && written.has_observation && !written.operation_error &&
                    (written.observed.subject.facts.stx_mode & 07777) == 04604);
                struct stat independent;
                assert(stat(subject->path, &independent) == 0 && (independent.st_mode & 07777) == 04604);
            } else {
                assert(written.state == FC_WRITE_NOT_SENT);
            }
            assert(fc_polkit_apply(pending, subject, &written) == -EACCES && written.state == FC_WRITE_NOT_SENT);
        }
        if (drift || (claimed == 0 && !inspect))
            assert(chmod(subject->path, 0600) == 0);
        fc_polkit_free(pending);
    }
    fc_consent_free(consent);
}

int main(int argc, char **argv)
{
    assert(argc == 2 && getuid() == 1000);
    alarm(35);
    struct fc_subject subject = FC_SUBJECT_INIT;
    bind_subject(argv[1], &subject);
    int control[2];
    assert(pipe2(control, O_CLOEXEC) == 0);
    pid_t child = fork();
    assert(child >= 0);
    if (child == 0) {
        close(control[1]);
        requester_process(control[0]);
        _exit(0);
    }
    close(control[0]);
    sd_bus *mechanism = NULL, *authority = NULL;
    sd_bus_slot *request_slot = NULL, *authority_slot = NULL;
    struct fc_bus_peer proof = FC_BUS_PEER_INIT;
    struct fixture fixture = { 0 };
    assert(sd_bus_open_user(&mechanism) >= 0 && sd_bus_open_user(&authority) >= 0);
    assert(sd_bus_add_object_vtable(mechanism, &request_slot, CLIENT_PATH, CLIENT_NAME, client_methods, &proof) >= 0);
    assert(sd_bus_add_object_vtable(authority, &authority_slot, AUTHORITY_PATH, AUTHORITY_INTERFACE,
        authority_methods, &fixture) >= 0);
    assert(sd_bus_request_name(mechanism, CLIENT_NAME, 0) > 0 && sd_bus_request_name(authority, AUTHORITY_NAME, 0) > 0);
    assert(write(control[1], "r", 1) == 1);
    while (!proof.request)
        service_bus(mechanism, authority);
    int result;
    while ((result = fc_bus_peer_finish(&proof)) == -EAGAIN)
        service_bus(mechanism, authority);
    assert(result == 0 && proof.process.pid == child && fc_bus_peer_live(&proof) == 0);

    show_acl_grants(&proof, &subject);
    run(mechanism, authority, &fixture, &proof, &subject, permit, 0, 0, 0);
    puts("PASS live D-Bus sender, exact scope, escaped name, and a single verified native apply");
    run(mechanism, authority, &fixture, &proof, &subject, permit, 1, 0, 0);
    puts("PASS separate authorization returns only the selected metadata and can be used once");
    run(mechanism, authority, &fixture, &proof, &subject, permit, 0, 1, 0);
    puts("PASS approved change with a drifting native revision is refused");
    run(mechanism, authority, &fixture, &proof, &subject, implicit, 0, 0, -EPERM);
    run(mechanism, authority, &fixture, &proof, &subject, keep, 0, 0, -EPERM);
    puts("PASS implicit and retained authorization are refused before requesting interaction");
    run(mechanism, authority, &fixture, &proof, &subject, wrong_digest, 0, 0, -EPROTO);
    puts("PASS authority response with a different digest is refused");
    run(mechanism, authority, &fixture, &proof, &subject, dismiss, 0, 0, -ECANCELED);
    run(mechanism, authority, &fixture, &proof, &subject, no_agent, 0, 0, FC_POLKIT_NO_AUTHENTICATION_AGENT);
    run(mechanism, authority, &fixture, &proof, &subject, hold, 0, 0, 0);
    puts("PASS agent dismissal, missing interaction, and cancellation of a pending prompt");

    assert(write(control[1], "q", 1) == 1);
    int status;
    assert(waitpid(child, &status, 0) == child && status == 0);
    close(control[1]);
    fc_subject_close(&subject);
    fc_bus_peer_close(&proof);
    sd_bus_slot_unref(request_slot);
    sd_bus_slot_unref(authority_slot);
    sd_bus_flush_close_unref(mechanism);
    sd_bus_flush_close_unref(authority);
    return 0;
}
