# EFS protection of reviewed replacement targets

Copy, move, rename and streamed extraction withhold Replace when the existing destination is observed as EFS-encrypted. A default Replace policy cannot bypass that check. The shared Windows replacement checks current attributes on the held destination before and after retiring it to the recovery name. If it observes EFS before publication, it keeps or restores that exact object. If it observes EFS after publication, it retains the original at the recovery name and reports it. These are observations of held objects at the indicated points.

The Windows 11 x64 NTFS VM passed 20 tests as its ordinary user, including copy and move with a default Replace policy. In two additional native cases, the fixture encrypted the destination after review or after retirement to the recovery name. Both cases retained the exact old file ID and bytes and left the incoming file untouched. The remaining tests covered ordinary bound replacement, replacement through copy and move jobs, and streamed extraction. Results are in `.tmp/filecat-efs-replacement-tests.xml`.

This verifies the replacement target checks exercised by those routes. E-OUT1 still requires qualification for copies and moves from EFS inputs. E-OUT12 still requires contributor and stage admission before derived extraction.
