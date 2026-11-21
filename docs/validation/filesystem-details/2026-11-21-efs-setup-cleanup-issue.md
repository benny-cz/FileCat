# EFS setup cleanup issue retention — 21 November 2026

At source commit `3db2088e7`, `ReviewEfsRecovery` records `InspectEfsSetup`'s `CleanupProblem` as a separate job issue before returning for cancellation or raising a setup failure. The issue carries the last captured setup path, the retained object identity when available, and the cleanup problem code. This prevents the cleanup failure from disappearing when the recovery-choice dialog cannot open.

The production setup routine creates the cleanup problem only after it has a held setup file and cannot confirm deletion. It retains that file for review rather than reopening a pathname to delete it. The focused deterministic `WindowsEfsSetupTests.Failed_or_canceled_setup_records_a_retained_artifact_even_without_a_recovery_dialog` theory passed both failed and canceled cases (2/2) using prebuilt managed references. The test command was:

```powershell
dotnet test tests/FileCat.Platform.Windows.Tests/FileCat.Platform.Windows.Tests.csproj --no-restore -m:1 -nodeReuse:false -p:BuildProjectReferences=false -p:UseSharedCompilation=false -p:NuGetAudit=false --filter 'FullyQualifiedName~Failed_or_canceled_setup_records_a_retained_artifact_even_without_a_recovery_dialog' --logger 'console;verbosity=normal' --verbosity quiet
```

An ordinary project-reference build reached an unrelated privileged-host native bootstrap failure, so this result does not qualify the bootstrap or a native EFS setup/cleanup path. No VM or Windows Sandbox was used for this check. EFS product availability and the N-KEY native gate remain unchanged.
