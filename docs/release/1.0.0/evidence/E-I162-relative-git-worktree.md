# E-I162 — relative Git worktree bypasses local-path admission

2026-10-06. Discovered against exact source
`850298359fc16cf28503cea42f3ba9c0b1599240`, whose Git reader source remains unchanged
on current main. High path-admission risk; must fix under I16/V23 B10/V24.
Status: Open, reproduced; remediation and affected revalidation follow.

A private BCL-only probe loads the actual production FileCat component by
reflection and calls its production `SafeRepository` and `ReadAsync`, with the
installed Git executable explicitly selected and pinned. Its five actual local
Git cases establish ordinary badges, an ordinary absolute worktree redirect,
refusal of an absolute junction path, acceptance of the same junction through
`core.worktree=../target-link`, and restored ordinary badges. The relative case
admits the repository and returns Untracked for a file only in the owned target.
The absolute spelling of that same junction returns no snapshot/admission.

Expected: inspect the resolved relative worktree path for the same local/junction
policy before Git runs. Actual: `NamesThisComputer` accepts relative values
without resolving them. The metadata-tree check does not cover the separately
configured worktree. This is native component evidence of inconsistent admission
and redirected reads, not measured network contact, credential transmission,
execution of repository-defined programs or installed-candidate qualification.

The fixture uses only owned local folders and one junction. Its exact junction
is removed without recursion; target file bytes and every production payload
pin remain unchanged. The original configuration is restored. No physical source,
VM/Mac access, user/system setting or external service is used. The probe does not
execute FileCat's GUI or administrator helper.

Private `FileCatReleaseEvidence/git-relative-worktree-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-relative-worktree-v1.json | 4f2c62b7a8461a2ee9ce40fb0251caf978b8c540e2dc7425e2f839b70386fb2a |
