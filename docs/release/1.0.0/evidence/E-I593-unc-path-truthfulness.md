# I593 — Copy UNC paths claimed a local Mac path was UNC

## User consequence and distinct scope

An isolated exact-`14868053` Mac app listed one owned `local.txt`, 15 bytes and SHA-256 `f1662d730215e6b1113726b55ade53c057a6f0ebb24f0eb1459607112b75f315`. **Mark → Copy UNC paths** was offered beside **Copy full paths**. With the local row focused, it put `/Users/benny/FileCatValidation/ExploratoryTwenty/journey54/source/local.txt` on the pasteboard and announced **Copied 1 UNC path to the clipboard.** That string is a normal local POSIX path, not a UNC share path. The Mac shell service's default conversion returns its input unchanged. The same can happen on Windows for an unmapped local drive because the Windows converter returns the input when no network name exists. An admin using the result as a share path could paste the wrong address into another tool. The sibling target was empty; no file was executed or mutated. The initial pasteboard was empty and was restored after the probe.

This is separate from the accepted path-breadcrumb clipboard route, which truthfully copied a *full local path*, and from the older Compare difference clipboard handoff.

## Correction and bounded verification

Product `05167b4038344d11c4931741329e8dae85d5cc85` omits this Windows-only command from non-Windows main menus and command search and refuses direct command activation there. On Windows, conversion now keeps only paths that actually have UNC syntax; if none qualify, the command gives a clear local-path instruction without replacing clipboard contents. **Copy full paths** remains the appropriate cross-platform action.

A focused App test passed **1/1 on Windows x64** and **1/1 on Mac ARM64**, covering local-path rejection, mapped-drive conversion, already-UNC input and platform applicability. A fresh Mac checkout built the exact-source `1.0.0-exploratory` package ZIP, SHA-256 `c893d0348fcd6cae51db34c5a47f653bef817c0993922ba61bf61e4f4b63c466`; staged App DLL SHA-256 `7c14d798c891ac15499e31bb9cefd4bda5b052b00ab7c6a1cd602d0a5176bfec`. The extracted bundle passed strict deep code-signature verification. In its isolated native app, Mark offered **Copy names** and **Copy full paths** with no UNC command. Command search for `UNC paths` said no command matches. The ordinary **Copy full paths** positive control copied the same exact local path. Independent rehash matched the source oracle, target stayed empty, the initial empty pasteboard was restored and both before/after test processes stopped. This does not qualify a real mapped-drive server or installed Windows candidate.

## Disposition and finite candidate rule

Medium path-output truthfulness issue, **remediated preliminarily**. On the exact installed 1.0.0 candidate, require no UNC command in Mac/Ubuntu Mark menu or command search and verify Copy full paths still works. On Windows, select one ordinary local file and require no clipboard overwrite or false UNC-success message; select one real mapped-drive or UNC file and require the resulting server/share path plus the correct count. Stop after those affected routes and latest-product five-lane CI with skip reconciliation. Public preview.5 predates I593; stable 1.0.0 remains **NO-GO** until candidate gates and explicit owner GO. The active exploratory clean streak resets to **0/20**.
