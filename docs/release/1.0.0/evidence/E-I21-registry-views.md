# E-I21 — the Registry's explicit 32-bit and 64-bit views without administrator rights

Issue: [I21](../FILECAT_1_0_RELEASE_ISSUES.md#i21--the-registrys-32-bit-and-64-bit-views-of-hklm-hku-and-hkcc-failed-without-administrator-rights).

## E-I21-R1 — discovery and reproduction

- **Discovery (VM, `be6ca25`):** the first unelevated run of the Windows suites in the lent Windows 11 VM (E-X01, run
  W1) failed `RegistryHardeningTests.Explicit_32_bit_view_reaches_redirected_keys_below_the_root` with
  `UnauthorizedAccessException`. Every CI lane runs elevated, so CI never exercised this path.
- **Probe (VM, unelevated, .NET runtime 10.0.6;** output `regprobe.txt`
  `847ee249c2e4040fc972d39b783cc4939928685629b71565d6cdf1751350cc5c`**):** opening `HKLM\SOFTWARE`,
  `HKLM\SOFTWARE\Microsoft`, `HKU\<own SID>\Software`, `HKU\<own SID>_Classes`, `HKU\.DEFAULT\Software` and
  `HKCC\System` through an explicit `Registry32` or `Registry64` view failed with `UnauthorizedAccessException`; the same
  keys opened in the default view, and `HKCU` and `HKCR` opened in every view. The failure is already in
  `RegistryKey.OpenBaseKey(LocalMachine | Users, Registry32 | Registry64).Handle`, before FileCat opens any subkey.
- **Mechanism:** `WindowsRegistryProvider` opened a hive with `RegistryKey.OpenBaseKey(hive, view)` and used its
  `Handle`. For an explicit view, .NET materializes that handle by opening the predefined root again with the view flag
  and write access, which a token without administrator rights is refused for the machine-wide roots.
- **Reproduction on the host:** new `RegistryStandardUserTests` run their checks on a thread impersonating a restricted
  copy of the process token (Administrators deny-only, maximum privileges removed), as a standard user or an unelevated
  administrator has. On the unchanged code, `Explicit_views_of_machine_and_user_hives_open_without_administrator_rights`
  failed with `UnauthorizedAccessException` for `HKLM\SOFTWARE`, `HKLM\SOFTWARE\Microsoft`, `HKU\<SID>\Software` and
  `HKU\<SID>_Classes`, each in both views.

## E-I21-V1 — fix `47c27b9`

- **Change:** the hive is opened from its predefined handle (`HKEY_LOCAL_MACHINE` = `0x80000002`, …) wrapped in a
  non-owning `SafeRegistryHandle` with `RegistryKey.FromHandle(handle, view)`, so the view applies to the subkeys FileCat
  opens and nothing reopens the root.
- **Tests:** the two `RegistryStandardUserTests` pass under the restricted token: every explicit view of `HKLM`, `HKU`
  and `HKCC` opens, and each view's `HKLM\SOFTWARE` subkey list equals the list Windows itself gives for that view
  (`RegistryKey.OpenBaseKey(LocalMachine, Registry32/64)` in the elevated test process, the oracle).
- **Regression:** CI run 36763921747 on `47c27b9`, all four lanes green (elevated). In the VM, unelevated, at `47c27b9`
  (E-X01 run W2): Platform.Windows 108 tests, 89 passed, 0 failed, 19 skipped, including the test that found the issue.

## Limitations

- Checked on Windows 11 Insider builds (host 26220, VM 26300) and .NET 10.0.6/10.0.12; a standard (non-administrator)
  account was simulated by a restricted token, not signed in as one (ENV-05).
- Writing through an explicit view still needs rights on the key, as it should; the change affects opening only.
