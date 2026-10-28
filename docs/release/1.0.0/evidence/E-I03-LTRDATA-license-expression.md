# E-I03-LTRDATA — signed NuGet MIT-expression reference text

**Preliminary I03 notice-provenance child accepted at `c3966157d458007d9b75eea07a5b21655b73e518`; not candidate qualification or release GO.**

LTRData.Extensions 1.0.23 contains no license-named archive member, and its package-declared source revision `915cdf4eaac22bffd2623de0721f7e7d18c93268` contains no license-named file. The package instead declares `<license type="expression">MIT</license>`, copyright `2011 - 2026, Olof Lagerkvist, LTR Data`, repository URL/revision and content hash in its signed nuspec. NuGet's [.nuspec reference](https://learn.microsoft.com/nuget/reference/nuspec#license) defines this value as an SPDX license expression; the [licenses.nuget.org protocol](https://learn.microsoft.com/nuget/nuget-org/licenses.nuget.org) exists to provide stable reference text for those expressions.

The correction retains byte-identical `text/MIT.txt` from exact `spdx/license-list-data` revision `f715b3660bb164c3d0b81cba9ce94e3c558c4048`:

- 1,078 bytes;
- SHA-256 `b05785f9f18e6716bab63424b11454513b9943a222595b70411009202fc592b5`;
- Git blob `d817195dad53ec992418c28ffca5fbd1cd86502a`;
- retained path `licenses/dependencies/spdx/MIT/f715b3660bb164c3d0b81cba9ce94e3c558c4048/MIT.txt`.

`dotnet nuget verify --all` on the exact restored archive passes its author and NuGet.org repository signatures and reports the locked content hash `K+Lb4/siT7+K50r4kndp5QaJLgupf5zE4qe8QxqwF0uSAiZtodsiFc3+i9IQpkGGMDwi83isG9qYA3JOJrO7UA==`. The author certificate SHA-256 is `25b8abb80f0feacd468c83aeffedc225f533d9d25df1707a8cb33d19544fb242`; the repository certificate SHA-256 is `1f4b311d9acc115c8dc8018b5a49e00fce6da8e2855f9f014ca6f34570bc482d`. Raw output is retained at `V:\FileCat\artifacts\release-evidence\i03-cross-platform-draft-20261028-v3\ltrdata-nuget-verify.txt`.

The notice index now binds the signed package identity, expression, copyright metadata and canonical reference bytes. `validate-dependency-notice-manifest.py` requires that exact relationship, rejects an altered reference digest, and copies the indexed text only after complete snapshot and payload identity checks. The exact clean-source run passes 69/69 controls (10 allowed, 59 refused) in `V:\FileCat\artifacts\release-evidence\i03-cross-platform-draft-20261028-v3\notice-controls`.

This closes only the recorded LTRData.Extensions full-text gap. It does not claim that upstream added a source-tree license file, prove reproducible package binaries, decide SharpCompress RAR eligibility, qualify every AppImage static component, freeze a candidate, sign an artifact or authorize publication.
