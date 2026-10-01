# E-V24-G1 — a repository's configuration sending Git off this computer (I69)

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
197 total, 0 failed, 8 skipped, 101.3 s (host, Debug).

Files (`artifacts/release-evidence/v24/`): `v24-git-share-capture.txt`
`81486c7127fb5a60b3dde517ba70887f4fb2179e7f688f701a6d2d60eed6d58b`; `v24-a-git.pcap`
`085d67fa94891ddec6f3ca10e040889aeab8aa8c294e15e29442f9a8d7cb9ab4`; `v24-b-filecat.pcap`
`704e5e5b3234433c01fcfd1b20a306e77e985038120492dc53965c3edd38a4ea`; `v24-c-git.pcap`
`dd51cdbd64fec99441f6e86ebeddb72fa72a13c9b57f5407b563a5e7b10d98e2`.

## Still open in V24

This pass covered the Git route's configuration. The charter's other seed groups — `.lnk`/`.url`/`desktop.ini` and
icon resources, gpg and sidecars, the terminal, SSH and association routes with a recording executable, and malformed
discovery — remain, as does the capture-and-trace form of the whole case on a final candidate. On Linux and macOS a
value naming a path under an automounter is still only a path to FileCat (`IsLocalPath` is a Windows decision); that is
the same limitation the `gitdir:`/`commondir` checks have carried since `2f35a6b`.
