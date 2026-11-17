# Recursive EFS through held parents

The internal EFS job adapter now handles the reviewed existing-descendants scope. It plans overlapping roots from held ancestry. Enumeration borrows the live ancestor guards and opens each child relative to its held parent. The child must match the cursor identity, kind and reparse state. A child owns only its new handle. Already-satisfied directories establish their guards before advancing the cursor. Existing depth, lifetime and evidence limits apply. Unsupported children remain individual recorded outcomes.

The Windows 11 x64 VM ran a recursive encrypt and decrypt round trip with reversed overlapping root selections. An already-encrypted directory needed no native call. Setup was requested when its first clear descendant was reached. The job changed each eligible object once, kept a read-only file clear and skipped a directory link without following it. Decryption removed the observed EFS flags and preserved the synthetic main and named data. The combined native suite passed 68 cases with zero failures and four fixture-specific skips. See `.tmp/filecat-efs-recursive-jobs-tests.xml`.

The same job protocol exercised a pending recovery decision. Canceling the decision stopped before any intent. In a second test, the isolated VM account selected a new actual EFS default after setup. The first file became encrypted under that key. The job stopped, retained the exact reviewed and observed keys in its on-disk journal, and left the second file clear. See `.tmp/filecat-efs-job-stop-tests.xml` and `.tmp/filecat-efs-job-stop-rotation-tests.xml`.

These qualify the internal job composition under the approved pathname boundary. The application decision dialog and remaining native, output and release work are still open.
