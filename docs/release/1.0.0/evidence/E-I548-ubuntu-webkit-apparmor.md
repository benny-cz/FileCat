# E-I548 — Ubuntu 24 WebKit sandbox AppArmor profile

**Preliminary I08/V10/ENV-04 operational-containment child accepted at `97eb0e497d03dcfa705712458b7eecec53244eb7`; exact hosted CI, other containment boundaries and frozen-candidate qualification remain open.**

## Finding and correction

Ubuntu 24.04 restricts unprivileged user namespaces through AppArmor. Exact source `ff1585cd` enabled and verified WebKitGTK's native sandbox, then the required page test aborted after about 61 seconds with test-process exit 134. Kernel audit records show `/usr/bin/bwrap` transitioning from `unconfined` to the generic `unprivileged_userns` profile and being denied `setpcap` and `net_admin`. The same WebKitGTK 2.52.6 test passes under a named AppArmor profile with `flags=(unconfined)` and `userns,`, matching Ubuntu's installed application-profile pattern.

The correction detects an active restricted-user-namespace kernel and fails closed before creating a WebKit view when FileCat is unconfined, with a specific instruction to install the Debian package or load the supplied profile. The Debian package installs the shared product profile at `/usr/share/filecat/apparmor/filecat`, loads `/etc/apparmor.d/filecat` only when the restriction is active, and unloads/removes it on package removal. Hosted CI derives its temporary exact dotnet attachment from that same template and removes it afterward.

## Exact validation

Raw evidence is retained at `V:\FileCat\artifacts\release-evidence\i548-linux-webkit-sandbox-20261028-v1`. Its 47-entry `SHA256SUMS.txt` independently rehashes with zero mismatches; manifest SHA-256 is `8e574841746b12e3d6efefe656d275b483bdaf3ba8dd86305c5b5801ddc8ddb2`.

- The clean Ubuntu 24.04.5 VM reproduces the original exit-134 failure and exact kernel denials. The five-case profile-decision unit scope passes **5P/0F**. Without a profile, the required live page case now fails cleanly in three seconds with the explicit AppArmor message rather than aborting. With the named profile, the complete page-engine class passes **10P/0S/0F** in one second.
- The shared-template CI wrapper passes **10P/0S/0F**. The actual testhost, `bwrap` and `WebKitWebProcess` were observed under `filecat-ci-dotnet-final (unconfined)`, and the temporary CI profile/sudoers attachment was removed.
- Windows passes the same ten-case class **7P/3 explicit Linux skips/0F**. The exact isolated Release solution build has **0 warnings / 0 errors**.
- Exact `97eb0e49` on Ubuntu builds tar, Debian and AppImage payloads plus three SPDX documents. Package hashes are retained in `filecat-ubuntu24-package-sha256.txt`. The installed Debian package reports `FileCat 0.1.0-i548`; the running `/opt/filecat/FileCat` process is attached to `filecat (unconfined)`. Package removal succeeds, the product profile is absent from both `/etc` and `/usr/share`, and `aa-status` no longer lists it. The first cleanup attempt is retained separately because Ubuntu's own unattended upgrade still held the `dpkg` lock; the later cleanup passes without deleting that lock.
- The complete-history Git bundle contains `refs/heads/main` at exact `97eb0e49`; bundle SHA-256 is `aab8f9ffd69a17bc9072b59313eb56a5f16c275f6d4f8f7a560ebd91e5795ef8`.

## Boundary

This accepts Ubuntu 24 restricted-user-namespace detection, fail-closed unprofiled behavior, the shared AppArmor profile, real Debian-package attachment and cleanup. It does not replace I545's WebKit-native sandbox assertion, qualify unsupported distributions, close ordinary-user filesystem authority or other parser/Shell routes, make the current in-progress hosted run green, freeze a candidate or authorize publication. Those scopes remain in GATE-CI-FINAL, REL-I08-CONTAINMENT, REL-FQ-CANDIDATE and REL-GO-PUBLISH.
