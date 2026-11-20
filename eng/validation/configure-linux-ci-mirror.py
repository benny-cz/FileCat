"""Use the canonical Ubuntu archive for Azure and EC2 mirrors on disposable CI runners."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile

HOSTED_MIRROR = re.compile(rb'(?<!\S)https?://(?:azure|[a-z0-9-]+\.ec2)\.archive\.ubuntu\.com/ubuntu(?=[/\s]|$)')
ARCHIVE = b'https://archive.ubuntu.com/ubuntu'


def configure(root):
    root = root.resolve(strict=True)
    targets = [root / 'sources.list', root / 'sources.list.d/ubuntu.sources', root / 'apt-mirrors.txt']
    # Validate every target before the first write; no following a source file or parent link outside this root.
    for path in targets:
        if path.is_symlink() or (path.exists() and not path.resolve().is_relative_to(root)):
            raise ValueError('APT source target must stay inside the supplied source directory without a file symlink')
    records = []
    for path in targets:
        if not path.exists():
            continue
        before = path.read_bytes()
        after, count = HOSTED_MIRROR.subn(ARCHIVE, before)
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
                            NonMirrorBytesSHA256=hashlib.sha256(HOSTED_MIRROR.sub(b'<mirror>', before)).hexdigest(),
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
    proof = dict(Records=configure(args.sources_root), OnlyHostedUbuntuMirrorsReplaced=True,
                 NewMirror=ARCHIVE.decode(), RequiredPackageOrTestScopeReduced=False)
    args.evidence_file.parent.mkdir(parents=True, exist_ok=True)
    with args.evidence_file.open('x', encoding='utf-8') as stream:
        json.dump(proof, stream, indent=2)
    print(json.dumps(proof))


if __name__ == '__main__':
    main()
