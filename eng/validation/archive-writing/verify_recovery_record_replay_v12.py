#!/usr/bin/env python3
"""Independently check guest FZ09 v12 stateful replays against sealed v40 product binaries."""

import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tarfile


ROOT=Path(__file__).resolve().parents[3]
CANDIDATE='a521bf45285110a2c836d9caec824e207fb99a5e'
RUNNER_COMMIT='c7c51af24cb6f371c8a26975a1bcb32fb5c3104c'
PACKET_SHA='758aa0dcc9865ae30eac26d4aa53653243748798868a068aaee4e028699ffb3b'
ACCEPTED={'session-record':{'remote-v1.json','zip-v2.json','remote-v3.json'},
          'capture-record':{'start.json','complete.json'},'save-copy-intent':{'valid.json'},
          'save-copy-receipt':{'valid.json','published.json'},
          'journal-record':{'valid-begin.json','valid-two.json'},
          'journal-state':{'valid-begin.json','valid-two.json','valid-end.json'},
          'manifest-member':{'valid.bin','directory.bin'}}
spec=importlib.util.spec_from_file_location('transport',Path(__file__).with_name('verify_password_browsing_checkpoint.py'))
transport=importlib.util.module_from_spec(spec);spec.loader.exec_module(transport)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def verify(guest):
    platform='windows' if guest=='windows' else 'unix'
    result_path=ROOT/'.tmp/storage-evidence'/guest/'recovery-record-replay-v12.json'
    data=json.loads(result_path.read_text())
    assert data['candidate']==CANDIDATE and data['platform']==platform
    packet=ROOT/'.tmp/fuzz-harness-v12.tar.gz'
    assert data['packet_sha256']==PACKET_SHA==sha(packet.read_bytes())
    with tarfile.open(packet) as archive:
        for source in ('ArchiveFuzzHarness/Program.cs','make_session_fuzz_seeds.py',
                       'prepare_fuzz_guest.py','run_fuzz_guest.py'):
            repo='eng/validation/archive-writing/'+source
            member='source/'+source.removeprefix('ArchiveFuzzHarness/')
            assert archive.extractfile(member).read()==subprocess.check_output(
                ['git','show',RUNNER_COMMIT+':'+repo],cwd=ROOT)
    seeds=ROOT/'.tmp'/f'recovery-record-seeds-{platform}-v11.tar.gz'
    assert data['seeds_sha256']==sha(seeds.read_bytes())
    runner=subprocess.check_output(['git','show',RUNNER_COMMIT+':eng/validation/archive-writing/replay_recovery_records_guest_v12.py'],cwd=ROOT)
    assert data['runner_sha256']==sha(runner)
    _,index,_=transport.transport(ROOT/'.tmp/storage-evidence'/guest/'awe-storage-v40-evidence.tar.gz','awe-storage-v40')
    assert data['product_core_sha256']==index['binaries/FileCat.Core.dll']['sha256']
    assert data['passed']==54 and data['failed']==0 and len(data['replays'])==54
    seen=set()
    for row in data['replays']:
        target,name=row['target'],row['seed']
        key=(target,name)
        assert key not in seen and target in ACCEPTED
        seen.add(key)
        source=ROOT/'.tmp'/f'recovery-replay-v11-{platform}'/target/name
        assert source.is_file() and row['seed_sha256']==sha(source.read_bytes())
        expected=name in ACCEPTED[target]
        assert row['expected_accepted']==row['accepted']==expected and row['returncode']==0 and not row['stderr']
        assert json.loads(row['stdout'])=={'target':target,'accepted':expected}
    assert {target for target,_ in seen}==set(ACCEPTED)
    return {'guest':guest,'candidate':CANDIDATE,'replays':len(seen),'accepted':sum(row['accepted'] for row in data['replays']),
            'result_sha256':sha(result_path.read_bytes())}


if __name__=='__main__':
    print(json.dumps([verify(guest) for guest in sys.argv[1:]],indent=2))
