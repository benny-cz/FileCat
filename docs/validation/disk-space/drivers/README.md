# Native development controllers

These are the exact r60 controllers for the generated disk-space harness fixtures, retained so their evidence can be reviewed. They are not general desktop automation or complete feature/accessibility qualification. Paths and archive hashes identify the isolated r60 guest stages; reruns need a fresh task-owned stage, the corresponding bundle and updated expected hash. Never point them at a user's profile or existing files.

Mac uses System Events accessibility selection/menu actions and native Return/Space. Captures use the unchanged, already authorized FileCatCapture helper through Launch Services. Ubuntu uses observed AT-SPI targets and native X11 pointer/keyboard input. Both controllers gate competing app/test processes and request closure of their exact owned run in `finally`.

The Windows PowerShell script stages a hash-checked bundle or observes the exact owned native process and its exit. The app itself is launched interactively inside the VM with `--windows-input`; input and desktop guards are in `tests/FileCat.NativeAcceptance/DiskSpaceHarness/WindowsInput.cs`. The observer is hidden and does not need interactive mode. Do not run a live UI test on the host. A blocked foreground, competing process, deadline close or incomplete cleanup is not a passing receipt.
