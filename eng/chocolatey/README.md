# FileCat Chocolatey package

The `filecat` package installs the official signed Windows x64 Setup for all users. It requires elevation. Windows 11 x64 is FileCat's supported desktop platform. The package keeps the x64 installer's OS gate, including Server 2016 (build 14393) and builds from 22000 onward. Windows Server needs a graphical desktop to use FileCat. The package targets native x64 only. The uninstaller uses FileCat's 64-bit machine registration and retains the same data as [the standalone uninstaller](../../docs/DOWNLOAD.md#removing-filecat).

## Build or update

Run from the repository root on Windows with Chocolatey CLI 2.x on `PATH`. Supply the exact published stable tag to update. The script accepts an immutable GitHub release, downloads its versioned x64 Setup, verifies its size, SHA-256 and timestamped Authenticode signature, and runs `choco pack`. The build receipt records the installer and package digests. A fresh pack can have a different archive digest because Chocolatey writes package archive metadata at build time.

```powershell
.\eng\chocolatey\New-FileCatPackage.ps1 -Tag v1.0.0 -OutputDirectory .\artifacts\chocolatey\package
```

## Validate

Use a disposable Windows 11 x64 VM with a clean test account and Chocolatey installed. Run the following from an elevated Windows PowerShell 5.1 session on the guest desktop. For an existing artifact, copy both the `.nupkg` and its matching `.json` receipt into the isolated guest's package directory. The test installs from the local package source, starts FileCat, forces a same-version reinstall, and uninstalls using Chocolatey. It checks the installed version, signatures, alternate registered uninstaller and preservation of settings, history, a journal fixture and an unrelated file. Discard or revert the VM after the test.

```powershell
.\eng\chocolatey\Test-FileCatPackage.ps1 -Version 1.0.0 `
  -PackageDirectory .\artifacts\chocolatey\package `
  -EvidenceDirectory .\artifacts\chocolatey\evidence -IsolatedMachine -CheckGui
```

The [Chocolatey package workflow](../../.github/workflows/chocolatey.yml) runs the package round trip on Windows Server 2025. It runs on packaging PRs and accepts a stable tag for manual validation of a future version. It uploads the package, verification receipt, validation report, and logs. Windows 11 desktop qualification uses the VM procedure above. Inno writes unique `FileCat-Chocolatey-*.log` files in the process temp directory. Setup exit `3010` signals a completed install that needs a restart. Setup exits 1–8 and nonzero uninstall exits are failures. The test requires exit `0` for a clean, fully verifiable round trip.

## Submit for moderation

Confirm the ID against the [Community Repository package list](https://community.chocolatey.org/packages?q=filecat). Use a Chocolatey Community Repository account with a configured key for `https://push.chocolatey.org/`. Retrieve the key from the [signed-in account page](https://community.chocolatey.org/account) and configure it locally with `choco apikey add --source https://push.chocolatey.org/ --key <key>` in a private session. Push the exact `.nupkg` from the successful Windows 11 test. The validation workflow has read-only GitHub permissions and no Chocolatey credentials.

```powershell
choco push .\artifacts\chocolatey\package\filecat.1.0.0.nupkg --source https://push.chocolatey.org/
```

After a successful upload, request the verifier exemption in the review comments on the pending package version. Chocolatey documents Windows Server 2019 as its verifier host, and FileCat Setup rejects that OS (build 17763). Link the Windows 11 validation evidence in the comment. Verify the version has been approved and listed in the public feed before advertising Chocolatey installation. [Package creation and pushing](https://docs.chocolatey.org/en-us/create/create-packages/), [verifier exemption procedure](https://docs.chocolatey.org/en-us/community-repository/moderation/package-verifier/), [automatic packaging](https://docs.chocolatey.org/en-us/create/automatic-packages/).
