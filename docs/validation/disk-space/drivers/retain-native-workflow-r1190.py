"""Retain workflow bytes without rewriting the completed r1190 qualification."""
import hashlib
import json
import pathlib
import tarfile

r = pathlib.Path.cwd()
out = r / 'docs/validation/disk-space'
manifest = out / 'source-manifest-r1190-native-scale-workflow.json'
archive = out / 'exact-native-workflow-r1190.tgz'
assert not manifest.exists() and not archive.exists()


def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()


paths = set()
for pattern in ('*native*1175*', '*native*1181*', '*native*1184*', '*native*1185*', '*native*1186*', '*native*1187*', '*native*1190*'):
    paths.update(p for p in (out / 'drivers').glob(pattern) if p.is_file())
for revision in ('r1175', 'r1181', 'r1182', 'r1184', 'r1185'):
    paths.update((r / f'.tmp/batch-{revision}-mac/render').glob('*.py'))
assert sha(out / 'drivers/validate-native-optimization-r1184.py') == sha(r / '.tmp/batch-r1185-mac/render/validate-native-optimization.py')
assert sha(out / 'drivers/validate-native-optimization-r1181.py') == sha(r / '.tmp/batch-r1182-mac/render/validate-native-optimization.py')
rows = [dict(path=p.relative_to(r).as_posix(), sha256=sha(p)) for p in sorted(paths)]
with tarfile.open(archive, 'w:gz') as bundle:
    for row in rows:
        bundle.add(r / row['path'], arcname=row['path'], recursive=False)
manifest.write_text(json.dumps(dict(qualification='source-manifest-r1190-native-scale.json',
    qualificationSha256=sha(out / 'source-manifest-r1190-native-scale.json'),
    purpose='Supplemental workflow provenance only; historical staged validators remain distinct from current corrected files',
    entries=rows, archive=archive.name, archiveSha256=sha(archive)), indent=2) + '\n')
print('Retained additional native workflow sources:', len(rows))
