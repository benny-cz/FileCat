# E-V24-D1 — damaged answers from the network, what a failed Shell request means, and what runs while a folder is shown (B05, I70)

Plan: V24 ("For discovery/config test malformed WS-Discovery/mDNS …"), V23 B05 (remote server and discovery → local
work). Issue: [I70](../FILECAT_1_0_RELEASE_ISSUES.md#i70--a-shell-picture-that-got-no-answer-was-remembered-as-the-file-having-none).

## E-V24-D1-C1 — the discovery parsers under damage

Looking at the network reads whatever anything on it chose to answer, before the user has asked for anything in
particular. Three parsers do that reading, and `DiscoveryFuzzTests.Damaged_answers_are_ignored_never_crashed_on`
damages each one's answer the way the archive and inspector campaigns damage a file — bytes changed, more often near
the start, sometimes cut short — with the damage depending only on the kind and the round's number, so a round that
fails anywhere replays everywhere:

| Kind | What it is | Rounds | Result |
|---|---|---|---|
| `probe-matches` | what a device answers a WS-Discovery probe with (XML over UDP) | 0–999,999 | **passed**; 7,317 endpoints or addresses still read out of damaged answers; most allocated by one round **19 KB** |
| `metadata` | what it answers a WS-Transfer Get with, naming the computer (XML over HTTP) | 0–999,999 | **passed**; 3,012 computers read; most **15 KB** (round 574237) |
| `mdns` | an mDNS answer naming an SMB service (binary DNS, with name pointers) | 0–999,999 | **passed**; 75,410 hosts read; most **18 KB** (round 375586) |

Host, build `aaee133` (Debug), 205 s for all three, nothing over 64 MiB (the campaign's budget) and nothing over
30 seconds in a round. The earlier targeted tests stay as they were: an answer cut at every single length, a name
pointer that loops back on itself, "not xml", and a DTD with an entity in it.

What this does **not** cover: the answers are read, but what FileCat then does with a *well-formed* answer from a
hostile device is the B05 review's subject (E-DPI), where it already holds — a discovered name is accepted only as a
host name, and a device is asked only at the address that answered.

## E-V24-D1-I1 — I70, found by this campaign's own CI run

The commit that added the recording-program test went red on the Windows ARM64 lane, in a test of quick view's Shell
thumbnail, with a message that named the case itself: *"The helper made a thumbnail, but quick view did not show it:
quick view's request was answered with no picture; helpers started: 2."* Two helpers had been started, so the first
had died; and the request was recorded as answered with no picture.

Reading the code found why. The restricted helper is deliberately fragile — it is where the Shell's own handlers run,
so a handler that crashes takes the helper with it, and a loaded machine can miss its twenty-second start.
`ShellHostClient` returned `null` for every one of those, exactly as it returns `null` for "this file has no picture",
and `ShellPreviews` cached that `null` under a key of the file, its time and the size. Quick view asks once per item
shown, so the picture stayed missing for the rest of the session; icons fell back to the type icon the same way.

**Fixed `3f647bd`:** the client now reports which of the three happened — *answered* (a picture, or a definite none),
*refused* (this item hung or crashed a helper before, or pictures are off for this session), or *failed* (no helper
answered at all). Only an answer or a refusal is remembered. A failure is tried again the next time that picture is
wanted, at most three times per file, so a handler that brings the helper down on every try is still given up on and
nothing is asked for ever.

**Verification:** `ShellHostTests.A_request_that_got_no_answer_is_not_remembered_as_the_file_having_no_picture`, which
drives the three answers through a stand-in for the helper: two failures leave nothing remembered and are asked
afresh, the answer that follows is remembered and ends the asking, and a file that fails every time is given up on
after three tries. Run against the unfixed caching as well, where it fails on its first assertion ("a failure was
remembered as an answer"). `FileCat.Platform.Windows.Tests` whole suite afterwards: 128 total, 0 failed, 24 skipped.

**The icons as well.** `NativeIconSource` keeps a plan of its own per item, so the same thing happened a second time:
a shortcut or a customized folder whose named icon the helper never answered for stayed a plain type icon for the
session. The distinction now reaches it too (`ShellPreviews.GetWithAnswerAsync`), and a plan made from an unanswered
request is dropped instead of kept, so drawing the row asks again. That also covers a case with no crash in it at all:
while FileCat recovers deleted files it pauses Shell pictures (I09, so that the Shell writes nothing to the disk being
read), and **every icon asked for during a scan was being remembered as "none" for the rest of the session**. Those
are now asked again once the scan is over, which the same test checks (`Paused` gives `Failed`, not an answer).

**The single showing.** Not remembering a failure did not yet make one showing succeed: quick view asks once for the
item on screen. The ARM64 failure's own diagnostics say which case it was — the test's direct request right afterwards
returned a thumbnail, so the item had not been poisoned by a hang or a crash; the first helper had simply not started
in time. `ShellPreviews.GetForDisplayAsync` (`c7a02e9`) asks once more when no helper answered, which starts a fresh
one. A refusal or a real "none" stays final, two unanswered tries end that showing, and while pictures are paused
nothing is asked at all. `ShellHostTests.What_is_on_screen_is_asked_for_once_more_when_no_helper_answered` covers the
four cases, and fails with the retry taken out; both quick view picture tests pass on the host.

The ARM64 lane's other red run that day (36871560455) was unrelated: `actions/setup-dotnet` crashed while installing
the SDK.

## E-V24-D1-B1 — what runs while a folder built to tempt FileCat is shown

The other half of I16's gate, beside the network evidence in E-V24-G1: V24's pass criterion says ordinary browsing
"runs no input-selected executable/script/hook … only documented policy-authorized children execute".

`BrowsingProcessTests.Browsing_a_hostile_folder_reads_only_what_it_may` (gated on `FILECAT_V24_BROWSE`) builds a folder
in which three different things name the same program — `%SystemRoot%\system32\notepad.exe`, standing in for anything
an attacker would put there:

- a repository whose `.git/config` declares a `[filter "evil"]` whose `clean` is that program, with a `.gitattributes`
  applying it to every file (a filter is what `git status` runs while it compares);
- an Internet shortcut and a read-only folder whose `desktop.ini` name it as their icon;
- beside them an ordinary repository, a copy of the program itself, and a file whose name means something to a shell.

The folder is listed, every row's icon asked for repeatedly over six seconds, and the badges read.
`artifacts/vm/win-browse-trace.ps1` records every process started meanwhile (`Win32_ProcessStartTrace`, an elevated
subscription), keeps only what grew out of the test process, and splits the run at the moment the test says its
fixtures are built — by each process's own `TIME_CREATED`, since the events are delivered in bursts.

| | Processes |
|---|---|
| While the test built its fixtures with Git | 16 |
| **While the folder was shown** | **8**: two `git.exe` runs with their child `git.exe` and `conhost.exe`, and one `FileCat.ShellHost.exe` |
| `notepad.exe`, which all three fixtures named | **0** |

The two Git runs are the ordinary repository's; the one that names a program is not read at all, and its badge is
absent while the ordinary one's is `Clean` — which is what the test asserts from inside. The single Shell helper is the
documented child that reads icons. Host, build `3f647bd`; `v24-browse-processes.txt`
`4a3eb05157faf50f9c88efc92e15dec42d375249d274aac02c9df66b6a50ca63`.

Not covered here: the same case on a candidate's installed files. The file-access half (which handler touched what) is
E-V24-D1-F1 below.

## E-V24-D1-F1 — which files browsing that folder opens

The file half of I16's gate: the same test under the kernel's own file events — an elevated ETW session of
`Microsoft-Windows-Kernel-File`, event 12 ("Create", logged as an open starts, so a failed attempt counts too) — with
the process tree from `Win32_ProcessStartTrace`, counting only the opens between the test's two markers, fixtures built
and browsing over, since building the fixtures with Git and removing them afterwards open everything
(`artifacts/vm/win-browse-filetrace.ps1`; the test writes the second marker since `f1b48de`). No event was lost.

| Process | What it opened while the folder was shown |
|---|---|
| FileCat (the test process) | the folder and its data folder; its build, .NET and Windows; `git.exe` looked for in each folder on PATH (finding Git); the parent folders up to `C:\.git` (finding the repository a folder is in); of the repository that names a program only `.git`, `.git\commondir` and `.git\config` — the reading that decides it is not to be read; `notepad.exe` six times, for its time (part of the icon cache's key, read once the helper's policy has accepted the path); the Shell's icon cache; its user-local scratch and hex-recovery folders (user-local by design, also for a portable copy) |
| Git (`git.exe` and its `conhost.exe`) | the ordinary repository, Git's installation and the user's global Git configuration — **nothing of the repository that names a program** |
| The Shell helper (`FileCat.ShellHost.exe`) | `notepad.exe` three times (its icon, extracted); three of the fixtures' files; .NET; the Shell's caches; the user's known folders and their `desktop.ini`, on C: and D: (the Shell resolving known folders) |
| Any of them | **no network path** (no `\Device\Mup\…`), and `notepad.exe` never started (E-V24-D1-B1) |

**Found and changed (I73, Low, `f1b48de`):** FileCat's own process also tried to open `C:\file.url`, which does not
exist. Its type-icon lookup asked the Shell about a bare "file.url", and the Shell's handler for .url tried to open it
at the system drive's root, although told not to touch the file. Standard users can make only folders there (`icacls`:
Authenticated Users have AD on `C:\` itself), and nothing was there; but a placeholder should name no place at all. It is
now a fully qualified name in a folder below System32 that does not exist and that only an administrator could make.
`TypeIconTests` checks that name, and that the Shell answers it exactly as it answered the bare one: the same icon place
and type name for ten types, a folder among them. Traced again with the change and without it: the attempt is there
without it and gone with it, and nothing else differs (1,009 and 1,011 opens; the two more are the probe's own name).

Reports: `v24-browse-files-before.txt` `f5d0aac3b3e8746188fdc6040c0f296b255fbe8e39d56b86764c273ee0adc497` (the change
taken out) and `v24-browse-files-after.txt` `ca3008fa8be46b02ff3c321c151d32285ee543213952bfcae9f5b28a53ab25e8`
(`f1b48de`); host, Windows 11 26220, Debug test builds. Platform suite 139, app suite 203, 0 failed.
