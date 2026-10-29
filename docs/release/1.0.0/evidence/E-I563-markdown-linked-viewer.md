# E-I563 — linked Markdown opens in a separate viewer

Recorded 2026-10-29. Product and test source:
`3a1c118c07a57fdea81332b7951797421a7a4de2`. This is preliminary
source/native acceptance, not installed or final-candidate qualification.

## Finding, decision and correction

The owner reported that the Markdown Page view showed a local `.md` link as
hover-only text. A separate Viewer window was chosen so ordinary click can
follow the file without adding back/forward history. Only relative Markdown
targets inside the viewed disk file's folder tree are offered as links. A
missing file, `..` traversal, outside symlink, absolute/scheme destination or
non-Markdown target is refused. Archive-backed Markdown leaves local links
inert because it has no corresponding local folder. Section fragments are
currently ignored in the new window.

The owner then tried the first implementation: the linked file opened, but
the original viewer fell from Page to Text, and its Page button did nothing.
The Windows VM test reproduced the cause before the correction: WebView2
reported a successful initial load followed by two `OperationCanceled`
`Loaded(false)` callbacks for the host-handled good and refused missing links.
The PageView treated those expected navigation cancellations as page failures
and marked Page mode unavailable. The corrected WebView2 adapter ignores
`OperationCanceled` completions; genuine page-load failures remain reported.
The owner subsequently confirmed that a real Windows click works. That owner
observation has no independently pinned installed binary and is not claimed
as candidate qualification.

The first proposed resolver allowed a parent (`../`) link to leave the folder.
Automatic approval review rejected it for that containment risk. The
implemented resolver rejects parent segments and independently checks the
lexical and symlink-resolved target beneath the source folder, using an
ordinal comparison on Unix, including case-sensitive APFS volumes.

## Exact controls

| Environment | Result | Method and limit |
|---|---|---|
| Windows host, Core | 71 passed, one expected Unix FIFO skip | Focused `MarkdownTests` and `HtmlPageTests`: rendered/archived links, relative target, traversal, outside symlink, missing target and existing page policy. |
| Windows host, headless App | 12 passed, four expected Linux platform skips | Focused PageView/ViewerWindow/LinuxPageEngine compile and controls. |
| Windows 11 Insider x64 VMware guest | Initial controlled case **failed 1/1**, corrected exact-source case **passed 1/1** | Host-controlled click activated the actual rendered anchor. The good target produced one exact new-viewer request and no target-page load; the missing target was refused. The corrected run had no false load completion and kept the `index.md` title. The original failure recorded `True, False(OperationCanceled), False(OperationCanceled)`. |
| macOS 27.0.1 ARM64 | PASS, including twelve existing page lifecycle rounds | WKWebView policy routed the exact contained target, refused the missing target, retained `README.md`, and emitted zero cancellation load failures. The test used a host navigation through the native policy callback. |
| Ubuntu 24.04.5 x64 VMware guest | Native WebKitGTK case 1 passed, zero skipped | Native policy routed the exact contained target, refused the missing target and emitted zero cancellation load failures. A temporary AppArmor user-namespace profile matching `eng/linux/filecat.apparmor` was loaded for the standalone .NET host and removed on exit. The first unprofiled attempt failed at the expected sandbox gate. |

The first Mac smoke failed only because its assertion compared `/var` with
macOS's canonical `/private/var` path as literal strings; both resolved to
the same target. The corrected assertion checks the target suffix and exact
file bytes. That original test failure remains in the private evidence.

Private root: `V:\FileCat\artifacts\release-evidence\i563-markdown-links-20261029`.
SHA-256 of the exact-source Windows TRX:
`c6dae918616ca70d12653592d56205cb448813cbe1feb85455d8b692c9ef13e1`;
Mac result:
`dd6339ca965c6a4cb45b7e0a1bfa1816b1c0b714be8ce2b0c03b8fbabf8a3eaa`;
Ubuntu TRX:
`27d6299986d430c5d773474e7addc246eef3876ee4b2f9a8fe6b69f272bd187c`.
The controlled Windows failure TRX is
`607ded14d2d6bfc0874d8a426492c4c9067f643ba98196dbc6fad513967f094d`.
All raw logs, TRXs and compact test bundles remain in that root.

## Stopping condition and gap

The source/native child is accepted at `3a1c118c`. Hosted CI at the pushed
product head remains GATE-CI-FINAL, and the actual separate-window plus
original-Page-state interaction must be repeated on frozen installed
candidate artifacts under REL-FQ-CORE. No release candidate exists yet.
