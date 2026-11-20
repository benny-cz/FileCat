import hashlib, json, os, pathlib, re, sys
kind = 'mac' if sys.platform == 'darwin' else 'linux'
base = pathlib.Path.home() / 'FileCatValidation' / ('dsa-20261106-r397-indexed-' + kind)
receipt = json.loads((base / 'native-indexed-result.json').read_text())
assert receipt['passed'] and receipt['exitCode'] == 0 and receipt['closed']['sourceUnchanged'] and not receipt['closed']['deadlineClosed']
run = pathlib.Path(receipt['closed']['run']).resolve(strict=True)
assert run.is_relative_to((base / 'app' / 'native-validation').resolve(strict=True))
profile = receipt['closed']['indexedProbe']['profile'].replace('-', '')
found = []
for current, directories, files in os.walk(run / 'data', followlinks=False):
    for name in files:
        match = re.fullmatch(r'capacity-([0-9]{2})[.]state', name)
        if match:
            slot = int(match.group(1)); assert slot < 32
            path = pathlib.Path(current) / name
            expected = ('FileCat profile capacity v1\n' + profile + '\n' + format(slot, '02d') + '\n').encode('ascii')
            with path.open('rb') as stream: actual = stream.read(129)
            assert actual == expected
            assert hashlib.sha256((path.parent / 'private-state.json').read_bytes()).hexdigest() == receipt['closed']['indexedProbe']['privateStateSha256']
            found.append({'slot': slot, 'sha256': hashlib.sha256(actual).hexdigest(), 'bytes': len(actual)})
assert 1 <= len(found) <= 32
out = {'passed': True, 'purpose': 'Read-only native indexed workflow file-capacity integration check after owned exit', 'tokenCount': len(found), 'tokens': found, 'profileStateMatchesNativeReceipt': True}
(base / 'file-capacity-post-r398.json').write_text(json.dumps(out, indent=2) + '\n')
print(json.dumps(out))
