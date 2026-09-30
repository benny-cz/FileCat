# E-I23 — a device found on the network lost its name when its answer came late in the search

Issue: [I23](../FILECAT_1_0_RELEASE_ISSUES.md#i23--network-discovery-listed-a-device-by-its-address-when-its-name-arrived-late).

## E-I23-D1 — discovery

E-X01 run U2 (lent Ubuntu 22.04 VM, `5c54181`, Core TRX
`856873aeb663107dd0e7081a82e4357104396b83f680128c2624d1b1d6661f8d`):
`NetworkDiscoveryTests.Devices_that_answer_are_found_named_and_listed_once` failed — the WS-Discovery device was listed
as "127.0.0.1" instead of "TESTBOX". The same test passed on that VM at `be6ca25` (run U1), on every CI lane, on the
Mac and on the Windows VM. During U2 the Windows VM ran the Synchronize stress (E-X01 W3) and the host built and ran
tests: the host's CPUs were shared by all three.

## E-I23-M1 — mechanism (source)

`NetworkDiscovery.ProbeWsdAsync` runs for the search window (`DiscoveryWait`, 3 s in the app on Windows and Linux; 2 s in the test) and, for each
device that answers, starts `NameAsync`: a WS-Transfer Get of the device's metadata over HTTP, which carries its
computer name and workgroup. `NameAsync` received the **search window's** cancellation token, so a Get still under way
when the window closed was canceled, and the device was listed by the address it answered from. A device that answers
late in the window (WS-Discovery lets a device wait up to half a second before answering) or whose metadata answer is
slow therefore loses its name; on a busy machine the test's own local answers are late enough.

## E-I23-R1 — runs

- At idle (Ubuntu VM load average 0.52): the test passed 20 of 20 (`ubu-disc-log.txt`
  `6cb6392d844426f1e4c8d6ff6513a2a51b1863ca70e470778185e4a7285f2f16`).
- Under load inside the guest (16 busy threads on 8 vCPUs, load average 13.5; script `ubu-disc-load.sh`, log
  `ubu-disc-load-log.txt` `99597afb2e17bb085ea1108aac3bce695dc684642330020e8a98c346fd1e3eae`): 20 of 20 passed. Load
  inside the VM alone does not delay the local answers enough; the failing run had the host itself contended (the whole
  VM paused at times). A deterministic reproduction needs a device whose metadata answer comes after the window: it is
  part of the remediation's tests.

## Remediation and verification

In progress; recorded here when committed.
