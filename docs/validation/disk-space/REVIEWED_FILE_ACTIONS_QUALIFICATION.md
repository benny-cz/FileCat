# Reviewed file actions qualification

## NTFS move

The Windows VM tested a same-volume, no-replace rename candidate. It held the exact source file with delete access and used held source and destination ancestors. The primary handle denied write and delete sharing. The held named-stream handle denied write sharing and shared delete access with the proposed mover.

Run r1415 passed 64 of 65 checks. The move qualification check demonstrated that another handle could remove an existing named stream while both review handles were live. Ordinary writes to the primary and existing named stream had already failed. No source rename was issued in that check. This result agrees with Microsoft's documentation that sharing modes are maintained per stream and that a delete open on the primary file checks sharing across its streams. [NTFS file streams](https://learn.microsoft.com/en-us/windows/win32/fileio/file-streams), [Windows file sharing checks](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fsa/82b364ce-6d7b-422f-8d88-4db32eea809a)

FileCat keeps reviewed Move unavailable. The failed candidate was removed from the product. The remaining fixture exercises the Windows handle behavior directly and records no source mutation authority. The gate requires a native proof at the mutation boundary for the complete approved file version and exact source name.

## Interrupted copy

The Operations recovery report shows the recorded source and destination for each attempted file and identifies publication as committed, skipped, failed, canceled before publication, or unconfirmed. An intent with an uncertain outcome remains open for review. A failure without a recorded outcome also retains its intent. Acknowledging the report closes recovery tracking. The files stay in place and a new job requires a fresh review.

## Regression results

Run r1416 passed all 66 renderer and filesystem checks on the Windows VM, Mac and Ubuntu VM. The Windows fixture confirmed that both stream removal and creation are possible during the proposed move review. The copy recovery check ran on all three platforms and verified that acknowledging the report kept a committed copy, an existing namesake, an unconfirmed output and all four originals. Post-run inventories showed zero FileCat or .NET processes on all three test machines.

This covers the tested move refusal and copy recovery. A physical Windows desktop run, the remaining capabilities and the final independent review are pending.
