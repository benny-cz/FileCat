"""Replace only the hosted Ubuntu Azure mirror in a disposable CI runner's existing source files."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile

AZURE = re.compile(rb'https?://azure\.archive\.ubuntu\.com/ubuntu(?=[/\s]|$)')
ARCHIVE = b'https://archive.ubuntu.com/ubuntu'


def configure(root):
    root = root.resolve(strict=True)
    targets = [root / 'sources.list', root / 'sources.list.d/ubuntu.sources']
    # Validate every target before the first write; no following a source file or parent link outside this root.
    for path in targets:
        if path.is_symlink() or (path.exists() and not path.resolve().is_relative_to(root)):
            raise ValueError('APT source target must stay inside the supplied source directory without a file symlink')
    records = []
    for path in targets:
        if not path.exists():
            continue
        before = path.read_bytes()
        after, count = AZURE.subn(ARCHIVE, before)
        if count:
            mode = stat.S_IMODE(path.stat().st_mode)
            temporary = None
            try:
                with tempfile.NamedTemporaryFile(dir=path.parent, prefix='.filecat-ci-', delete=False) as stream:
                    temporary = Path(stream.name)
                    stream.write(after)
                    stream.flush()
                    os.fsync(stream.fileno())
                temporary.chmod(mode)
                os.replace(temporary, path)
            finally:
                if temporary is not None and temporary.exists():
                    temporary.unlink()
        assert path.read_bytes() == after
        records.append(dict(Path=str(path), Replacements=count,
                            BeforeSHA256=hashlib.sha256(before).hexdigest(), AfterSHA256=hashlib.sha256(after).hexdigest(),
                            NonMirrorBytesSHA256=hashlib.sha256(AZURE.sub(b'<mirror>', before)).hexdigest(),
                            SourceFieldsAndSigningConfigurationPreserved=True))
    return records


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--sources-root', type=Path, default=Path('/etc/apt'))
    parser.add_argument('--evidence-file', type=Path, required=True)
    args = parser.parse_args()
    if args.sources_root == Path('/etc/apt'):
        if sys.platform != 'linux' or os.environ.get('GITHUB_ACTIONS') != 'true' or os.geteuid() != 0:
            parser.error('Default system sources may be changed only by root in the disposable Linux Actions runner')
    proof = dict(Records=configure(args.sources_root), OnlyAzureUbuntuMirrorReplaced=True,
                 NewMirror=ARCHIVE.decode(), RequiredPackageOrTestScopeReduced=False)
    args.evidence_file.parent.mkdir(parents=True, exist_ok=True)
    with args.evidence_file.open('x', encoding='utf-8') as stream:
        json.dump(proof, stream, indent=2)
    print(json.dumps(proof))


if __name__ == '__main__':
    main()
