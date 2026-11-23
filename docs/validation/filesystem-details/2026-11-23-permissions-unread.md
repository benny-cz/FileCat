# Permissions that cannot be read — 23 November 2026

The column audit of 22 November found the **Permissions** column blank on Linux and macOS wherever an item's mode could not be read. The producer returned nothing when `statx` or `lstat` failed, and nothing shows as an empty cell.

The commonest case is a folder that may be listed but not searched: with `r` and no `x`, the folder names its items, but nothing about them can be looked up (`ls -l` shows question marks). An item removed after the list was read is the other.

## What FileCat does now

The cell names the failure, and pointing at it says why:

- **no access** when the system refuses the lookup (`EACCES` or `EPERM`): a folder on the way does not let this user look items up in it.
- **error** when the item is gone (`ENOENT`): it was removed or renamed after the list was read.
- **error** with the system's message and number for anything else.

## Tests

`UnixPermissionTests.Permissions_that_cannot_be_read_say_why` (Core) makes a folder readable but not searchable, then removes the file. The test is skipped for root, who may look items up in any folder.

The Mac (macOS 27.2.1, account 501) and the Ubuntu 24.04.5 guest (account 1000) built the Core and App test projects from a source archive of this work. Core ran 59 tests, App 56, and none failed. The new test ran and passed on both.
