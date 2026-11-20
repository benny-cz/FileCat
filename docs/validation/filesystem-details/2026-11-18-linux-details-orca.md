# File-system details with Orca on Ubuntu

Orca 46.1 ran with the native FileCat Release build from `92d52654` on the Ubuntu 24.04 x64 VM. The fixture used its own Xvfb display, D-Bus accessibility session, FileCat profile and XTEST keyboard events. It read Orca's actual speech output. The harness assigned top-level X11 focus when opening and closing the test windows, and left row and control focus to FileCat.

Orca spoke the selected `alpha-details.txt` row. Ctrl+Alt+Enter opened **File-system details**. Tab reached **Read full record**, and activation announced the full observation. Tab reached **Copy all** and the resulting clipboard text contained 2,474 UTF-8 bytes, including the selected path and the metadata-only omission notice. The clipboard contained neither the file payload nor the real xattr value used as canaries. **Show summary** announced the summary and exposed **Show full record** again. Escape closed the report, and Orca spoke the original selected row after the top-level focus handoff. The owned source file and xattr both retained their bytes.

The initial live-desktop attempt reached FileCat's native starting row, then the locked desktop took the first physical key. The passing receipt is scoped to the isolated X11 journey. Live-desktop physical navigation, the wider size/scale/picture matrix and the other native screen-reader platforms remain separate N-UX evidence. The VM report is `~/FileCatValidation/details-92d52654.zR6HolZy/orca-filesystem-details-xvfb-final.json`. The tested `FileCat.dll` SHA-256 is `ce4010daeea19c99b8f0ae66abb81c7ce0c3ed21a6eb841940578519f5be9c9c`.

## Properties shortcut

The `--properties` run used Alt+Enter on the focused file in the dedicated Xvfb session. It opened FileCat's canonical File-system details window at 520 × 420 pixels. Orca 46.1 spoke the full observation, the summary and the original selected row after Escape. **Copy all** included the selected path and the metadata-only notice. The file and real xattr retained their bytes.

The prepared Ubuntu app had `FileCat.dll` SHA-256 `4EF42AF7C6FFC93C8C135EEBD14B3A5D45DCEB27A3CA7BB534C73CDAAF5DCCB2`. The receipt `linux-details-properties-893e4d0f-r1.json` has SHA-256 `5B9FC93AFD9BA41E883F763752D48B82BE1CEA1198B2A084CC34E7292193F64C`.

## Standalone Find results

On 20 November, Ubuntu x64 kernel `7.0.0-43-generic` and Orca 46.1 exercised `--find-results --report-size 520 420` using the build from `85cfc78c3` on ext4. Alt+F7 opened standalone Find. AT-SPI filled the search criteria and focused the results list. Orca spoke the matching file after keyboard navigation and announced the Full, Summary and cached Full observations. The metadata clipboard contained 3,009 UTF-8 bytes with the original path and the omission notice. It excluded the file and xattr payload canaries and the unselected entry.

Escape closed the report and the fixture verified the same native Find window ID. The Xvfb harness then set top-level focus to that window and Orca spoke the original file again. The file and xattr retained their bytes. The focus result is scoped to the isolated X11 session. The receipt `~/FileCatValidation/filecat-exact-main-85cfc78c-ext4-r1/receipts/orca-find-details-ext4-r1.json` has SHA-256 `DFAED844B0204413D40FB804F8E6C26B67422BB9377CF03139924A68B9845DE4`. The tested `FileCat.dll` has SHA-256 `7F722F683D84B32DEEE2990CCE65D811A3B9A52214F30DC2DA25AB62941BAD7D`.
