#!/usr/bin/env python3
"""Independently verify one sealed Ubuntu FZ01/FZ09 native fuzz transport."""

import hashlib
import json
from pathlib import Path
import sys
import tarfile


ROOT=Path(__file__).resolve().parents[3]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def verify(image,run,target,minimum_cpu_seconds):
    path=ROOT/'.tmp/storage-evidence/ubuntu'/(image+'-'+run+'-evidence.tar.gz')
    sidecar=json.loads(path.with_suffix('.json').read_text())
    assert path.stat().st_size==sidecar['bytes'] and sha(path.read_bytes())==sidecar['sha256']
    with tarfile.open(path,'r:gz') as archive:
        members=archive.getmembers()
        assert all(item.isfile() and not item.issym() and not item.islnk() and
                   not item.name.startswith('/') and '..' not in Path(item.name).parts for item in members)
        assert len({item.name for item in members})==len(members)
        data={item.name:archive.extractfile(item).read() for item in members}
    index=json.loads(data.pop('index.json'))
    assert (index['image'],index['run'],index['target'],index['minimum_cpu_seconds'])==(image,run,target,minimum_cpu_seconds)
    rows={row['path']:row for row in index['files']}
    assert set(rows)==set(data)
    assert sum(len(value) for value in data.values())<=256*1048576
    for name,value in data.items():
        assert rows[name]=={'path':name,'bytes':len(value),'sha256':sha(value)}
    file=lambda name:data['image/'+name]
    result=json.loads(file('runs/'+run+'/result.json'))
    prep=json.loads(file('preparation.json'))
    final=result['final_properties'];report=result['managed_report']
    assert result['terminal'] and not result.get('output_budget_failure') and not result['artifacts']
    assert result['target']==report['Target']==target and result['phase']=='clean'
    assert final['MainPID']=='0' and final['Result']=='success' and final['ExecMainStatus']=='0'
    assert int(final['CPUUsageNSec'])>=minimum_cpu_seconds*10**9 and int(final['MemoryPeak'])<=1073741824
    assert report['Calls']==report['Accepted']+report['Refused'] and report['Calls']>0
    assert result['sha256']['core']==sha(file('clean/FileCat.Core.dll'))==report['ModuleSha256'].lower()
    assert result['sha256']['harness']==sha(file('clean/FileCat.ArchiveFuzzHarness.dll'))
    packets=[value for name,value in data.items() if name.startswith('packet/')]
    assert len(packets)==1 and prep['packet_sha256']==sha(packets[0])
    assert prep['images']['clean']['FileCat.Core.dll']['sha256']==result['sha256']['core']
    assert prep['images']['clean']['FileCat.ArchiveFuzzHarness.dll']['sha256']==result['sha256']['harness']
    if target=='tar-strict':
        assert report['SecondaryModuleSha256'].lower()==sha(file('clean/FileCat.Archives.dll'))
        assert {row['name'] for row in result['extra_seeds']}=={
            'ustar-one-file.tar','ustar-directory.tar','ustar-duplicate.tar','pax-unicode-time.tar'}
        assert any('StrictTarCatalog::Read' in name for name in report['production_methods'])
        assert any('StrictTarCatalog::CompareDecoder' in name for name in report['production_methods'])
    for number,row in enumerate(result['extra_seeds']):
        content=file('runs/'+run+f'/corpus/synthetic-{number:04d}')
        assert len(content)==row['bytes'] and sha(content)==row['sha256']
    assert not any(name.startswith('image/runs/'+run+'/crashes/') for name in data)
    return {'image':image,'run':run,'target':target,'cpu_seconds':int(final['CPUUsageNSec'])/10**9,
            'executions':report['Calls'],'accepted':report['Accepted'],'refused':report['Refused'],
            'transport_sha256':sidecar['sha256']}


if __name__=='__main__':
    print(json.dumps(verify(sys.argv[1],sys.argv[2],sys.argv[3],int(sys.argv[4])),indent=2))
