"""Prepare a tracked copy of the receipts without full-desktop image pixels."""
import hashlib
import io
import json
from pathlib import Path
import tarfile


directory = Path("docs/validation/disk-space")
manifest = json.loads((directory / "source-manifest-r1328-indexed-deferred.json").read_text())
original = Path(".tmp") / "exact-receipts-r1328-indexed-deferred.tgz"
record = next(item for item in manifest["archives"] if item["path"] == original.name)
assert hashlib.sha256(original.read_bytes()).hexdigest() == record["sha256"]
output = directory / "repository-receipts-r1328-indexed-deferred.tgz"
mapping_path = directory / "repository-receipts-r1328-indexed-deferred.json"
assert not output.exists() and not mapping_path.exists()
omitted = {}


def add(archive, name, data):
    member = tarfile.TarInfo(name)
    member.mode = 0o600
    member.size = len(data)
    archive.addfile(member, io.BytesIO(data))


with tarfile.open(original) as source, tarfile.open(output, "w:gz") as target:
    for member in source:
        assert member.isfile()
        name = member.name
        data = source.extractfile(member).read()
        if name.startswith("mac-native-") and name.endswith("-results.tgz"):
            partial = io.BytesIO()
            with tarfile.open(fileobj=io.BytesIO(data)) as native, tarfile.open(fileobj=partial, mode="w:gz") as packaged:
                for item in native:
                    assert item.isfile()
                    payload = native.extractfile(item).read()
                    if item.name.startswith("compositor-") and item.name.endswith(".png"):
                        omitted[f"{name}/{item.name}"] = hashlib.sha256(payload).hexdigest()
                    else:
                        add(packaged, item.name, payload)
            data = partial.getvalue()
        add(target, name, data)

assert len(omitted) == 9
mapping_path.write_text(json.dumps({
    "originalArchiveSha256": record["sha256"],
    "repositoryArchiveSha256": hashlib.sha256(output.read_bytes()).hexdigest(),
    "omittedFullDesktopImages": omitted,
}, indent=2) + "\n")
