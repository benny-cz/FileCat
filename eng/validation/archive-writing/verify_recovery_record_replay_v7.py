#!/usr/bin/env python3
"""Independently check guest FZ09 v7 seed replays against sealed v38 product binaries."""

import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys


ROOT=Path(__file__).resolve().parents[3]
CANDIDATE='a59c7d2aa5515cb81f6c59a2af8a0e138a2437b8'
RUNNER_COMMIT='62e6019b4a19a29afb5399d2a5a53fe44de83f7c'
PACKET_SHA='df43851a4e45cddbfe90583d2e315a577ad9d361b3f08926e9792992bb8442bd'
ACCEPTED={'session-record':{'remote-v1.json','zip-v2.json','remote-v3.json'},
          'capture-record':{'start.json','complete.json'},'save-copy-intent':{'valid.json'},
          'save-copy-receipt':{'valid.json','published.json'}}
spec=importlib.util.spec_from_file_location('transport',Path(__file__).with_name('verify_password_browsing_checkpoint.py'))
transport=importlib.util.module_from_spec(spec);spec.loader.exec_module(transport)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def verify(guest):
    platform='windows' if guest=='windows' else 'unix'
    result_path=ROOT/'.tmp/storage-evidence'/guest/'recovery-record-replay-v7.json'
    data=json.loads(result_path.read_text())
    assert data['candidate']==CANDIDATE and data['platform']==platform
    assert data['packet_sha256']==PACKET_SHA==sha((ROOT/'.tmp/fuzz-harness-v7.tar.gz').read_bytes())
    seeds=ROOT/'.tmp'/f'recovery-record-seeds-{platform}-v7.tar.gz'
    assert data['seeds_sha256']==sha(seeds.read_bytes())
    runner=subprocess.check_output(['git','show',RUNNER_COMMIT+':eng/validation/archive-writing/replay_recovery_records_guest.py'],cwd=ROOT)
    assert data['runner_sha256']==sha(runner)
    _,index,_=transport.transport(ROOT/'.tmp/storage-evidence'/guest/'awe-storage-v38-evidence.tar.gz','awe-storage-v38')
    assert data['product_core_sha256']==index['binaries/FileCat.Core.dll']['sha256']
    assert data['passed']==28 and data['failed']==0 and len(data['replays'])==28
    seen=set()
    for row in data['replays']:
        target,name=row['target'],row['seed']
        key=(target,name)
        assert key not in seen and target in ACCEPTED
        seen.add(key)
        source=ROOT/'.tmp'/f'recovery-replay-v7-{platform}'/target/name
        assert source.is_file() and row['seed_sha256']==sha(source.read_bytes())
        expected=name in ACCEPTED[target]
        assert row['expected_accepted']==row['accepted']==expected and row['returncode']==0 and not row['stderr']
        assert json.loads(row['stdout'])=={'target':target,'accepted':expected}
    assert {target for target,_ in seen}==set(ACCEPTED)
    return {'guest':guest,'candidate':CANDIDATE,'replays':len(seen),'accepted':sum(row['accepted'] for row in data['replays']),
            'result_sha256':sha(result_path.read_bytes())}


if __name__=='__main__':
    print(json.dumps([verify(guest) for guest in sys.argv[1:]],indent=2))
