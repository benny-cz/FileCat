# RC exclusion from the unsigned preview draft

Baseline: `b4f7130ba56e89ad5d6c3e9fcda2c0693c395f27`. No RC tag or signed candidate exists.

The draft job in `.github/workflows/ci.yml` used `startsWith(github.ref, 'refs/tags/v')`. Its read-only preflight and asset guard accepted any syntactically valid prerelease, including `v1.0.0-rc.1`. An owner review was still required before upload, but approving that job for a future RC would create an **unsigned preview draft** using RC artifacts. That conflicts with the separate signed-candidate path in the 1.0 release plan. This is a prospective release-control gap; no RC release or incorrect publication was observed.

The job is now eligible only for `v1.0.0-preview.*`. Both the combined preflight and per-platform asset guard independently require the exact `v1.0.0-preview.N` form with a positive integer `N`. A future RC may still run producer/package CI, but cannot enter this unsigned draft path. It needs its own origin-bound signing, final-byte inventory, protected private candidate and promotion controls before any RC tag is authorized. Stable tags remain refused by the producer policy.

Focused synthetic controls were moved from an RC reference to the actual preview reference form and gained explicit RC refusal cases. Local results on Windows PowerShell 7.6.9:

| Control | Result |
| --- | --- |
| Three-platform draft package preflight/upload fixture | 14/14 passed, including RC and stable refusals |
| Per-platform draft asset preflight/upload fixture | 37/37 passed, including RC and stable refusals |
| Producer-reference policy fixture | 16/16 passed; RC remains a valid producer reference, stable remains refused |
| Workflow syntax | PyYAML parse passed; `draft-preview.if` is `startsWith(github.ref, 'refs/tags/v1.0.0-preview.')` |

The first local fixture attempt was invalid because this sandbox's temporary path contained `{}` and the asset selector correctly refused action-glob metacharacters. A second attempted scratch root under `artifacts/` was denied by that directory's ACL. The final run used a fresh `.release-validation-temp` root within the checkout and passed; that root was removed after confirming only its empty owned `FileCatValidation` directory remained. Neither fixture used the real GitHub API, created a tag, changed a release or signed a package.

**Disposition:** preliminary source/workflow guard only. `REL-PIPELINE` stays Open for the actual signed-candidate and manifest-only stable-promotion negative/positive controls. The owner withdrew an accidental own-certificate choice, has no certificate/provider and is reconsidering a public-preview prerequisite. The verified preview.3 draft remains private until a separate exact publication approval; this guard does not authorize it.
