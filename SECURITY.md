# Security policy

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub's **Report a vulnerability** button, on the repository's
**Security** tab (private vulnerability reporting). Do not open a public issue for a security problem.

Include:

- what an attacker controls, such as a crafted archive, a file name, a shared folder, or a settings file;
- the impact;
- the FileCat version (Help → About) and the package you use, such as the installer or a portable ZIP;
- your operating system and its version (Windows, macOS or Ubuntu);
- the smallest reproduction you can share.

Please do not include personal files. A diagnostics bundle (Tools → Export diagnostics) never contains file contents
or credentials. Unless diagnostic mode is on, the paths in its log and crash reports, error messages included, are
replaced with hashes, but a message can still name a file, folder or server, for example by a bare file name or a
server's name. With diagnostic mode on, paths appear as written. Its `settings.json` holds your FileCat settings in
plain text, including saved server hosts and user names, key-file and start paths, tool paths and Find's ignored
folders. Check the ZIP before you attach it, and remove what you do not want to share.

Expected response:

- an acknowledgement within 7 days;
- an assessment within 14 days;
- for confirmed issues, a fix in a security release (see [servicing](docs/SERVICING.md)), then public disclosure once
  users can update. Reporters are credited unless they prefer otherwise.

## Supported versions

Security fixes go into the latest release. There is no updater, so each fix ships as a new release. The automatic
update check, on by default for new profiles (Settings → Privacy), or Help → Check for updates tells you when one is
available. Neither downloads or installs anything.

## Administrator windows from portable copies

On Windows, any FileCat copy can reopen itself as administrator after Windows asks for approval (Tools → Open FileCat
as administrator, and the administrator reviews that offer it). Every administrator window starts through
`FileCat.AdminLauncher.exe` beside `FileCat.exe`, which Windows' prompt names "FileCat administrator window". The
launcher removes the .NET runtime's environment settings (profilers, startup hooks, host and diagnostics settings),
which Windows would otherwise carry over from your account, then starts the `FileCat.exe` in its own folder. A copy
without the launcher, or whose `FileCat.exe` was renamed, opens no administrator window. FileCat started elevated in
another way, such as with Explorer's Run as administrator, does not pass through the launcher and gets your account's
environment, as any elevated program does.

An administrator window keeps out of the state your ordinary account can change: it starts with the default settings
and saves none, keeps no log or crash report, neither reads nor acts on your history, journals or edit sessions, and
opens no terminal or file association. FileCat running as root on Linux or macOS does the same.

The administrator window runs the program files of the copy that opened it. By default the installer puts them under
Program Files, which only administrators can change. A portable or framework-dependent copy runs from its own folder,
which your ordinary account, and any program running as you, can usually change. If you open administrator windows
from such a copy, keep it in a folder only administrators can change, such as one under Program Files, or use the
installer. The same holds for an installed copy you put outside Program Files. This is a known limitation.

## In scope

These are examples, not an exhaustive list:

- Code execution or argument injection through file names, paths, or tool placeholders (plan §14.2, TV-17).
- Archive handling that escapes the destination, bypasses resource limits, or loses Mark-of-the-Web without saying so.
- Silent data loss, silent permanent deletion, or undo that touches items it did not create.
- Privacy defects: paths or contents in logs outside diagnostic mode, or network requests while the update check is off.

## Out of scope

- Behavior of external programs that the user configured, such as tools, editors, or associations.
- Attacks that need administrator rights, or physical access to an unlocked session.
