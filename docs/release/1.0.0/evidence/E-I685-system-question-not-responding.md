# E-I685 — While the system asked the person inside a file-system call, FileCat could say the drive was not responding

**Found:** 2026-11-26, by the release session, in macOS's own log of the owner's walkthrough on `v1.0.0-rc.8` (Mac steps M4 and M6), while fixing [I684](E-I684-authorization-wait-not-responding.md). **Severity:** Low. It is I684's misleading notice: it clears at the answer, and nothing is read or lost. **Disposition:** fixed for rc.9, by the owner's decision.

## What happened

The first time an app reads a protected folder or volume, macOS asks the person whether it may (TCC, Files and Folders). The file-system call that triggered the question waits for the answer. In the owner's walkthrough on rc.8, `tccd` logged:
- **M4, Documents:** asked at 17:57:43.131, allowed at 17:57:52.045, after 8.9 s. FileCat's next requests followed within 4 ms of the answer, so the listing had waited inside its call.
- **M5, Desktop:** asked at 17:58:02.820, refused at 17:58:08.758, after 5.9 s.
- **M6, the removable volume:** asked at 18:01:57.897, allowed at 18:02:08.008, after 10.1 s.

The listing runs on its drive's lane of the I/O scheduler, which marks a device not responding after 8 s (I684's rule). So in M4 and M6 FileCat will have said "… is not responding" for about one and two seconds, then that the drive responds again. The owner did not report it, and no capture shows it: this is inferred from the log and the code.

Windows asks for a smart card's PIN the same way when an EFS file is opened with a key on the card: the open waits for the PIN.

## Why it is apart from I684

I684's waits are questions FileCat asks or starts itself: an authorization dialog, a password, a host key. Here the system asks inside an ordinary file-system call, which FileCat cannot tell from a drive that stopped answering. A fix therefore has to stop measuring some reads that may not wait for anyone.

## The owner's decision

The session offered three options:
- the known issue (recommended);
- a fix in rc.9, at the cost that a drive that really hangs on such a first access shows no notice;
- a reworded notice.

The owner first chose the known issue, then wrote "sorry, I changed my mind. deliver it in rc.9".

## The fix

[PR #75](https://github.com/benny-cz/FileCat/pull/75), with I684's fix:
- **macOS:** `SystemPrompts.ReadingFolder` classifies a folder as one of macOS's protected places:
  - Desktop, Documents, Downloads, iCloud Drive, a cloud provider's folder, or other apps' data;
  - a network volume (no `MNT_LOCAL`), or a removable one (`MNT_REMOVABLE`, or any local volume under `/Volumes`). On the Mac, an attached disk image's volume carried `MNT_REMOVABLE`, and `statfs` on Documents did not ask anything.
- **While that kind is unanswered,** the read waits for the person until its first read returns, allowed or refused. FileCat keeps the answered kinds in `macos-privacy-answers.txt` in its local folder, so a later read of an answered kind is measured as usual. Panel listings, the analysis's folder reads and folder contents (tree compare) are covered.
- **Windows:** opening a file's content waits for the person when the file is EFS-encrypted and this user has an EFS certificate whose key is outside the computer's own software providers. This is read from the certificate store without opening any key.
- **The cost, accepted by the owner:** a drive that hangs during such a first, still unanswered access shows no "not responding" notice. Resetting macOS's answers (`tccutil reset`) brings a question FileCat no longer expects, and the notice can show again.
