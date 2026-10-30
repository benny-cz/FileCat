# FileCat privacy policy

FileCat is a file manager that runs on your device. It does not require a FileCat account or send usage analytics to a FileCat-operated service. It stores settings, workspaces, history, job records, caches and diagnostic logs locally in the selected profile's application data (or the portable data folder). Local logs hash paths by default. If you enable diagnostic mode, selected paths can appear in the local log.

FileCat connects to network locations when you use its remote or network-browsing features. Files and credentials you choose to send to those locations are handled by the selected server and its operator. Saved connection secrets use the operating system's credential store when available, with a session-only fallback where it is not. FileCat does not put passwords in its settings file.

Automatic update checks are off by default. If you enable them in **Settings → Privacy**, FileCat checks the GitHub releases API at most once a day and sends its version in the HTTP User-Agent. **Help → Check for updates** makes the same request when you ask for it. The check only reports availability; it does not download or install an update. GitHub processes these requests under its [privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

**Tools → Export diagnostics** creates a local ZIP for you to preview and share yourself. It includes environment details, settings without stored credentials, and local logs or crash reports where available. It does not include file contents or credentials. If diagnostic mode is enabled, exported logs may include selected paths. FileCat does not upload the ZIP automatically.

You can remove FileCat's locally stored data from its profile or portable data folder. For a privacy concern, use the repository's [private vulnerability reporting](../SECURITY.md) when disclosure would expose sensitive information, or open a [GitHub issue](https://github.com/benny-cz/FileCat/issues) for a general question.
