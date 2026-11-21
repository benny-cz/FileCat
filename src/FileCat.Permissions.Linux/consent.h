#ifndef FILECAT_PERMISSIONS_CONSENT_H
#define FILECAT_PERMISSIONS_CONSENT_H

#include "change.h"

enum {
    FC_CONSENT_NONCE_LENGTH = 32,
    FC_CONSENT_DIGEST_LENGTH = 64,
    FC_CONSENT_SECONDS = 90
};

struct fc_consent;

/* The mechanism obtains these values from its live, authenticated bus and process proof. */
struct fc_consent_requester {
    const char *sender;
    uid_t uid;
    pid_t pid;
};

struct fc_consent_details {
    const char *requester;
    const char *target;
    const char *scope;
    const char *changes;
    const char *nonce;
    const char *digest;
};

/* A single subject, either metadata-only inspection or one typed change. */
int fc_consent_prepare(const struct fc_consent_requester *requester, const struct fc_subject *subject,
    const struct fc_security_snapshot *before, unsigned inspect_sections, const struct fc_change *change,
    struct fc_consent **prepared);
const struct fc_consent_details *fc_consent_details(const struct fc_consent *consent);
const struct fc_change *fc_consent_change(const struct fc_consent *consent);
unsigned fc_consent_inspect_sections(const struct fc_consent *consent);
int fc_consent_requester_matches(const struct fc_consent *consent, const struct fc_consent_requester *requester);
int fc_consent_begin_authorization(struct fc_consent *consent);
/* Recheck the same peer, exact subject and reviewed revision before admitting the approved operation. */
int fc_consent_recheck(const struct fc_consent *consent, const struct fc_consent_requester *requester,
    const struct fc_subject *subject, struct fc_security_snapshot *current, struct fc_change_projection *projection);
void fc_consent_free(struct fc_consent *consent);

#endif
