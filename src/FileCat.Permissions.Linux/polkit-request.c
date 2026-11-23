#include "polkit-request.h"

#include <errno.h>
#include <stdlib.h>
#include <string.h>

static const char authority[] = "org.freedesktop.PolicyKit1";
static const char authority_path[] = "/org/freedesktop/PolicyKit1/Authority";
static const char authority_interface[] = "org.freedesktop.PolicyKit1.Authority";

enum phase { preflight, interactive, finished, approved, consumed };

struct fc_polkit_request {
    const struct fc_bus_peer *peer;
    const struct fc_consent *consent;
    fc_polkit_complete complete;
    void *userdata;
    sd_bus_slot *slot;
    char *authority_owner;
    enum phase phase;
};

static struct fc_consent_requester requester_of(const struct fc_bus_peer *peer)
{
    return (struct fc_consent_requester) {
        sd_bus_message_get_sender(peer->request), peer->process.uid, peer->process.pid
    };
}

static void cancel_prompt(const struct fc_polkit_request *pending)
{
    sd_bus *bus = sd_bus_message_get_bus(pending->peer->request);
    sd_bus_message *cancel = NULL;
    if (sd_bus_message_new_method_call(bus, &cancel, pending->authority_owner, authority_path,
            authority_interface, "CancelCheckAuthorization") >= 0 &&
        sd_bus_message_append(cancel, "s", fc_consent_details(pending->consent)->nonce) >= 0 &&
        sd_bus_message_set_expect_reply(cancel, 0) >= 0)
        sd_bus_send(bus, cancel, NULL);
    sd_bus_message_unref(cancel);
}

static int authority_owner_of(sd_bus *bus, const char *sender)
{
    sd_bus_creds *credentials = NULL;
    int result = sd_bus_get_name_creds(bus, authority, SD_BUS_CREDS_UNIQUE_NAME, &credentials);
    if (result >= 0) {
        const char *owner = NULL;
        result = sd_bus_creds_get_unique_name(credentials, &owner);
        if (result >= 0 && (!owner || strcmp(owner, sender) != 0))
            result = -EPROTO;
    }
    sd_bus_creds_unref(credentials);
    return result;
}

static int parse_decision(sd_bus_message *reply, const struct fc_consent_details *expected, enum phase phase)
{
    const char *signature = sd_bus_message_get_signature(reply, 1);
    if (!signature || strcmp(signature, "(bba{ss})") != 0)
        return -EPROTO;
    int authorized, challenge;
    if (sd_bus_message_enter_container(reply, 'r', "bba{ss}") <= 0 ||
        sd_bus_message_read(reply, "bb", &authorized, &challenge) <= 0 ||
        sd_bus_message_enter_container(reply, 'a', "{ss}") <= 0)
        return -EPROTO;

    int digest = 0, retained = 0, dismissed = 0;
    const char *key, *value;
    int result;
    unsigned count = 0;
    while ((result = sd_bus_message_read(reply, "{ss}", &key, &value)) > 0) {
        if (++count > 32)
            return -EPROTO;
        if (strcmp(key, "filecat.digest") == 0) {
            if (digest || strcmp(value, expected->digest) != 0)
                return -EPROTO;
            digest = 1;
        } else if (strcmp(key, "polkit.temporary_authorization_id") == 0 ||
                   strcmp(key, "polkit.retains_authorization_after_challenge") == 0) {
            retained = 1;
        } else if (strcmp(key, "polkit.dismissed") == 0) {
            dismissed = 1;
        }
    }
    if (result < 0 || !digest || (authorized && (challenge || dismissed)))
        return -EPROTO;
    if (retained)
        return -EPERM;
    if (phase == preflight)
        return authorized ? -EPERM : (challenge ? 0 : -EACCES);
    if (authorized)
        return 0;
    if (dismissed)
        return -ECANCELED;
    return challenge ? FC_POLKIT_NO_AUTHENTICATION_AGENT : -EACCES;
}

static int receive_decision(sd_bus_message *reply, void *userdata, sd_bus_error *error);

static int submit(struct fc_polkit_request *pending)
{
    sd_bus *bus = sd_bus_message_get_bus(pending->peer->request);
    sd_bus_message *call = NULL;
    const char *destination = pending->authority_owner ? pending->authority_owner : authority;
    int result = sd_bus_message_new_method_call(bus, &call, destination, authority_path, authority_interface,
        "CheckAuthorization");
    if (result >= 0) {
        const struct fc_consent_details *details = fc_consent_details(pending->consent);
        const char *action = fc_consent_inspect_sections(pending->consent)
            ? "org.filecat.permissions.inspect" : "org.filecat.permissions.apply";
        const char *sender = sd_bus_message_get_sender(pending->peer->request);
        uint32_t flags = pending->phase == interactive ? 1u : 0u;
        /* The action policy supplies the template. Target text is a detail substituted once by polkitd. */
        result = sd_bus_message_append(call, "(sa{sv})sa{ss}us", "system-bus-name", 1, "name", "s", sender,
            action, 5, "filecat.requester", details->requester, "filecat.target", details->target,
            "filecat.scope", details->scope, "filecat.changes", details->changes, "filecat.digest", details->digest,
            flags, flags ? details->nonce : "");
    }
    if (result >= 0)
        result = sd_bus_call_async(bus, &pending->slot, call, receive_decision, pending,
            pending->phase == preflight ? UINT64_C(5000000) : FC_CONSENT_SECONDS * UINT64_C(1000000));
    sd_bus_message_unref(call);
    return result;
}

static int receive_decision(sd_bus_message *reply, void *userdata, sd_bus_error *error)
{
    (void)error;
    struct fc_polkit_request *pending = userdata;
    pending->slot = sd_bus_slot_unref(pending->slot);
    int result = sd_bus_message_get_errno(reply);
    if (result != 0) {
        result = -result;
    } else {
        const char *sender = sd_bus_message_get_sender(reply);
        if (!sender || sender[0] != ':' || sd_bus_service_name_is_valid(sender) <= 0 ||
            (pending->authority_owner && strcmp(pending->authority_owner, sender) != 0)) {
            result = -EPROTO;
        } else {
            result = authority_owner_of(sd_bus_message_get_bus(reply), sender);
            if (result >= 0)
                result = fc_bus_peer_live(pending->peer);
            if (result >= 0)
                result = parse_decision(reply, fc_consent_details(pending->consent), pending->phase);
        }
        if (result == 0 && pending->phase == preflight) {
            size_t size = strlen(sender) + 1;
            pending->authority_owner = malloc(size);
            if (!pending->authority_owner) {
                result = -ENOMEM;
            } else {
                memcpy(pending->authority_owner, sender, size);
                pending->phase = interactive;
                result = submit(pending);
                if (result >= 0)
                    return 1;
            }
        }
    }
    if (result == -ETIMEDOUT && pending->phase == interactive)
        cancel_prompt(pending);
    pending->phase = result == 0 ? approved : finished;
    pending->complete(pending->userdata, result);
    return 1;
}

int fc_polkit_start(const struct fc_bus_peer *peer, struct fc_consent *consent,
    fc_polkit_complete complete, void *userdata, struct fc_polkit_request **pending)
{
    *pending = NULL;
    if (!complete)
        return -EINVAL;
    int result = fc_bus_peer_live(peer);
    if (result < 0)
        return result;
    struct fc_consent_requester requester = requester_of(peer);
    if (!fc_consent_requester_matches(consent, &requester))
        return -EACCES;
    struct fc_polkit_request *check = calloc(1, sizeof(*check));
    if (!check)
        return -ENOMEM;
    *check = (struct fc_polkit_request) {
        .peer = peer, .consent = consent, .complete = complete, .userdata = userdata, .phase = preflight
    };
    result = fc_consent_begin_authorization(consent);
    if (result == 0)
        result = submit(check);
    if (result < 0) {
        fc_polkit_free(check);
        return result;
    }
    *pending = check;
    return 0;
}

static int claim(struct fc_polkit_request *pending, const struct fc_subject *subject,
    struct fc_security_snapshot *current, struct fc_change_projection *projection)
{
    if (pending->phase != approved)
        return -EACCES;
    pending->phase = consumed;
    int result = fc_bus_peer_live(pending->peer);
    if (result < 0)
        return result;
    struct fc_consent_requester requester = requester_of(pending->peer);
    result = fc_consent_recheck(pending->consent, &requester, subject, current, projection);
    return result < 0 ? result : fc_bus_peer_live(pending->peer);
}

int fc_polkit_inspect(struct fc_polkit_request *pending, const struct fc_subject *subject,
    struct fc_security_snapshot *observed)
{
    unsigned sections = fc_consent_inspect_sections(pending->consent);
    if (!sections)
        return -EINVAL;
    struct fc_security_snapshot before;
    int result = claim(pending, subject, &before, NULL);
    if (result < 0)
        return result;
    result = fc_security_capture(subject, sections, observed);
    if (result < 0)
        return result;
    if (!fc_subject_state_matches(&before.subject, &observed->subject))
        return -ESTALE;
    return fc_bus_peer_live(pending->peer);
}

int fc_polkit_apply(struct fc_polkit_request *pending, const struct fc_subject *subject,
    struct fc_write_result *result)
{
    *result = (struct fc_write_result) { .state = FC_WRITE_NOT_SENT };
    const struct fc_change *change = fc_consent_change(pending->consent);
    if (!change)
        return -EINVAL;
    struct fc_security_snapshot before;
    struct fc_change_projection projected;
    int admitted = claim(pending, subject, &before, &projected);
    if (admitted < 0)
        return admitted;
    fc_change_dispatch(&pending->peer->process, subject, &before, change, &projected, result);
    return 0;
}

void fc_polkit_free(struct fc_polkit_request *pending)
{
    if (!pending)
        return;
    if (pending->phase == interactive && pending->slot)
        cancel_prompt(pending);
    sd_bus_slot_unref(pending->slot);
    free(pending->authority_owner);
    free(pending);
}
