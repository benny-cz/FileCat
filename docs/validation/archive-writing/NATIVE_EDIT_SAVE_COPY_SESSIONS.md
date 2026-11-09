# Native Save copy session link and restart review

The connected implementation binds a selected new-file destination to its originating retained edit capture.
It is available only through explicit internal test composition. The public native Save copy guard and default
application store remain unchanged until the complete workflow is qualified.

`SaveNewNativeCopy` requires the originating session and native immutable snapshot, a newly opened selected
destination parent with the same canonical path, and three parent-owned reservations under one operation nonce.
Before creating any destination artifact, it publishes immutable `save-copy.json` in the held source capture.
The bounded strict record binds the session record digest, capture/source/parent identities, exact source bytes,
metadata, origin and time, plus the target, stage and two receipt names. It refuses a destination inside the
private session store, another destination returned by the factory, another session and a second attempt using
the same capture. Publication then uses the existing native preparation and exclusive new-name workflow.

After linking an attempt, closing the snapshot preserves its source records and bytes for recovery review,
including cancellation, stage creation failure and uncertain outcome recording. All native handles and the
live admission owner still retire. This is deliberately not automatic cleanup or durable retained-disk admission.

A fresh store discovers the optional source link while reviewing complete captures. It reopens only for
read-only native inspection under the recorded canonical parent, requires its current identity and validates
receipt/source/destination bindings. A complete published outcome, absent private stage and matching target
identity, bytes, required metadata, origin and time yield `MatchesRecordedCopy`. An interrupted attempt without
a preparation receipt or public target yields `Interrupted`. Changed, substituted, missing, malformed,
inaccessible or unconfirmed facts yield `NeedsReview`; unsupported source records remain visible at the capture
level. Final native re-review checks for changes during observation. No deserialized fact grants deletion,
replacement, replay or adoption authority. Linked review accounts conservatively against the existing bounded
byte budget; larger captures can require separate review rather than unbounded background hashing.

Core and Windows-platform test projects compile without warnings/errors. The next guest cohort adds 25 native
session/restart cases per platform and 14 strict-intent cases, including 10,000 seeded byte mutations. It covers
empty/large/origin-bearing sources, late editor saves, cancellation, collisions and creation/receipt failures,
wrong session/destination, repeat attempts, malformed links, changed/replaced sources/destinations, corrupt or
missing receipts and unavailable readers. Whole-fixture hashes before/after fresh recovery assert no mutation.
Runtime results remain pending; compilation is not guest qualification.

Existing-target overwrite, exact cleanup, durable retained-resource accounting, startup actions, job/UI
composition and actual crash/power-loss qualification remain open. This work does not authorize native store
registration or enable any archive write profile.
