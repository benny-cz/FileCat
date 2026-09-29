# ADR-12: Platform adapters and packaging

**Status:** Decided (2026-09-28) for Windows x64, Linux x64, and macOS on Apple silicon; Windows ARM64 added by D-48
(2026-09-29, addendum below). TV-10 and TV-13 checks on real machines remain release gates.

## Decision

- **Explicit adapters.** Core is portable and holds no Windows assumptions; `FileCat.Platform.Windows` supplies native
  copies, the Recycle Bin, Shell integration, the Registry, MTP, SMB sign-in, Credential Manager, and the elevation
  broker. The portable platform serves Linux and macOS with their own trash (freedesktop.org, macOS volumes), POSIX
  permissions and ownership, extended-attribute origin marks, the keychain or Secret Service, and their terminals.
  The capability matrix lists what each platform lacks, and the app explains it where users meet it.
- **One application build**, targeting plain .NET, guards every native entry point, so the same assemblies run
  everywhere and platform code cannot leak into Core.
- **Packages per platform, each started in CI:**
  - Windows: a per-machine installer (Inno Setup) plus portable and framework-dependent ZIPs (ADR-15);
  - Linux: a `.tar.gz` with a menu-entry script, a `.deb` with declared dependencies, and an AppImage;
  - macOS: a zipped `.app`, ad-hoc signed (Developer ID signing and notarization are not approved).
- MSIX and Flatpak were not adopted: both would need separate validation of the Registry, elevation, and system tools.

## Consequences

- Promotion is per OS and architecture; a successful compile promises nothing.
- Portable builds never elevate (ADR-14). Linux and macOS read drives through the system's own authorization (D-47), not a helper.

## Addendum (2026-09-29): Windows ARM64 (D-48)

- Release builds publish `win-arm64` beside `win-x64`: installer, portable and framework-dependent ZIPs, with FileCat,
  the administrator helper, and the Shell helper all native ARM64 (every native library in the build checked ARM64).
- The installer script takes the architecture; the ARM64 installer installs only on ARM64 Windows, while the x64 one
  also installs there and runs emulated, which FileCat's About says.
- Every push builds and tests FileCat on a Windows ARM64 runner, starts the ARM64 package, and has it draw its window
  (a screenshot artifact); physical ARM64 devices (TV-13) stay a release gate.
- Shell extensions that exist only as x64 DLLs cannot load into an ARM64 FileCat, as in Explorer on ARM64.
