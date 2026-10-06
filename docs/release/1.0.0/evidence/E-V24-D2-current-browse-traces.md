# E-V24-D2 — current browsing process, file and share-contact controls

Classification: verified preliminary native component observations, not drawn UI
or candidate qualification. Plan V23 B03/B10 and V24; I16 stays open for the wider
catalog. Actual source `313d40be9f84babe7bab704ce477f46bf6ca4113`; 885 raw Git blobs
verify before a fresh self-contained win-x64 App-test publication. The producer
manifest SHA-256 is `23e7b9679280e168ee9bc9a77d706834c7f2bb5fefad2192b0bab4a005503d2f`.
All 354 payload byte/size pins verify before and after each Windows 26300 guest run.
The VMware Admin guest token is elevated; host tool token is unelevated. No native
desktop is observed: the existing headless cases exercise actual production
services, native icon extraction and the restricted Shell process.

## B1 — process starts during hostile-folder browsing

The existing BrowsingProcessTests case passes, with ordinary Git Clean and the
filter-bearing repository None. An independent Kernel-Process ETW session records
66 events: 32 starts and 32 stops, with zero reported lost events/buffers or skipped
events. Before/after cmd controls exit 17/19 and have matching start/stop identities.
All 26 test-process descendants have matching sequence-number/PID/creation/exit
lifetimes; PID alone is not the ancestry key. The exact 8.092-second browsing phase
starts four processes: one FileCat.ShellHost, two Git binaries and their console
host. No notepad, copied fixture program or calc launch is observed. The Shell
process has low mandatory label S-1-16-4096; its exit code 1 remains recorded.
Source RestrictedProcess disposal terminates its job with code 1, consistent with
this cleanup observation; the trace does not itself identify that call site.

Two logger/console starts at session shutdown and two stops at startup lack their
other endpoint; all are outside the verified test subtree and are retained.
tracerpt renders SystemTime with +01:59 although the guest's markers use +02:00.
Raw rendering is preserved; provider UTC CreateTime/ExitTime and the independently
recorded process StartTime define the phase. No timestamps or names are rewritten.
Owned payload processes are absent and test temp is empty. Independent proof:
`e141442ab361bd03f22f4a5ba82e55569e974f58ba8d3790fc8a9f9f3a2d0dc1`.

## F1 — file-open attempts during the same component scenario

A separate fresh run passes the actual case. Kernel-File Create events and process
events are captured together, with successful owned-file read controls before and
after. The raw trace contains 5,585 events, including 5,519 file opens and 64 process
events; lost events/buffers and skipped events are reported as zero. Native
Get-WinEvent UTC matches ToXml UTC, provider process creation and the recorded test
StartTime. Its export contains 5,584 rows: the EventTrace header is omitted, and an
undecodable partition metadata row remains explicit. All file/process events decode
without ProcessingErrorData and match the raw provider counts; this metadata limit
is not relabeled as a fully identical decoder inventory.

The verified test lifetimes and ready/done markers select 594 open attempts, across
249 exact raw paths: App test 244, Shell helper 179, Git 160 and console host 11.
No network file path is observed. All five opens of the hostile repository are by
the App parent, inspecting its .git/config/commondir; Git does not enter it. Eight
control-file opens match the two controller/PID/path/read-byte positives. Every
raw path and open is retained, including local user-profile, Git configuration,
Shell cache and runtime locations. Create events include failed open attempts;
they do not qualify all reads/writes, access rights or protected-file mutation.
Owned process/temp and all payload pins verify. Independent proof:
`6965c6a356e17e546b67ed6863959900b2dda42979e8e361f525425e2c38a5f8`.

The first capture fails before any FileCat starts: duplicate -p options are refused.
Its fifteen retained pins remain. The fresh corrected driver uses a multi-provider
file, as [Microsoft documents](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/logman-create-trace).
No failing attempt is replaced or credited as a product pass.

## N1 — named share icons and shortcut targets

The Windows case passes with actual local .url/.lnk/custom-folder named icons and
the local target program icon arriving; the three corresponding share icons and
share target retain type icons. It points only at the owned Ubuntu 26.04.1 guest's
literal IPv4 address 192.168.58.129. A bounded root tcpdump recorder and owned port
445 listener capture the sender guest 192.168.58.128. The receiver only acknowledges
known TCP control bytes; it provides no SMB negotiation or authentication challenge.
Exact VMware guest-read SSH host-key verification precedes the authorized root
launch. Before/after controls carry unique tokens and receive the exact ACK.

The complete pcap has 118 untruncated packets. Native tcpdump decoding agrees with
the independent save-file/Ethernet/IPv4/TCP parser and all 118 capture/filter counts;
kernel-dropped count is reported as zero. Twenty packets to/from the named endpoint
are exactly the two known control flows, including SYNs and exact sent/ACK payloads.
No additional endpoint packet or connection is observed across the full capture.
Receiver and sender clocks differ by approximately 0.86 seconds; the verifier uses
complete-capture control four-tuples and bytes, not cross-machine phase subtraction.
All timestamps remain raw. The other 98 packets are retained; their destinations
cannot be attributed to FileCat from this capture alone, and no all-network or
whole-machine zero-contact claim is made. Counter/format semantics follow the
[tcpdump source manual](https://github.com/the-tcpdump-group/tcpdump/blob/master/tcpdump.1.in)
and [libpcap save-file manual](https://github.com/the-tcpdump-group/libpcap/blob/master/pcap-savefile.manfile.in).

Both known controls, actual test exit, 34 Windows/six Ubuntu retained pins and all
354 payload pins verify. The receiver closes and exact recorder/controller processes
are independently absent; no firewall or system service is changed. Independent
proof: `2979f329505e0864dcf54095c3249b65635c52eaaa9007c80126f4d2bde5ccc9`.

Private evidence lives in the authorized second workspace's
FileCatReleaseEvidence/browse-traces-20261006, under windows-process-v1,
windows-files-v1/v2, windows-icon-network-v1 and network-capture-v1. The original
producer's incorrect source-file name is retained as an observer failure after its
successful publish; fresh read-only sealing verifies the actual source-file path.
These finite fixtures do not cover indirect/reparse/environment/global-config
variants, other destinations, all protected-file effects, actual user-driven
tools/terminal/association routes, containment generally or final installed bytes.
I16/V23/V24 remain open. Owner interactions remain queued until 08:40 CEST; overall
NO-GO and the explicit human stable-GO requirement remain.
