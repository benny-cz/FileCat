# E-DEC-2026-11-20 — owner decisions on the Windows signing route and signer identity

Recorded 2026-11-20 by the release session working for the owner (benny-cz). Source at recording: `main` `0725a6a6`.

## What the owner was asked and answered

The owner's message, verbatim: "for signing - approved to show my name and city and to replace SignPath with ssl.com".

| ID | Question | Owner's answer |
|---|---|---|
| DEC-12 | Replace the SignPath-specific signing route (D-40, A-11, release plan section 10.4) with SSL.com eSigner signing in the credential-isolated GitHub Actions pipeline? | Approved: "replace SignPath with ssl.com". |
| DEC-13 | May FileCat's Windows signatures, and with them UAC and download dialogs, show the certificate subject: the owner's legal name and city? | Approved: "show my name and city". |

## Context

- SignPath Foundation declined FileCat on 2026-10-31. Section 10.4 of the release plan names an alternative certificate route for that case, to be chosen by an explicit owner decision; DEC-12 is that decision.
- On 2026-11-20 the owner obtained an SSL.com individual-validated code-signing certificate held in eSigner, and put its credentials in the protected `signing` environment.
- Pilot run 44822592170 signed with that certificate and passed verification against Windows' roots with revocation.
- A preliminary Smart App Control check on an Insider VM allowed the signed files and blocked unsigned and tampered controls. See [E-SIGN-DRYRUN-20261120](E-SIGN-DRYRUN-20261120.md).

## What DEC-12 settles and keeps

**Settled.** Stable Windows signing uses the owner's SSL.com certificate through eSigner:

- in the protected `signing` environment: the owner approves each run, administrator bypass is off, and only `main` and `v*` tags deploy;
- with credentials only in the signing steps, on a machine that never ran the build;
- with a canary signature checked against the pinned certificate;
- with signed outputs in a private draft until the owner publishes.

**Kept.** The controls the 2026-10-31 reassessment required of any replacement route:

- immutable source identity (the build job's payload manifest);
- protected review (environment approval);
- exact unsigned-input and signed-output manifests;
- signer identity (the pin);
- RFC 3161 timestamps;
- chain validation against Windows' roots with revocation;
- final installed bytes (the silent install round trip);
- Smart App Control on the GA tier;
- a separate final GO.

**Superseded.** SignPath's own terms, OSI-only eligibility and trusted-build integration no longer constrain the route. DEC-07's SignPath prerequisite no longer applies; previews already published stay as they are. EXT-01 is resolved.

## What DEC-13 settles

Signatures, and so Windows' publisher prompts, carry the certificate subject: the owner's legal name and city, as the installer's AppPublisher already names the owner. This record approves that disclosure in signed artifacts. It does not put the subject or the certificate's thumbprint into the repository; the thumbprint pin stays in the `ESIGNER_CERT_SHA256` environment secret.

## Not decided here

- Bringing signing into the tagged candidate pipeline (engineering).
- GA-tier Smart App Control evidence (ENV-03).
- The stable GO.
