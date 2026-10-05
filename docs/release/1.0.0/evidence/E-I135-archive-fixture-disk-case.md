# E-I135 - archive size regressions require canonical fixture identities

**Requirement:** V13, reliable identity/case validation.
**Severity/disposition:** Low, validation reliability; must fix.
**Status:** remediated at `3caf480`; verified preliminary test repair on host, clean CI and both VMs.
**Baseline:** committed I133 tests at `578a0ed`, with the separately retained I134 Archives working overlay.

An affected 100-case archive run passes 89, skips two declared benchmarks and fails nine I133 initial
search cases. Windows' default TEMP uses `C:\WINDOWS`; the on-disk spelling is `C:\Windows`. The fixture's
direct member lookup preserves the supplied spelling, while initial search intentionally canonicalizes
its root. Content/size outcomes are correct; only original ItemRef/parent string equality fails.
All narrowed controls pass. The earlier I133 owned-temp host/CI/guest passes retain their own scope.

Private `archive-search-formats-20261005/i134-working-v1` retains the exact source snapshot, command,
logs and full failed TRX inventory. The correction canonicalizes the fixture path after creation and
before building its original member identity. No assertion is removed, no product behavior changes,
and all content/filter/typed parent/name/relative-scope oracles remain. The same default-TEMP affected
run must pass before remediation is claimed. Clean CI/native execution and candidate scope remain.

The Core correction passes all 98 affected cases with two declared benchmark skips under the same
default TEMP. A following Find run exposes the same mismatch in its initial-search case (three passes,
one failure); that full output remains retained in `i134-working-v2`. Its owned scope is likewise
canonicalized before constructing the original member. This is the same fixture precondition in both
test projects. The complete affected run is repeated after both corrections.

The final same-default-TEMP run passes all 98 affected Core cases (two declared benchmark skips) and
all four Find cases without skips. This includes every twenty-case I133 control and its four Find
controls, with all original assertions retained. Corrected source snapshots/full TRX inventories and
the unchanged thirteen-format corpus are independently verified in `i134-working-v4`. An intervening
App compilation misses the namespace import and is retained as failed; the corrected build passes.
I134's separate production overlay remains explicitly pinned. Clean CI/native/candidate revalidation
remains; no product path behavior was changed for I135.

Clean `3caf48088a4aaeed1ebd471b1c4aaf9266dd9dde` passes all four required CI jobs. All twenty I133
Core cases pass without skips in Windows' full inventory; all four Find cases pass without skips
in each Windows/Ubuntu/macOS App inventory. Four artifact server digests/six complete TRX inventories
verify. Both SDK-free Windows/Ubuntu guests pass all 71 affected archive/Find cases, including these
controls, with zero skips. Source/fixture/payload/case/output pins and post-bootstrap process/temp
cleanup independently verify. Combined CI/native proof hashes and full scope are in
[E-I134](E-I134-empty-7z-members.md). Original failed runs remain failed; native/AT/candidate obligations
are not closed, and I134's separate production correction retains its own identity.
