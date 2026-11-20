# Case-distinct native archive sources

8 November 2026. Fix implemented and live-validated on all three platforms. No write profile is enabled.

The native job planner and selection forest previously used case-insensitive Windows path dictionaries before
acquiring native evidence. This merged two selected inputs on case-sensitive NTFS. The real Windows VM reproduction
uses `Case` and `case` with different three-byte payloads and different native file identities: the committed
`c5d62947` Core assembly nevertheless planned only one source. This could archive the first file's bytes under
both requested output names. The reproduction is sealed at the task's `native-source-case-reproduction-v1`,
including its exact Core SHA-256, probe/source bytes, native observations and the first fixture-access failure.
The successful fixture lives in the standard user's profile; pinning its ancestors does not require access to
the administrator's home directory.

Both path maps now coalesce only identical spellings. The native capture still determines object identity and
rejects unsupported aliases; a string comparer cannot infer whether different Windows spellings are aliases.
This also preserves distinct selected parent directories named `Root` and `root`.

Two core cases cover distinct leaves and parent paths. Twenty actual Windows application-worker cases cover both
layouts across all six container formats and every RAR/7z password mode. Each checks native identities, parent
expectations, exact output payloads, fresh assessment and unchanged inputs. Their independent exported matrix is
separate from the existing job/workflow vectors: six 7z, six RAR5 and eight TAR-family archives, all with exact
two-file payload and metadata checks. The [verified checkpoint](native-source-case-checkpoint.json) records:

| Platform | Affected archive cases passed | Skipped | Focused cases passed |
| --- | ---: | ---: | ---: |
| macOS ARM64 | 3,811 | 55 | 116 |
| Ubuntu x64 | 3,824 | 42 | 116 |
| Windows x64, standard user | 3,595 | 271 | 116 |

Windows also passes all twenty new native source-case preflights, the complete 197-case native selection and
ten application-entry cases. All inherited catalog/worker/native/workflow/job readers pass, followed by exact
independent checks of all twenty source-case archives. The changed C# files match compiler checksums: four on
Unix and five on Windows. Final censuses find no owned FileCat/test instances. The Mac wake helper remains active
and Ubuntu's owned private keyring daemon is stopped.

The combined driver retains the preceding complete core result, runs the entire affected archive closure and
requires its exact test inventory plus the two new core cases. Every previously passing affected case must still
pass. It also runs focused job/capture checks, Windows native/application-entry selections and all inherited
catalog/worker/workflow/job readers. The unrelated unchanged core code retains its preceding full-suite evidence;
final release qualification still requires the complete inherited gates.

The first inventory check used the new method's name without its `_separate` suffix. Its exact diff showed no
missing old cases and precisely the two expected additions. The corrected validation helper continued from the
passed C# results; original/corrected drivers and the refusal are retained. Windows' first continuation lacked
read access to the newly copied helper, so the task granted read access to its three own scripts and resumed
under the same standard account. That setup failure is retained too; neither correction repeated passed tests
or changed the application binaries.

Microsoft documents the [per-directory case-sensitive flag](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_case_sensitive_information)
and [case-sensitive Windows directories](https://learn.microsoft.com/en-us/windows/wsl/case-sensitivity). The actual
VM observation, rather than an OS-wide case-sensitivity assumption, establishes the failing input selection here.

Remaining integration work includes native cleanup/F6 and the shared UI. Before reusing the legacy ZIP move
accounting, qualify its case-sensitive source keys and root outcomes too. Preparation's conservative Windows
name comparisons also need sibling target/source case tests alongside identity-based self-inclusion checks.
This source-selection checkpoint does not claim those separate paths are complete.
