# E-EXT04 — AppImage runtime from the repository and its corresponding-source bundle

**Engineering for EXT-04 (owner disposition 2026-10-28) and review finding R-M17, at `3dfd443f202d6a83ba7633add5e771a3785c2d99` on branch `claude/appimage-source` (not pushed). Not a legal determination, hosted CI result, frozen candidate or release GO.**

## Vendored runtime (R-M17)

The reviewed x86_64 type2-runtime was retrieved once, on 2026-10-28T23:39:34Z, from `https://api.github.com/repos/AppImage/type2-runtime/releases/assets/657374641`. The asset still existed; GitHub's metadata reported 944,632 bytes, digest `sha256:617aa77314d57c8b8e4fae9c3f0b35af7b30d5529cb69e98f64f92510ed98a46`, created 2026-10-19T20:37:12Z, and the downloaded bytes match the pin. They are now `eng/linux/appimage-runtime/runtime-x86_64`, described by `eng/linux/appimage-runtime/manifest.json` (bytes, SHA-256, origin, retrieval time, `.digest_md5` section offset 932,096 and length 16). `licenses/appimage-runtime/index.json` keeps the same `RuntimeInput` bytes and hash and adds `VendoredPath`.

`eng/package-linux.sh` no longer contacts GitHub for the runtime. It uses the vendored file, or `APPIMAGE_RUNTIME` if set, and checks either against the unchanged pin `617aa773…8a46`; a mismatch exits 2 before the AppImage is assembled. After appimagetool runs, the script zeroes the 16 `.digest_md5` bytes of the AppImage's first 944,632 bytes and requires the pinned hash again; a failing AppImage is renamed `.rejected`. No aarch64 runtime is vendored: CI builds only linux-x64, and the notice snapshot pins x86_64 bytes, so the script now refuses aarch64 at runtime selection with that reason instead of asking for `APPIMAGE_RUNTIME`.

## Corresponding-source bundle (EXT-04)

`package-linux.sh` writes `FileCat-<version>-linux-x64-appimage-runtime-source.tar.gz` with one directory, `appimage-runtime-source/`:

| File | Bytes | SHA-256 | Origin |
|---|---:|---|---|
| `sources/fuse-3.15.0.tar.xz` | 4,589,124 | `70589cfd5e1cff7ccd6ac91c86c01be340b227285c5e200baa284e401eea2ca0` | libfuse release asset, as pinned by type2-runtime's own dependency script |
| `sources/squashfuse-0.5.2.tar.gz` | 75,404 | `db0238c5981dabbd80ee09ae15387f390091668ca060a7bc38047912491443d3` | `github.com/vasi/squashfuse/archive/0.5.2.tar.gz`, same upstream pin |
| `sources/type2-runtime-fedfad51acc2860b2916a1eef32ba4a500ff31ef.tar.gz` | 31,128 | `e27ea3c837e9c660e1a9a2993b673ae93bbb53077330efba8654031e302d4e8e` | GitHub archive of the reviewed commit (runtime.c, Makefile, scripts, `patches/libfuse/mount.c.diff`) |
| `producer-packages.txt` | 2,601 | `63c38652083060da6a8f4d6b7350dba24e45f8fceab619be4c16e337365b82b6` | the 138 apk install/upgrade lines of producer job 118293253809 |
| `README` | 8,733 | `6c8d770db8222559b1d643e3180cdf9850bc30d7d7b247016be11c10f20a4bcf` | runtime identity and AppImage-prefix check, contents, producer recipe, rebuild and relink steps |
| `rebuild.sh` | 8,655 | `a56f71e2fa231c18ef2b1734fe891bfcba4b307323b38aa06fe4e2b6a00ed85c` | rebuild in `alpine:3.21@sha256:ce64758a…3507`; `--unpack-libfuse`, `LIBFUSE_DIR`, `--relink`, `--expect-shipped` |
| `SHA256SUMS` | 565 | `6b483a2a3fa2e603dec0a0a2790c86b22fbcf4981f51d52cd17b618888742f4e` | checksums of the six files above |

The source archives are vendored under `eng/linux/appimage-runtime/source-bundle/sources` (4.70 MB; 5.64 MB with the runtime), not fetched at packaging time. That stays under the 8 MB budget, keeps packaging offline, and removes the R-M17 failure mode for sources too: two of the three are archives GitHub generates on request rather than stored release files, and a deleted or regenerated download would otherwise stop packaging. `.gitattributes` marks the directory `-text`; the committed blobs hash to the values above.

The archive is GNU tar format, sorted by name, mtime 2026-10-19T20:35:22Z (the type2-runtime commit time), uid/gid 0 without names, modes normalized to 644/755, `gzip -9n`. Its bytes depend only on the vendored files, so `package-linux.sh` pins it: 4,704,832 bytes, SHA-256 `be7816ce0e129b0417758f942378bed89350e04fb1700c7bd5caa1005d72e587`. An unmatched archive is left as `.unreviewed`. Windows Git Bash (GNU tar 1.35, gzip 1.14), WSL2 Ubuntu 24.04.4 (tar 1.35, gzip 1.12) and the Ubuntu VM below (tar 1.35, gzip 1.12) produce the same bytes. The earlier pin at `08108bb8` (`8b92ecaf…530a`, 4,704,829 bytes) differs only by a README wording change and was equally identical on all three.

## Rebuild and relink

The producer facts come from the official job log, re-fetched through the GitHub API (`gh api …/actions/jobs/118293253809/logs`, 108,922 bytes, SHA-256 `b7ef27950ff358619a262a8ce0afc12960d19c2351d0feae4748e2abc1dc871b`). This retrieval's hash differs from the 2026-10-28 retention (`22794e55…`); no equality is claimed. It confirms the base digest, the Dockerfile package list and the versions in `licenses/appimage-runtime/index.json`, plus clang19 19.1.4-r0, gcc 14.2.0-r4, binutils 2.43.1-r3 and meson 1.6.1-r0.

On 2026-10-29 the current Alpine 3.21 index differed from the producer for eight of its 138 packages: musl-dev 1.2.5-r11→r12, brotli-libs, c-ares, pcre2 and four python3 packages. musl-dev 1.2.5-r11 is no longer on the mirror (HTTP 404).

`rebuild.sh` ran under rootless Podman 4.9.3 in WSL2 Ubuntu 24.04.4 (kernel 6.18.54.1). Podman was installed into that WSL distribution for this test; the Ubuntu VM has no container engine, its AppArmor policy blocks unprivileged user namespaces, and installing one needs root, which was not used there.

- The first run stopped in libfuse's install helper: `mknod /dev/fuse` is refused in a rootless container (upstream builds in rootful `docker build`). `rebuild.sh` now creates a placeholder `/dev/fuse` before the dependency step; nothing that is built depends on that node.
- Three unmodified rebuilds give **`runtime-x86_64` 944,632 bytes, SHA-256 `617aa77314d57c8b8e4fae9c3f0b35af7b30d5529cb69e98f64f92510ed98a46`: byte-identical to the shipped runtime**, and `runtime-x86_64.debug` 1,361,136 bytes (SHA-256 `193dd7ab…6cc3`), the size the producer log shows. They used the pre-final script, the bundle the VM produced at `08108bb8`, and the final bundle the VM produced at `3dfd443f` (`be7816ce…e587`, `--expect-shipped`, exit 0). `out/package-differences.txt` lists the eight newer packages, so musl-dev r12 does not change the linked bytes.
- `--unpack-libfuse` unpacked libfuse with the type2-runtime patch. A one-line change to `lib/mount.c` printing a marker to stderr rebuilt with `LIBFUSE_DIR` to SHA-256 `aae7a80280044f04937d06ccaf7ed903298dbbaa980524ffd5c31a1f053763bc`, twice identically; `--expect-shipped` then exits 3. That marker never appeared when FileCat ran, because libfuse sends the fusermount child's stderr to `/dev/null` and the runtime daemonizes before unmounting; this first oracle is retained as inconclusive, not as a product result. The accepted change appends a line to the file named by `FILECAT_RELINK_PROBE` in the patched `fusermountProg()` (retained `modified-libfuse-mount.c`); its runtime is SHA-256 `86ca1e7f9b82669758640e4135b039808ef3b68f571cc74e47ea509f07e43e60`.
- `--relink` put the first modified runtime in front of the pinned appimagetool 1.9.1 AppImage's payload; the result runs (`APPIMAGE_EXTRACT_AND_RUN=1 … --version`, exit 0). `--relink` refuses an existing output and a "runtime" with bytes after its ELF image.
- On the VM, the final bundle's `rebuild.sh --relink` wrote `FileCat-relinked-x86_64.AppImage` (SHA-256 `a01cb74a…470c`) from FileCat's final AppImage and the `86ca1e7f…` runtime: the new runtime followed by the byte-identical payload. Started normally through FUSE with `FILECAT_RELINK_PROBE` set, it printed `FileCat 1.0.0-rc.1`, exit 0, and the modified libfuse's fusermount lookup wrote four probe lines. The original AppImage with the same variable wrote none, and the relinked AppImage under `APPIMAGE_EXTRACT_AND_RUN=1` (no FUSE) also ran and wrote none. No FileCat process or `mount_FileCa` mount remained.

## Ubuntu VM packaging

The VM at `V:\Virtual Machines\Barebit\Ubuntu 64-bit` is currently **Ubuntu 24.04.5 LTS** (host `filecat-ubuntu2404`, kernel 7.0.0-38-generic), not 26.04; its SSH host key changed accordingly, so all work used `vmrun` guest operations as `benny`, without sudo. The branch reached the VM as a git bundle on top of a fresh clone of the public repository; .NET SDK 10.0.101 came from `~/FileCatValidation/dotnet-sdk`.

At `08108bb8`, `bash eng/package-linux.sh 1.0.0-rc.1 linux-x64` exited 0 in 81 seconds; its log has no GitHub API or release-asset URL. The source bundle was the then-pinned `8b92ecaf…530a`; the deb is `filecat_1.0.0-rc.1_amd64.deb` with control `Version: 1.0.0~rc.1`; the AppImage's first 944,632 bytes differ from the vendored runtime in exactly 16 bytes, 932,096–932,111, followed by `hsqs`; extraction and normal FUSE launch both print `FileCat 1.0.0-rc.1`; the eleven embedded runtime notice files equal `licenses/appimage-runtime`; the bundle unpacks and passes `sha256sum -c SHA256SUMS`. Four refusal runs exited 2 at the named check: a changed `APPIMAGE_RUNTIME`, a changed vendored runtime, a changed source archive (`SHA256SUMS`), and a consistent but unreviewed bundle (README and `SHA256SUMS` changed together, caught only by the archive pin). The first three produced no source bundle; the fourth left its unreviewed archive under the release name, hence `3dfd443f`.

At `3dfd443f` all five refusal runs exit 2, and no refused AppImage or bundle keeps its release name: the same four, with the unreviewed bundle now kept as `.unreviewed`, and an appimagetool wrapper that changes one runtime byte of the finished AppImage, refused by the prefix check and kept as `.rejected`. The following clean run exited 0 in 39 seconds, again with no GitHub URL in its log and no `.rejected`, `.unreviewed` or `~` names in `artifacts/`:

| Output (`3dfd443f`, version `1.0.0-rc.1`) | Bytes | SHA-256 |
|---|---:|---|
| `FileCat-1.0.0-rc.1-linux-x64.tar.gz` | 71,163,795 | `619fb6861bde99aaa4602a476adce8ad509c422dcad37e4ff66d7a8785375d7c` |
| `filecat_1.0.0-rc.1_amd64.deb` (control `Version: 1.0.0~rc.1`) | 57,953,314 | `548e12d6d28db1efdff2c8a4ae0e5bd847241d2c7b1943f19da1e5d5b3af858c` |
| `FileCat-1.0.0-rc.1-x86_64.AppImage` | 65,899,000 | `aa77691e833ded7e5bf1b7b5ec4c9acf06e77a91b2f36b686b105a6c223c7a4f` |
| `FileCat-1.0.0-rc.1-linux-x64-appimage-runtime-source.tar.gz` | 4,704,832 | `be7816ce0e129b0417758f942378bed89350e04fb1700c7bd5caa1005d72e587` |
| `sbom-1.0.0-rc.1-linux-x64-tar.spdx.json` | 244,845 | `71a070140b9791c8216059acb5b4cd87dacc714c8013279e05be37f98f8468bb` |
| `sbom-1.0.0-rc.1-linux-x64-deb.spdx.json` | 256,163 | `cb6b1e90a6ea6c417b5ff39741df9d38a56844d3a8028c0ee05f33a163689906` |
| `sbom-1.0.0-rc.1-linux-x64-appimage.spdx.json` | 263,385 | `e0d0b58abdb667fc7017a614dc7e8719d5ae61d601385a466c07604db8c9e313` |

The tar, deb and AppImage differ in bytes from the `08108bb8` run, which built another commit; the source bundle does not depend on the commit. The AppImage again differs from the vendored runtime in exactly bytes 932,096–932,111, the bundle README's three-command check run as written prints `617aa773…8a46`, the AppImage SBOM records `./.appimage/runtime` with that hash (the pre-digest bytes, R-M15 unchanged), extraction and FUSE launch print `FileCat 1.0.0-rc.1`, and the eleven embedded notice files equal the committed snapshot.

## Release asset contract and the Debian name

`Select-ReleaseAssets.ps1` and `Assert-DraftReleaseAssets.ps1` add `FileCat-<version>-linux-x64-appimage-runtime-source.tar.gz` after the AppImage; the Linux set is seven assets plus its manifest, and the combined draft set in `Invoke-DraftPackageChecks.ps1` grows from 23 to 24 (13 Windows, 8 Linux, 3 macOS). The three validators' fixtures and counts follow; CI's package-linux job also unpacks the bundle and checks its `SHA256SUMS`.

The Linux asset names contained `~` for every prerelease: `filecat_1.0.0~rc.1_amd64.deb`. The draft-preview job runs only for prerelease tags, and GitHub renames uploaded asset names with such characters, so the exact post-upload name check would refuse every draft. The asset file is now named with FileCat's version, `filecat_<version>_amd64.deb` (`filecat_1.0.0-rc.1_amd64.deb`; stable `filecat_1.0.0_amd64.deb` is unchanged); the package's Debian version keeps `1.0.0~rc.1` so a preview still sorts before its release. `validate-release-assets.ps1` now also requires every contract name to match `^[0-9A-Za-z][0-9A-Za-z._-]*[0-9A-Za-z]$`, which the old name fails.

At clean `3dfd443f` on Windows (PowerShell 7): `validate-release-assets.ps1` 8/8, `validate-draft-release-assets.ps1` 32/32, `validate-draft-package-set.ps1` 13/13, and `validate-dependency-notice-manifest.py` 71/71 (11 allowed, 60 refused), the last confirming that the edited THIRD-PARTY-NOTICES.md keeps its required statements. The same four pass at clean `08108bb8`. The production notice tool copies the eleven runtime notice files for the vendored runtime and refuses a copy with one changed byte, creating no output.

## Retained evidence

Raw records are in the worktree's ignored `artifacts/release-evidence/ext04-appimage-source-20261029-v1` (`V:\FileCat\worktrees\claude-appimage\…`), for the coordinating session to seal:

| Record | SHA-256 |
|---|---|
| `upstream/runtime-asset-657374641-metadata.json` | `bb44e3820f44c7b24797830ef8818e9c0c1fd3cc2f32dfe8005d064739e09b9c` |
| `upstream/producer-job-118293253809.log` | `b7ef27950ff358619a262a8ce0afc12960d19c2351d0feae4748e2abc1dc871b` |
| `clean-3dfd443f/asset-controls/release-assets-controls.json` | `cdcd1c4ed1752c63b7fbfe10d0f49a6dbf14bfda9c1b20488f9777120531b85f` |
| `clean-3dfd443f/asset-controls/draft-release-controls.json` | `28cb8743cc19ea445608242b6aad6c30afb1c04535a45ddad8eba7564e3b6388` |
| `clean-3dfd443f/asset-controls/draft-package-set-controls.json` | `ff3a600d63c37eff7e29c28c0c35bdfc958d7ce2f3ebb807c30a3efadbf35887` |
| `clean-3dfd443f/notice-controls/dependency-notice-manifest-controls.json` | `4eb5338632e52499f3f81b481d1b5b3c83cc4ec05bee209ff988e3737c483ed6` |
| `vm-ubuntu2404/vm-package-1.log` (first VM run, `08108bb8`) | `8b629c293e38462f0243c90fb36c30d5de16888f1da2ed8a179775f7d79f2542` |
| `vm-ubuntu2404/vm-negative-1.log` (four refusals, `08108bb8`) | `88bd3a486e4ad450d680738e1a95555f5dba8e265732c597e4fef259d768d757` |
| `vm-ubuntu2404/vm-final.log` (five refusals, clean run, first relink oracle, `3dfd443f`) | `def6430808377809885a5fb4a4ccb9db2498fb1cfa9fc99fe3dfb37e170b4ae6` |
| `vm-ubuntu2404/package-linux-final.log` | `e69780a54bd105f479d7c4582ea94ffa57750bac5fda24af84e65f195738eca1` |
| `vm-ubuntu2404/vm-relink-2.log` (accepted relink) | `5906a29eafae79824a0cdcc8611bec30991be0de7bb01980b7f3b09981ce1d6c` |
| `vm-ubuntu2404/probe-relinked.txt` | `6be353dac754bfc24257a5515b79ceda88de2f1a0945e20746bb6b6a518f3ab2` |
| `wsl-podman/rebuild-1/rebuild.log` (first identical rebuild) | `f0111ace0ffcbca0cc4220af676be94a162adc65d738072db21d1b16815a0133` |
| `wsl-podman/final-1/final.log` (bundle `8b92ecaf`) | `c76802812811e7087c7fa6ed88dbc834db17c7478d74d4aa2c243e485e76abeb` |
| `wsl-podman/final-2/final.log` (bundle `be7816ce`) | `5980e7feb54db36fdf23a8b3b9da55a597d9d6f6e6aa3146c9fbb608852121a5` |
| `wsl-podman/final-2/rebuild-unmodified.log` | `9fc72adb577272cb9a2f0bbf8e49e648ae52d104afca4f96ee99fbcf857cba07` |
| `wsl-podman/final-2/package-differences.txt` | `63b2991a28d32b9415d68d5f644285e3d5c14418ae71670567dabd997165b990` |
| `wsl-podman/final-2/modified-libfuse-mount.c` | `eb31d2587f90412024753b717d034c726650b739f48bbc1a26fa7d6cfa67f8d1` |
| `wsl-podman/output-hashes.txt` | `59be5cca3426fc37d59420cd8dfcc5c1d8c7f2f2ed0412664f4486bf8ecdbf2b` |
| `wsl-podman/rebuild-0-mknod-failure.txt` (first attempt, stopped at `mknod /dev/fuse`) | `9ecbd3e066d53d3cb0447f97db68902d068abe38fec0fccb9d8c418609bea497` |
| `wsl-podman/relink-1/relink-1-summary.txt` (appimagetool relink and `--relink` refusals) | `35963c35c2d2ba945b7e7d3336013b07333f5d6505a6dcaca461d73232589980` |

The driver scripts that produced these records are retained beside them. The first attempt's full build log was overwritten by the corrected run; only its retained tail shows the failure.

## Boundary

GitHub's renaming of `~` was not observed here, because nothing was uploaded; the new name avoids it either way. No hosted CI run exists for this branch. `rebuild.sh` was exercised with rootless Podman only, not Docker or an SELinux host. A later rebuild needs Docker Hub to keep serving the pinned Alpine digest and installs the then-current Alpine 3.21 packages, which may stop reproducing the bytes; the script then reports the difference. Whether the bundle satisfies LGPL-2.1 section 6 is the owner's legal call under EXT-04; this record shows that the archive carries the exact sources, rebuilds the shipped runtime byte for byte and relinks FileCat with a modified libfuse. `licenses/appimage-runtime/index.json` no longer lists "Downloaded runtime source reproducibility" as unresolved; its other qualification flags are unchanged. Release dashboard, issue register and REP sealing are left to the coordinating session.
