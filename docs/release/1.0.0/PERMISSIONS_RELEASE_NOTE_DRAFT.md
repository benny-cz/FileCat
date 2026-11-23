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

## Elevated FileCat

An elevated FileCat, meaning a Windows administrator window or FileCat running as root on Linux or macOS, does not
offer the permissions command. Open your usual FileCat window instead.
