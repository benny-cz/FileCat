# E-I03 — retain package inventories across Windows RIDs

Preliminary I03 remediation, 2026-10-02. The recorded producer writes sbom-<version>.json in each eng/publish.ps1
invocation; identical version and output root give the same path for win-x64 and win-arm64. A second producer replaces
the first inventory. The static collision is independent of payload bytes or signing.

Small remedy: output sbom-<version>-<rid>.json and update SERVICING.md to describe the actual package-inventory scope.
This remains dotnet list package output, not a complete per-artifact/native/runtime/helper SBOM. I03 stays open for its
other provenance gates. No Linux binary, Windows payload, signing or publisher policy changed.

Verification uses PowerShell's parsed actual $sbom assignment from the revised script, executed for win-x64 and
win-arm64 with version 0.1.0-i03check in a new owned evidence root, followed by the real project's native
dotnet list package --include-transitive --format json. Both JSON documents parse and remain present under distinct
names. Each SHA-256 is `05a191fe0f60eed665fc60d53213ebb8ebda01a325d5a5ac79c4ceaa05c93eb9`: the graph queried is
the same host project graph, so identical content is expected. This check proves filename retention only; it does
not claim resolved RID-specific payload inventories or execute a full Windows publish. The unchanged naming rule's
two destinations collapse to one (negative control). Publisher PowerShell parse and git diff checks pass.

Tested working publish.ps1 SHA-256 `75d3a5356749fdc217fb0190091aa5e11cead8d4f06462d7901f3e940febe6c9`.
Raw JSON/manifest in artifacts/release-evidence/i03-rid-inventory-89b0a99b3d7944ae93c6f9b28717e446/.
Initial harness executed the assignment in a child scope, leaving the caller's path unset and writing no inventory;
retry dot-sources it in a new root. Empty failed root retained. Candidate producer/SBOM validation remains mandatory.
