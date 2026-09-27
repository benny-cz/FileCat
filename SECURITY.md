# Security policy

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub's **Report a vulnerability** button, on the repository's
**Security** tab (private vulnerability reporting). Do not open a public issue for a security problem.

Include:

- what an attacker controls, such as a crafted archive, a file name, a shared folder, or a settings file;
- the impact;
- the FileCat version (Help → About) and the Windows version;
- the smallest reproduction you can share.

Please do not include personal files. A diagnostics bundle (Tools → Export diagnostics) hashes paths by default
and never contains file contents or credentials.

Expected response:

- an acknowledgement within 7 days;
- an assessment within 14 days;
- for confirmed issues, a fix in a security release (see [servicing](docs/SERVICING.md)), then public disclosure once
  users can update. Reporters are credited unless they prefer otherwise.

## Supported versions

Security fixes go into the latest release. There is no updater, so each fix ships as a new release. The opt-in
update check (Settings → Privacy) or Help → Check for updates tells you when one is available.

## In scope

These are examples, not an exhaustive list:

- Code execution or argument injection through file names, paths, or tool placeholders (plan §14.2, TV-17).
- Archive handling that escapes the destination, bypasses resource limits, or loses Mark-of-the-Web without saying so.
- Silent data loss, silent permanent deletion, or undo that touches items it did not create.
- Privacy defects: paths or contents in logs outside diagnostic mode, or network requests while the update check is off.

## Out of scope

- Behavior of external programs that the user configured, such as tools, editors, or associations.
- Attacks that need administrator rights, or physical access to an unlocked session.
