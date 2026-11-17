"""Finalize once, only after every final native controller has actually completed."""
import hashlib,io,json,pathlib,subprocess,tarfile,xml.etree.ElementTree as E
r=pathlib.Path.cwd();t=r/'.tmp';d=r/'docs/validation/disk-space'
target=d/'source-manifest-r1328-indexed-deferred.json'
outputs=[d/('exact-'+kind+'-r1328-indexed-deferred.tgz')for kind in('source-deltas','receipts','workflow')]
assert not target.exists()and not any(p.exists()for p in outputs),'Immutable packet already exists'
def sha(p):
    with p.open('rb')as f:return hashlib.file_digest(f,'sha256').hexdigest()
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
final=read(t/'native-deferred-proof-r1326.json')
known={x['path']:x['sha256']for x in final['sources']}
assert all(sha(r/n)==v for n,v in known.items()),'Current source differs from the frozen final packet'
assert sha(t/'source-r1326-native-deferred.tgz')==final['sourceSha256']
subprocess.run(['git','diff','--quiet','--','src','tests'],check=True)
assert not subprocess.check_output(['git','ls-files','--others','--exclude-standard','src','tests'],text=True).strip()
base='026a127248cfb529dbb3a0e79628d583935474f8'
code_changed=subprocess.check_output(['git','diff','--name-only',base,'--','src','tests'],text=True).splitlines()
assert all(n in known for n in code_changed)
paths=sorted(known)
batch=subprocess.run(['git','cat-file','--batch'],input=''.join(base+':'+p+'\n'for p in paths).encode(),capture_output=True,check=True)
stream=io.BytesIO(batch.stdout);changed=[]
for n in paths:
    header=stream.readline().rstrip(b'\n')
    if header.endswith(b' missing'):changed.append(n);continue
    fields=header.split();assert len(fields)==3 and fields[1]==b'blob'
    data=stream.read(int(fields[2]));assert stream.read(1)==b'\n'
    if hashlib.sha256(data).hexdigest()!=known[n]:changed.append(n)
assert not stream.read()
retained={};executions={};native={};workflow={};variants={}
headless=[(rev,tag)for rev,tags in [
 ('r1305',['windows','mac','ubuntu']),('r1307',['windows','mac','ubuntu']),('r1308',['mac']),
 ('r1310',['windows','mac','ubuntu']),('r1321',['mac','ubuntu']),('r1324',['windows','mac','ubuntu'])]for tag in tags]
for rev,tag in headless:
    archive=t/f'{tag}-batch-{rev}-results.tgz';post=t/f'{tag}-batch-{rev}-post.json'
    assert not read(post)['filecatOrDotnet'],(rev,tag,'Worker remains')
    retained[archive.name]=archive;retained[post.name]=post
    with tarfile.open(archive)as packed:
        state=packed.extractfile('state.txt').read().decode('utf-8-sig').strip()
        assert state.startswith('FINISHED exit=')
        for m in packed.getmembers():
            if not m.name.endswith('.xml'):continue
            a=E.fromstring(packed.extractfile(m).read()).find('assembly')
            counts={k:int(a.get(k,'-1'))for k in('total','passed','failed','errors','skipped')}
            assert counts['total']>0 and counts['errors']==0
            key=f'{tag}/{rev}/{pathlib.PurePosixPath(m.name).stem}'
            executions[key]=dict(**counts,seconds=a.get('time'),state=state,
                failures=[dict(name=x.get('name'),message=x.findtext('failure/message'),stack=x.findtext('failure/stack-trace'))for x in a.iter('test')if x.get('result')=='Fail'],
                skips=[dict(name=x.get('name'),reason=''.join(x.find('reason').itertext()))for x in a.iter('test')if x.get('result')=='Skip'])
for tag in('windows','mac','ubuntu'):
    full=executions[f'{tag}/r1310/render'];assert(full['total'],full['passed'],full['failed'],full['skipped'])==((842,841,0,1)if tag=='windows'else(842,842,0,0))
    focused=executions[f'{tag}/r1324/render'];assert(focused['total'],focused['passed'],focused['failed'],focused['skipped'])==(40,40,0,0)
    assert executions[f'{tag}/r1305/settings']['passed']==1
    assert executions[f'{tag}/r1305/app-rescan']['failed']==4
    assert executions[f'{tag}/r1307/render']['failed']==1
native_runs=[(rev,tag)for rev,tags in[
 ('r1313',['mac']),('r1318',['mac','ubuntu']),('r1322',['mac','ubuntu']),
 ('r1326',['mac','ubuntu','windows']),('r1327',['windows'])]for tag in tags]
app_sha=sha(t/'batch-r1326-windows/FileCat.dll')
for rev,tag in native_runs:
    archive=t/f'{tag}-native-{rev}-results.tgz';post=t/f'{tag}-native-{rev}-post.json'
    assert not read(post)['filecatOrDotnet'],(rev,tag,'Worker remains')
    retained[archive.name]=archive;retained[post.name]=post
    with tarfile.open(archive)as packed:
        raw=packed.extractfile('result.json').read()
        try: decoded=raw.decode('utf-8-sig');encoding='utf-8'
        except UnicodeDecodeError:
            assert tag=='windows'
            decoded=raw.decode('cp1252');encoding='windows-1252'
        data=json.loads(decoded);data['retainedResultJsonEncoding']=encoding
        if tag=='windows'and rev=='r1327':
            app_report=json.loads(packed.extractfile('report.json').read().decode('utf-8-sig'))
            assert app_report['state']=='closed'and app_report['sourceUnchanged']and not app_report['deadlineClosed']
            assert app_report['appSha256']==data['closed']['appSha256']and app_report['harnessSha256']==data['closed']['harnessSha256']
            assert app_report['indexedDeferredProbe']['frames']==data['closed']['indexedDeferredProbe']['frames']
            assert app_report['indexedDeferredProbe']['passed']and app_report['indexedDeferredProbe']['completedMatches']==50000 and app_report['indexedDeferredProbe']['reviewedMarks']==50000
    assert not data['postProcesses']
    native[f'{tag}/{rev}']=data
for tag,rev in(('mac','r1326'),('ubuntu','r1326'),('windows','r1327')):
    data=native[f'{tag}/{rev}'];probe=data['finished']['indexedDeferredProbe']
    assert data['passed']and data['exitCode']==0 and not data.get('forcedOwnedTermination',False)
    assert data['closed']['state']=='closed'and data['closed']['sourceUnchanged']and not data['closed']['deadlineClosed']
    assert data['finished']['appSha256']==app_sha
    assert probe['passed']and probe['completedMatches']==50000 and probe['reviewedMarks']==50000 and probe['sessions']==2
    assert 0<probe['previewPaths']<50000 and probe['pageLimit']==128 and len(probe['frames'])==3 and all(x['safeDefault']and x['initialPage']==128 for x in probe['frames'])
    if tag=='mac':assert len(data['compositorCaptures'])==3 and all(x['compositorCaptured']for x in probe['frames'])
    else:assert not probe['osInputQualified']and not probe['compositorCaptureQualified']
assert not native['windows/r1326']['passed']and native['windows/r1326']['forcedOwnedTermination']
assert any(x['ProcessName']=='dotnet'and x['SessionId']!=native['windows/r1326']['launch']['childSession']for x in native['windows/r1326']['gateObservation'])
assert not native['mac/r1313']['passed']and not native['ubuntu/r1318']['passed']
task=t/'windows-native-r1327-task-outcome.json';outcome=read(task)
assert outcome['terminalExit']==0 and outcome['logonType']=='Interactive'and outcome['runLevel']=='Limited'and outcome['principal'].endswith('\\filecatdsa_r2')
retained[task.name]=task
encoding_proof=t/'native-receipt-encoding-proof-r1328.json';encoding_receipt=t/'windows-native-receipt-encoding-r1328.json'
encoding=read(encoding_proof);assert encoding['passed']and encoding['nonAsciiRoundTrip']and sha(encoding_receipt)==encoding['receiptSha256']
assert 'Set-Content -Encoding UTF8' in(d/'drivers/native-replacement-ui.ps1').read_text()
retained[encoding_proof.name]=encoding_proof;retained[encoding_receipt.name]=encoding_receipt
packet_specs=[('indexed-deferred',rev)for rev in('r1305','r1307','r1308','r1310','r1321','r1324')]
packet_specs += [('native-deferred',rev)for rev in('r1313','r1318','r1322','r1326')]
source_packets=[]
for kind,rev in packet_specs:
    proof=t/f'{kind}-proof-{rev}.json';packet=read(proof);workflow[proof.name]=proof
    source=t/f'source-{rev}-{"indexed-deferred"if kind=="indexed-deferred"else"native-deferred"}.tgz'
    digest=next(packet[k]for k in('sourceSnapshotSha256','snapshotSha256','sourceSha256')if k in packet)
    assert sha(source)==digest
    delta=[x for x in packet['sources']if known.get(x['path'])!=x['sha256']]
    with tarfile.open(source)as packed:
        for x in delta:
            data=packed.extractfile(x['path']).read();assert hashlib.sha256(data).hexdigest()==x['sha256']
            variants[f'old-sources/{kind}-{rev}/{x["path"]}']=data
    source_packets.append(dict(kind=kind,revision=rev,snapshotSha256=digest,priorVariants=len(delta)))
for rev in('r1327',):workflow[f'native-deferred-proof-{rev}.json']=t/f'native-deferred-proof-{rev}.json'
for p in t.iterdir():
    if not p.is_file():continue
    if p.name.startswith('indexed-deferred-')and ('build-r' in p.name or 'restore-r' in p.name):workflow[p.name]=p
    if p.name.startswith('batch-')and p.suffix in('.ps1','.sh')and any('-'+rev in p.name for rev in('r1305','r1307','r1308','r1310','r1321','r1324','r1313','r1318','r1322','r1325','r1326','r1327')):workflow[p.name]=p
    if p.name.startswith('windows-pre-r1321')or p.name.startswith('windows-pre-r1324check'):workflow[p.name]=p
for p in(d/'drivers').iterdir():
    if p.name in('deploy-batch.ps1','preflight-indexed-r732.ps1','collect-rescan-ui-r1216.ps1','collect-native-deferred-r1313.ps1','native-replacement-ui.ps1','verify-native-receipt-encoding-r1328.ps1','finalize-indexed-deferred-r1328.py')or p.name.startswith(('prepare-indexed-deferred-r13','prepare-native-deferred-r13')):workflow['drivers/'+p.name]=p
for platform in('windows','mac','linux'):
    name='FileCat.exe'if platform=='windows'else'FileCat'
    workflow['native-apphosts/'+platform+'/'+name]=t/('batch-r1313-'+platform)/name
assert all(p.is_file()for p in workflow.values())
manifest=dict(baseCommit=base,sourceCommit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
 buildHeadCommit=base,sourceBytePolicy='Raw base git blobs plus exact tested byte deltas; no checkout EOL assumptions.',
 sources=[dict(path=n,sha256=known[n])for n in changed],codeChangedPaths=code_changed,sourcePackets=source_packets,
 executions=executions,nativeExecutions=native,finalAppSha256=app_sha,
 scope='Internal indexed after-scan selection capture, paged preserving review, exact new-tab marks, runtime owner transfer, queued-read retirement, scope/cohort summaries and wrapped path rows.',
 broadRenderingSourceRevision='r1310',finalScopedRenderingRevision='r1324',finalScopedCasesPerPlatform=40,
 finalNativeRevisions=dict(windows='r1327',mac='r1326',ubuntu='r1326'),nativeGeneratedMatchingPaths=50000,
 physicalInputQualified=False,fullAccessibilityQualified=False,fullW1Complete=False,publicFeatureRegistered=False,
 fullGoalComplete=False,finalIndependentReviewPerformed=False,
 receipts=[dict(path=n,sha256=sha(p))for n,p in sorted(retained.items())],
 workflow=[dict(path=n,sha256=sha(p))for n,p in sorted(workflow.items())],
 preservedNotes=['Original failures are retained; full 845 rendering discovery was not executed. Broad 842 plus final scoped 40 are distinct.',
 'The first Mac offscreen 2x render was distorted. Actual compositor captures establish the scoped visible layout; preserving native footer geometry is independently checked.',
 'Windows r1326 was interrupted by a foreign session dotnet worker; the controller terminated only its exact owned child. The unchanged binaries were retried from verified guest cache.',
 'Native r1325 preparation failed its cached-script hash guard before any guest execution; its prepared Windows runner remains an unexecuted draft.',
 'Windows PowerShell wrote its nested result receipt in Windows-1252. The original bytes are retained; critical final fields are cross-checked against the application UTF-8 report. Future controllers explicitly write UTF-8.',
 'A harmless extra final blank line in the native harness is retained to preserve the exact tested compiler input.'])
# No immutable output is written before all of the preceding gates have passed.
with tarfile.open(outputs[0],'w:gz')as out:
    for n in changed:
        data=(r/n).read_bytes();m=tarfile.TarInfo(n);m.size=len(data);m.mode=0o600;out.addfile(m,io.BytesIO(data))
with tarfile.open(outputs[1],'w:gz')as out:
    for n,p in sorted(retained.items()):out.add(p,arcname=n,recursive=False)
with tarfile.open(outputs[2],'w:gz')as out:
    for n,p in sorted(workflow.items()):out.add(p,arcname=n,recursive=False)
    for n,data in sorted(variants.items()):
        m=tarfile.TarInfo(n);m.size=len(data);m.mode=0o600;out.addfile(m,io.BytesIO(data))
manifest['archives']=[dict(path=p.name,sha256=sha(p),bytes=p.stat().st_size)for p in outputs]
target.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print(json.dumps(dict(headlessPhases=len(executions),nativeRuns=len(native),sourceDeltas=len(changed),priorSourceVariants=len(variants))))
