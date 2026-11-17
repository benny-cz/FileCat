import hashlib
import json
import pathlib
import tarfile

r = pathlib.Path.cwd(); t = r / '.tmp'


def sha(p):
    with p.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


prior = json.loads((t / 'owned-runtime-view-proof-r1241.json').read_text())
changes = [row['path'] for row in prior['sources'] if sha(r / row['path']) != row['sha256']]
assert changes == ['tests/FileCat.Rendering.Tests/AnalysisRenderingTests.OwnedRuntimeView.cs'], changes
runner = t / 'batch-windows-r1243.ps1'; original = runner.read_text(encoding='utf-8-sig')
needle = " $result=$LASTEXITCODE;$ErrorActionPreference='Stop';('FINISHED exit='+$result)|Set-Content $state;exit $result"
assert original.count(needle) == 1
addition = r""" $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
 [xml]$focused=[IO.File]::ReadAllText((Join-Path $base 'render-r1243.xml'));$a=$focused.assemblies.assembly
 if($result-ne0-or[int]$a.total-ne9-or[int]$a.passed-ne9-or[int]$a.failed-ne0-or[int]$a.errors-ne0-or[int]$a.skipped-ne0){throw 'Focused runtime view failed'}
 if(Busy){'BUSY phase=render'|Set-Content $state;exit 75}
 'RUNNING phase=render-full'|Set-Content $state;$ErrorActionPreference='Continue'
 & 'C:\Program Files\dotnet\dotnet.exe' (Join-Path $payload $plan.assembly) -class '*AnalysisRenderingTests' -noLogo -noColor -maxThreads 2 -xml (Join-Path $base 'render.xml') *> (Join-Path $base 'render.log')
 $result=$LASTEXITCODE;$ErrorActionPreference='Stop'
 [xml]$full=[IO.File]::ReadAllText((Join-Path $base 'render.xml'));$a=$full.assemblies.assembly
 if($result-ne0-or[int]$a.total-ne810-or[int]$a.passed-ne809-or[int]$a.failed-ne0-or[int]$a.errors-ne0-or[int]$a.skipped-ne1){throw 'Full native rendering suite failed'}
 ('FINISHED exit='+$result)|Set-Content $state;exit $result"""
proof = dict(revision='r1243', sourceBaseline='r1241', changedSources=changes, changedSourceSha256=sha(r / changes[0]),
             previousProofSha256=sha(t / 'owned-runtime-view-proof-r1241.json'), deltaProofSha256=sha(t / 'focused-delta-proof-r1243.json'),
             originalRunnerSha256=sha(runner))
runner.write_text(original.replace(needle, addition), encoding='utf-8', newline='\n')
proof['finalRunnerSha256'] = sha(runner)
marker = t / 'cache-input-r1243'; assert not marker.exists(); marker.write_text('Verified cached r1241 rendering inputs, only test DLL/PDB overlaid.\n')
snapshot = t / 'exact-fixture-r1243-owned-runtime-view.tgz'; assert not snapshot.exists()
with tarfile.open(snapshot, 'w:gz') as archive: archive.add(r / changes[0], arcname=changes[0], recursive=False)
proof['fixtureArchiveSha256'] = sha(snapshot)
out = t / 'owned-fixture-continuation-proof-r1243.json'; assert not out.exists(); out.write_text(json.dumps(proof, indent=2))
print(json.dumps(proof, indent=2))
