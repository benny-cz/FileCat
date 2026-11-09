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
    if target in ('journal-state','sevenzip-strict','rar5-strict'):
        canary_name,replay_name,short_name=index['qualification_runs']
        assert len({canary_name,replay_name,short_name})==3
        assert all(name.replace('-','').isalnum() for name in (canary_name,replay_name,short_name))
        canary=json.loads(file('runs/'+canary_name+'/result.json'))
        replay=json.loads(file('runs/'+replay_name+'/result.json'))
        short=json.loads(file('runs/'+short_name+'/result.json'))
        assert all(item['phase']=='canary' and item['target']=='digest' and item['terminal'] and item['canary_two_byte']
                   and item['final_properties']['Result']=='exit-code' and item['final_properties']['ExecMainStatus']=='77'
                   and item['sha256']['core']==prep['images']['canary']['FileCat.Core.dll']['sha256']
                   for item in (canary,replay))
        assert len(canary['artifacts'])==1 and canary['artifacts'][0]['bytes']==2
        crash=canary['artifacts'][0]
        assert len(file('runs/'+canary_name+'/crashes/'+crash['name']))==2
        assert sha(file('runs/'+canary_name+'/crashes/'+crash['name']))==crash['sha256']
        assert sha(file('runs/'+replay_name+'/replay-input'))==crash['sha256']
        assert not replay['artifacts'] and replay['managed_report']['Calls']==1
        assert all(item['managed_report']['ModuleSha256'].lower()==item['sha256']['core'] and
                   any('ArchiveWritePolicy::ValidateDigest' in name for name in item['managed_report']['production_methods'])
                   for item in (canary,replay))
        assert short['phase']=='clean' and short['target']==target and short['terminal'] and not short['artifacts']
        assert short['final_properties']['Result']=='success' and short['sha256']['core']==result['sha256']['core']
        assert short['managed_report']['Calls']>0
        if target=='journal-state':
            required=('JobJournal::ReadRecords','ArchiveTransactionCollector::Read')
            names={'valid-begin.json','valid-two.json','bad-crc.json','torn.json',
                   'invalid-utf8.json','unknown-tag.json','valid-end.json','future-header.json',
                   'unsupported-archive-record.json','duplicate-end.json',
                   'damaged-after-header.json','record-before-begin.json'}
            assert 'FileCat.Core.Archives.ArchiveTransactionCollector' in prep['instrumented_core_types']['clean']
        elif target=='sevenzip-strict':
            required=('StrictSevenZipCatalog::Read','StrictSevenZipFraming::Read')
            names={'EmptyEntries.7z','EmptyEntries.solid.7z','FileCat.Methods.Copy.7z',
                   'FileCat.Methods.LZMA.7z','FileCat.Methods.BZip2.7z','FileCat.Methods.Deflate.7z',
                   'FileCat.Methods.Deflate64.7z','7Zip.LZMA2.7z'} | {
                       f'generated-codec-{number}.7z' for number in range(7)}
            assert report['SecondaryModuleSha256'].lower()==sha(file('clean/FileCat.Archives.dll'))
            assert {'FileCat.Archives.Writing.StrictSevenZipCatalog',
                    'FileCat.Archives.Writing.StrictSevenZipFraming'}.issubset(prep['instrumented_tar_types']['clean'])
            mac=ROOT/'.tmp/storage-evidence/mac/fuzz-sevenzip-v14-mac-seeds.tar.gz'
            assert sha(mac.read_bytes())=='0096d979dda7b6a2df368aaa2c062b09a0fb7a814a7a2cae9010d035abdac664'
            with tarfile.open(mac,'r:gz') as mac_tar:
                mac_manifest=json.load(mac_tar.extractfile('seeds/manifest.json'))
            expected_seeds={row['name']:(row['bytes'],row['sha256']) for row in mac_manifest['seeds']}
            assert mac_manifest['target']=='sevenzip-strict' and set(expected_seeds)==names
            assert {row['name']:(row['bytes'],row['sha256']) for row in result['extra_seeds']}==expected_seeds
        else:
            required=('StrictRar5Catalog::Read','StrictRar5Framing::Read')
            names={'Rar4.rar','Rar.malformed_512byte.rar'} | {
                f'generated-method-{method}-{stage}.rar'
                for method in range(6) for stage in ('source','rebuilt')}
            assert report['SecondaryModuleSha256'].lower()==sha(file('clean/FileCat.Archives.dll'))
            assert {'FileCat.Archives.Writing.StrictRar5Catalog',
                    'FileCat.Archives.Writing.StrictRar5Framing'}.issubset(prep['instrumented_tar_types']['clean'])
            mac=ROOT/'.tmp/storage-evidence/mac/fuzz-rar5-v15-mac-seeds.tar.gz'
            assert sha(mac.read_bytes())=='5fc4ee4fc07e8852d610ff03bf8bc65356bb4bbf9db5e0932b9b1eda8bd61a95'
            with tarfile.open(mac,'r:gz') as mac_tar:
                mac_manifest=json.load(mac_tar.extractfile('seeds/manifest.json'))
            expected_seeds={row['name']:(row['bytes'],row['sha256']) for row in mac_manifest['seeds']}
            assert mac_manifest['target']=='rar5-strict' and set(expected_seeds)==names
            assert {row['name']:(row['bytes'],row['sha256']) for row in result['extra_seeds']}==expected_seeds
        assert {row['name'] for row in result['extra_seeds']}==names
        assert all(any(method in name for name in short['managed_report']['production_methods']) for method in required)
        assert all(any(method in name for name in report['production_methods']) for method in required)
        assert {row['name'] for row in short['extra_seeds']}==names
        for number,row in enumerate(short['extra_seeds']):
            content=file('runs/'+short_name+f'/corpus/synthetic-{number:04d}')
            assert len(content)==row['bytes'] and sha(content)==row['sha256']
    for number,row in enumerate(result['extra_seeds']):
        content=file('runs/'+run+f'/corpus/synthetic-{number:04d}')
        assert len(content)==row['bytes'] and sha(content)==row['sha256']
    assert not any(name.startswith('image/runs/'+run+'/crashes/') for name in data)
    return {'image':image,'run':run,'target':target,'cpu_seconds':int(final['CPUUsageNSec'])/10**9,
            'executions':report['Calls'],'accepted':report['Accepted'],'refused':report['Refused'],
            'transport_sha256':sidecar['sha256']}


if __name__=='__main__':
    print(json.dumps(verify(sys.argv[1],sys.argv[2],sys.argv[3],int(sys.argv[4])),indent=2))
