# Permissions and ownership — unreleased note

This covers the permissions work from `codex/permissions-acl`. It does not change a published preview or claim that
these changes are in a downloadable package. The "Permissions" rows of the [capability matrix](../../CAPABILITIES.md)
describe what each platform offers, and the [implementation plan](../../design/FILECAT_PERMISSIONS_ACL_IMPLEMENTATION_PLAN.md)
records the qualified filesystems, routes and evidence.

## What macOS does not do in this release

The macOS package stays an ad-hoc signed preview without an authorized helper (release decision DEC-03, kept on
23 November 2026). On macOS FileCat inspects permissions and makes the reviewed changes your own account may make:
mode, ACL entries, a group you belong to, Finder Locked and append-only, timestamps and Finder Hidden. It does not:

- change an item's owner;
- change items that belong to other users or to root;
- change system (root-only) flags such as system immutable and system append-only;
- switch a volume's Ignore ownership setting.

Each of these needs an authorized helper, which needs an Apple-issued signing identity, notarization and a signed
installer package.

## The inspector

**Permissions and ownership** shows the item first, then one section for each part of its security: ownership,
mode, file flags, access rules, defaults for new children, inheritance and auditing. Access rules are a table in
plain language (on Windows, Explorer's names: Full control, Read and execute, This folder, subfolders and files);
the chosen rule shows every right it grants, the account's full name and ID, and the exact entry with its mask and
flags. Account names are looked up as the dialog opens. Each section holds the actions that change it, and the
dialog keeps the size you give it, as other resizable FileCat dialogs now do.

## Administrator approval on Windows

The installed Windows x64 FileCat offers **Inspect with authorization…** without any setting: Windows asks for
administrator approval, and FileCat's own consent names the exact change. Portable copies and Windows on Arm do not
offer it.

## Elevated FileCat

An elevated FileCat, meaning a Windows administrator window or FileCat running as root on Linux or macOS, inspects
permissions read-only: the inspector says so and offers no change. On Windows x64 an administrator window reads with
its own backup and security privileges, so it also shows audit rules and items whose access rules deny the
Administrators group. From an ordinary window, **Open folder as administrator…** in the inspector opens the item's
folder in such a window after Windows' approval. Make changes from your usual FileCat window. When Windows gives the sign-in no standard-user
token (UAC off, or the built-in Administrator account), the inspector says that changes need a standard sign-in.
