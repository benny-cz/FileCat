# E-DEC-2026-11-24 — owner decisions that amend the frozen contract: I638, I639 and I640

Recorded 2026-11-24 by a documentation session working for the owner. Source at recording: `main` `565f7577`. Every product file cited below is identical at `v1.0.0-rc.2` (`7269ec61`) and at that `main`.

REL-CONTRACT froze the 1.0.0 contract on 2026-10-29 and reopens it "only for a later owner decision that changes a contract row". REL-V22-DOCS's [desk review](E-REL-V22-DOCS-20261124.md) found three contract rows that rc.2 no longer matches. The release session showed them to the owner on 2026-11-24 with the choice, for each, to amend the contract or change the product. The owner answered: "I639 and I640 both approved by me". For I638 the owner gave no new answer. The product follows the owner's own request of 2026-11-03 (below), and the release session told the owner on 2026-11-24 that it would amend C25 and V22 to match; the owner did not object. I638's amendment rests on that 2026-11-03 decision and stands until the owner says otherwise.

This record changes no product code. It amends contract rows and makes the user documents say what the product does.

## Summary

| Issue | Contract text before | Decision |
|---|---|---|
| [I638](../FILECAT_1_0_RELEASE_ISSUES.md#i638) | C25: "Update checks opt-in or explicit". V22: "Updates remain opt-in/explicit and never install software". | The automatic update check stays on by default for new profiles. It stays notify-only. |
| [I639](../FILECAT_1_0_RELEASE_ISSUES.md#i639) | PSD-W64: "installer minimum build 22000 (`MinVersion=10.0.22000`)", in a row that "lists exactly what is supported". | The x64 installer also accepts Windows Server 2016, exactly as `FileCat.iss` decides it (below). |
| [I640](../FILECAT_1_0_RELEASE_ISSUES.md#i640) | DEC-04: "administrator retry belongs to the installed build only". PSD-W64: "Administrator retry only in the installed build (DEC-04)". | Any Windows FileCat may reopen itself as administrator through UAC. The administrator helper still ships only in the installed build. |

<a id="i638"></a>
## I638: the update check is on by default

**Decision.** Automatic update checks stay on by default for new profiles, and Settings → Privacy turns them off. Help → Check for updates stays an explicit request. This is the owner's decision of 2026-11-03 (History, below), which came after the contract froze but did not amend it; this record makes the amendment.

**History.** `ab30f474` (2026-11-03) made `AppSettings.CheckForUpdates` default to true, added the startup release dialog with **Skip this version**, and amended ADR-15 ("Update check (amended 2026-11-03)"). [E-STARTUP-UPDATE-LIVE-20261103](E-STARTUP-UPDATE-LIVE-20261103.md) calls the feature "the owner-requested background startup update dialog" and tests its opt-out. C25 and V22 were not amended then.

**The product at rc.2.**
- `UpdateCheck.RunScheduledAsync` runs after the main window opens, when the setting is on and the last completed check is at least 24 hours old.
- It makes one HTTPS request to the GitHub releases API: `releases/latest` for a release build, the ten newest releases for a build whose version has a prerelease label. It uses the system proxy and a 20-second deadline, and reads at most 4 MiB.
- The only FileCat information it sends is the version, in the User-Agent (`FileCat/<version>`).
- A newer release opens a dialog with **Not now**, **Skip this version** and **Open release page**. FileCat opens the page only when asked. It never downloads or installs anything.
- A saved off preference stays off. A skipped version is hidden from automatic checks only.

**Amended rows.** C25 and V22 in the [readiness plan](../../../design/FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md), and the release-note item of its section 13.2. SECURITY.md no longer calls the check opt-in. PRIVACY.md and SERVICING.md already described the default.

**Residual.**
- A new profile contacts GitHub at its first start without asking. GitHub, and any proxy on the way, sees the network address the request comes from, the time and the version.
- The timestamp is saved only when a check finishes, and a check that fails offline or times out counts as finished. A check still running when the main window closes saves nothing, so the next start sends the request again. "At most once a day" therefore holds only for checks that finish. PRIVACY.md now says so. The product's Settings note ("At most once a day at startup") and ADR-15 ("At most one request a day") still leave out that exception; this record does not change them.

<a id="i639"></a>
## I639: the x64 installer also accepts Windows Server 2016

**Decision.** The x64 Setup's acceptance, as built, is the contract's scope for the installer.

**The product.** `e5cf68f2` (2026-11-23) changed `eng/installer/FileCat.iss`:
- **x64:** `MinVersion=10.0.14393`. `InitializeSetup` then accepts Windows build 22000 or later, or build 14393 when Windows reports a server or domain-controller product type. Anything else gets "FileCat Setup requires Windows 11 or Windows Server 2016 (x64)." and Setup ends.
- **ARM64:** `MinVersion=10.0.22000`, unchanged. The Server 2016 exception is x64 only.

| System | Build | x64 Setup |
|---|---|---|
| Windows 11 | 22000 or later | Accepted |
| Windows Server 2016 | 14393, server or domain-controller product type | Accepted |
| Windows 10, versions 1607 and Enterprise 2016 LTSB included | up to 19045; 14393 with a workstation product type | Refused |
| Windows Server 2019 | 17763 | Refused |
| Windows Server 2022 | 20348 | Refused |
| Windows Server 2025 | 26100 | Accepted by the build test; not tried |

Any other x64 Windows with build 22000 or later passes the same test. Nothing checks for Desktop Experience, so Setup also accepts Server 2016 without it (Server Core). The README and CAPABILITIES.md name Server 2016 with Desktop Experience and say that Setup does not check it.

**Evidence.** One run, recorded only in `e5cf68f2`'s commit message. On 2026-11-23 the x64 Setup built from `3c9c427a3` installed silently on a Windows Server 2016 Datacenter Evaluation VM (build 14393). FileCat started for the built-in Administrator, listed the drives and the profile, navigated by its command line, opened a file in the viewer and showed version 1.0.0 in About. The uninstaller removed the program folder, the Start menu shortcut and the uninstall entry. That build was not a candidate, and no release candidate has been recorded on Server 2016.

**Correction.** I639's title in the issue register, the desk review and the dashboard say the installer accepts Windows 10. `InitializeSetup` refuses Windows 10, build 14393 included, because its product type is workstation.

**Amended rows.** PSD-W64 and its summary in section 4.4 of the readiness plan, with a clarifying note on PSD-WA's "same installer minimum". PSD-W64 stays Stable, Tier A, and its qualification on a physical GA Windows 11 x64 PC (ENV-03) is unchanged.

**Residual.**
- Server 2016's only evidence is the one VM run above.
- Microsoft's [lifecycle page](https://learn.microsoft.com/en-us/lifecycle/products/windows-server-2016) gives 13 January 2027 as Server 2016's extended end date (read 2026-11-24), with paid Extended Security Updates until January 2030. Plan section 4.1 asks that version support follow the edition's servicing lifecycle.
- About names Server 2016 "Windows 10 (build 14393, x64)".
- Server Core and Server 2025 pass Setup untried.
- The portable and framework-dependent ZIPs check no Windows version. Nothing is claimed for them beyond PSD-W64; plan section 4.2 infers no Windows 10 support from a launch.

<a id="i640"></a>
## I640: FileCat reopens itself as administrator outside the installed build

**Decision.** DEC-04's "administrator retry belongs to the installed build only" is amended. Any non-elevated Windows FileCat, the portable and framework-dependent ZIPs included, may reopen itself as administrator through UAC. The administrator helper (`FileCat.PrivilegedHost`), and with it the per-plan **Retry as administrator**, still ships only in the installed build.

**The product.** Since `2f786b07` (2026-11-19), `AdministratorWindow.CreateStartInfo` (`src/FileCat.App/Services/AdministratorWindow.cs`) starts the running executable, `Environment.ProcessPath`, through the shell with the verb `runas`. When FileCat was started through `dotnet.exe`, it starts the `FileCat.exe` beside the app instead. The new process gets the system folder as working directory, `--new-instance` and its request's own arguments, but not the ordinary window's options or data paths. FileCat offers it whenever it runs on Windows without elevation; nothing checks the package or the executable's folder. The routes at rc.2:
- Tools → Open FileCat as administrator;
- Tools → Change journal of this drive;
- **Reopen folder as administrator** for an access-denied local folder, and **Open folder as administrator** in the permissions inspector;
- Create link → **Review as administrator**;
- the administrator reviews offered by the viewer, checksums, Find, Registry keys and exports, recovery and Bulk Rename;
- the Operations retry of a denied empty-file creation, recursive attribute change or timestamp change;
- the access-denied file retry (copy, same-drive move, rename, folder creation) when no helper plan is available, which is always the case in a portable or framework-dependent copy.

The desk review named five of these routes; the others come from the callers of `OpenAdministratorWindowAsync` and `RequestAdministratorTaskAsync`.

**Unchanged.** `eng/publish.ps1` removes `FileCat.PrivilegedHost` from the portable payload and fails if it appears in the framework-dependent payload. The helper runs only from Program Files (ADR-14). DEC-04's rule for the FDD ZIP stands.

**Residual.**
- An administrator window runs the program files of the copy that opened it. An elevated portable or framework-dependent copy loads its executable, DLLs and runtime configuration from its own folder. Extracted by an ordinary account, that folder is usually writable by that account and by any program running as it. Such a program can change the copy so that its code runs with administrator rights the next time the copy is opened as administrator.
- DEC-04 and the helper's loader fixes (I154 to I156, I158 and I159) keep code that the ordinary account can choose or change away from the helper's elevation. The administrator window has neither protection.
- An installed copy that the user put outside Program Files is in the same position. The helper refuses to run there; the administrator window does not check.
- The owner accepted this with guidance instead of a check. README.md, SECURITY.md, docs/CAPABILITIES.md and docs/SERVICING.md tell users who open a portable or framework-dependent copy as administrator to keep it in a folder only administrators can change, such as one under Program Files, or to use the installer.
- **Not examined here.** The administrator window is the ordinary `FileCat.exe`. Unlike the helper, it neither disables startup hooks (`StartupHookSupport`) nor removes .NET runtime environment variables before the runtime starts. Whether a user's environment can reach an elevated window, for any package, is for V06's loader audit.

**Amended rows.** DEC-04 in the [blocker register](../FILECAT_1_0_RELEASE_BLOCKERS.md), PSD-W64's limits, and V06's "portable retry unavailable" in the readiness plan.

## Files changed with this record

- `docs/design/FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md`: dated amendment notes on C25, PSD-W64 and its summary, PSD-WA, V06, V22 and section 13.2.
- `docs/release/1.0.0/FILECAT_1_0_RELEASE_BLOCKERS.md`: the DEC-04 amendment.
- `docs/release/1.0.0/evidence/E-DEC-2026-10-28-owner-decisions.md`: a pointer from DEC-04 to this record.
- `SECURITY.md`: the update check's default; the diagnostics bundle's log, whose error messages and crash reports can name files (`AppLog.Write` keeps exception messages, and `crash.log` whole exceptions), and its plain-text settings; the operating system and package on every platform; and administrator windows from portable copies.
- `docs/PRIVACY.md`: when the startup check repeats, and what the bundle's log and settings contain.
- `docs/SERVICING.md`: the signing statement, and the helper and administrator window per package.
- `docs/CAPABILITIES.md`: the Windows x64 row (Windows 11 or Windows Server 2016 for the x64 installer; administrator windows) and the per-plan broker row.
- `README.md`: the administrator-window guidance, and the Server 2016 sentence.
- `eng/package-linux.sh`: the .deb description no longer mentions phones, since MTP is not available on Linux. The next .deb's control file changes.

## Not decided here

- The 1.0.0 release notes, preview labels and signing statement, and the README and DOWNLOAD.md text for 1.0.0.
- Product text, such as the Settings note's "At most once a day".
- Any check of an administrator window's folder or environment.
- Qualification of Server 2016 by a candidate, and the stable GO.
