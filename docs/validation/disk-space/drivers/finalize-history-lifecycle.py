"""Verify the complete lifecycle cohort and its scoped App UI rerun; retain each raw archive once."""
import datetime, hashlib, json, pathlib, re, shutil, subprocess, tarfile
import xml.etree.ElementTree as E
root=pathlib.Path.cwd(); temp=root/'.tmp'; out=root/'docs/validation/disk-space'; revision='r1058'
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):
 with p.open('rb') as f: return hashlib.file_digest(f,'sha256').hexdigest()
def archive(p):
 with tarfile.open(p) as t:
  rows={}
  for m in t.getmembers():
   if not m.isfile() or m.name.startswith('payload/') or m.name.startswith('./payload/'): continue
   name=pathlib.PurePosixPath(m.name).name
   assert name not in rows and m.size<=32*1024*1024
   rows[name]=t.extractfile(m).read()
  return rows
report_name='source-manifest-r1058-lifecycle.json'
assert not (out/report_name).exists(), 'Fresh immutable qualification required'
sources=read(temp/'source-candidate-r1058.json')
assert sources and all(sha(root/s['path'])==s['sha256'] for s in sources)
base=read(temp/'source-candidate-r1050.json'); current={s['path']:s['sha256'] for s in sources}
fixture='tests/FileCat.Rendering.Tests/AnalysisRenderingTests.HistoryLifecycle.cs'
assert all(s['path']==fixture or current[s['path']]==s['sha256'] for s in base)
assert set(current)-{s['path'] for s in base}=={'src/FileCat.App/Views/DiskSpaceWindow.Indexed.cs','tests/FileCat.Rendering.Tests/AnalysisRenderingTests.IndexedSelection.cs'}
proof=read(temp/'app-ui-delta-proof-r1058.json'); control_proof=read(temp/'app-ui-delta-proof-r1057.json')
assert proof['baseline']==control_proof['baseline']=='r1050' and proof['scope']=='AppUi'
assert control_proof['scope']=='OldProductControl' and control_proof['productSourceBaseline']=='r1050'
allowed={'render/FileCat.dll','render/FileCat.pdb','render/FileCat.Rendering.Tests.dll','render/FileCat.Rendering.Tests.pdb'}
inputs={}; files=set(); drivers=set(); matrix={}; processes={}; bundles={}
for platform in ['windows','mac','ubuntu']:
 tag='linux' if platform=='ubuntu' else platform
 row=next(v for v in proof['platforms'] if v['platform']==tag)
 assert row['guestReuse'] and set(row['changed'])==allowed
 assert sha(temp/f'dsa-batch-{tag}-r1058.tgz')==row['sha256']
 delta=temp/('test-fix-delta-r1058' if tag=='windows' else f'test-fix-delta-{tag}-r1058')
 plan=read(delta/'plan.json'); assert plan['scope']=='AppUi'
 assert {v['path'] for v in plan['files'] if v['patch']}==allowed
 stage=temp/f'batch-r1058-{tag}'; baseline=temp/f'batch-r1050-{tag}'
 assert all(sha(stage/v['path'])==v['sha256'] and sha(baseline/v['path'])==v['baselineSha256'] for v in plan['files'])
 inputs[platform]=plan
 for r in ['r1050','r1058']:
  stem=f'{platform}-batch-{r}'; path=temp/f'{stem}-results.tgz'; bundles[platform,r]=archive(path); files.add(path.name)
  post=read(temp/f'{stem}-post.json'); assert not post['filecatOrDotnet']; processes[f'{platform}/{r}']=post
 assert bundles[platform,'r1058']['state.txt'].decode('utf-8-sig').strip()=='FINISHED exit=0'
 assert bundles[platform,'r1050']['state.txt'].decode('utf-8-sig').strip()==('FINISHED exit=0' if platform=='mac' else 'FINISHED exit=1 phase=preview' if platform=='windows' else 'FINISHED exit=1')
 for phase in ['smoke','preview','core','render','appsettings']+(['native'] if platform=='windows' else []):
  r='r1050' if phase=='core' and platform!='windows' else 'r1058'
  a=E.fromstring(bundles[platform,r][f'{phase}-{r}.xml']).find('assembly')
  total,skip={'smoke':(27,0),'preview':(61,0),'core':(1577,48) if platform=='windows' else (1559,4),'render':(456,1) if platform=='windows' else (456,0),'appsettings':(35,1) if platform=='windows' else (35,0),'native':(75,0)}[phase]
  assert a.get('failed')==a.get('errors')=='0'
  assert (int(a.get('total')),int(a.get('passed')),int(a.get('skipped')))==(total,total-skip,skip)
  skips=lambda node:sorted(t.get('name') for t in node.findall('.//test') if t.get('result')=='Skip')
  old=E.parse(out/f'{platform}-batch-r1036-{phase}-r1036.xml').getroot().find('assembly'); assert skips(a)==skips(old)
  result={'revision':r,**{key:a.get(key) for key in ['total','passed','failed','skipped','errors','time']},'skippedNames':skips(a)}
  if phase in ['preview','render']:
   prefixes=('NativeIdleHistory','NativeHistoryStartupSweep','NativeHistoryReviewedPolicyChange','NativeHistoryDisabledStartup','NativeOpaqueHistoryRemoval','OpaqueHistoryRemovalReview')
   cases=[t for t in a.findall('.//test') if (t.get('method') or '').startswith(prefixes)]
   assert len(cases)==22 and all(t.get('result')=='Pass' for t in cases); result['lifecycleCases']=[t.get('name') for t in cases]
  matrix[f'{platform}/{phase}']=result
 for r in ['r1050','r1058']:
  drivers.add(f'batch-{tag}-{r}.'+('ps1' if platform=='windows' else 'sh'))
failed=E.fromstring(bundles['windows','r1050']['preview-r1050.xml']).find('assembly')
assert (failed.get('total'),failed.get('passed'),failed.get('failed'))==('60','58','2')
assert all(t.get('method')=='NativeOpaqueHistoryRemovalRefusesChangedBytesOrAnIdenticalReplacementAndRequiresFreshReview' and 'protected ACL' in t.findtext('./failure/message') for t in failed.findall('.//test') if t.get('result')=='Fail')
failed=E.fromstring(bundles['ubuntu','r1050']['render-r1050.xml']).find('assembly')
assert (failed.get('total'),failed.get('passed'),failed.get('failed'))==('456','455','1')
assert [t.get('method') for t in failed.findall('.//test') if t.get('result')=='Fail']==['IndexedSelectionTablePressKeepsItsCapturedMarkTargetUntilThePendingQueryCanPublish']
negative=archive(temp/'mac-batch-r1057-results.tgz'); a=E.fromstring(negative['preview-r1057.xml']).find('assembly')
assert (a.get('total'),a.get('passed'),a.get('failed'))==('5','4','1')
assert [t.get('method') for t in a.findall('.//test') if t.get('result')=='Fail']==['IndexedSelectionTablePressKeepsItsCapturedMarkTargetUntilThePendingQueryCanPublish']
assert negative['state.txt'].decode('utf-8-sig').strip()=='FINISHED exit=1 phase=preview'
assert not read(temp/'mac-batch-r1057-post.json')['filecatOrDotnet']
old_plan=read(temp/'test-fix-delta-mac-r1057/plan.json'); new_plan=inputs['mac']
assert {v['path'] for v in old_plan['files'] if v['patch']}=={'render/FileCat.Rendering.Tests.dll','render/FileCat.Rendering.Tests.pdb'}
assert all(next(v['sha256'] for v in old_plan['files'] if v['path']==name)==next(v['sha256'] for v in new_plan['files'] if v['path']==name) for name in ['render/FileCat.Rendering.Tests.dll','render/FileCat.Rendering.Tests.pdb'])
files.add('mac-batch-r1057-results.tgz'); drivers.add('batch-mac-r1057.sh')
for name,count in [('build-r1049.txt',4),('build-r1055.txt',2),('build-r1056.txt',1)]:
 text=(temp/name).read_text(encoding='utf-8-sig'); assert 'Build FAILED.' not in text
 assert all(len(re.findall(p,text,re.M))==count for p in [r'^Build succeeded\.$',r'^    0 Warning\(s\)$',r'^    0 Error\(s\)$']); files.add(name)
files.update(['source-candidate-r1050.json','source-candidate-r1057.json','source-candidate-r1058.json','pack-timing-r1050.json','app-settings-inputs-r1050.json','app-ui-delta-proof-r1057.json','app-ui-delta-proof-r1058.json'])
for name in files: shutil.copy2(temp/name,out/name)
for name in drivers: shutil.copy2(temp/name,out/'drivers'/name)
helpers=['finalize-history-lifecycle.py','prepare-history-workflow-batch.ps1','prepare-cached-app-ui-batch.ps1','prepare-test-fix-batch.ps1','run-batch-cache.py','add-app-settings-suite.ps1','add-unix-previews.ps1','add-windows-previews.ps1','deploy-batch.ps1','collect-batch-archive.ps1','preflight-indexed-r732.ps1']
flags={'idleStartupRetentionImplemented':True,'guardedOpaqueRemovalImplemented':True,'newLifecycleNativeHeadlessCasesPassedAllThree':22,'broadSuitesPassedAllThree':True,'tableGestureStatusLayoutFixed':True,'fullHistoryFaultResourceDesktopMatrixQualified':False,'publicFeatureRegistered':False,'finalIndependentReviewPerformed':False}
record={'purpose':'Startup/idle retention, guarded opaque history removal and stable table gesture geometry','observedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'gitBase':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),'sources':sources,'matrix':matrix,'inputs':inputs,'deltaProof':proof,'negativeControlProof':control_proof,'postRunProcesses':processes,'qualification':flags,'artifacts':[{'path':(out/n).relative_to(root).as_posix(),'sha256':sha(out/n)} for n in sorted(files)],'drivers':[{'path':(out/'drivers'/n).relative_to(root).as_posix(),'sha256':sha(out/'drivers'/n)} for n in sorted(drivers|set(helpers))],'scope':'Actual native filesystems with headless UI; physical input/startup, full fault/power-loss/resource/scale/AT/theme/DPI/package gates and final independent review remain open.'}
(out/report_name).write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
progress=read(out/'progress.json'); progress['validation']['historyLifecycle']={'manifest':report_name,'matrix':matrix,**flags}
progress['completedBehaviors'].append('Enabled history applies count/byte/age retention at startup, periodically while idle, after reviewed policy changes and after actual protected-work closure. Scoped exact native identity/SHA256 reviews remove empty, corrupt, truncated or unindexed copies; pending metadata uses the guarded transaction and opaque reviews cannot restore corrupt bytes. Twenty-two new native/headless cases and broad regressions pass all three (r1058). A reproduced status-layout change moving a pressed checkbox is fixed by retaining rendered status space through the gesture. Full history fault/resource/physical-desktop qualification remains open.')
progress['nextWork']=['W3: finish the full history fault/resource/scale and physical desktop lifecycle matrix, including concurrent root-close outcomes. Browsing/pinning, protected views, idle/startup retention, guarded damaged/unindexed removal and exact-graph recovery have native/headless coverage. Finish comparison replacement or evidenced unavailable behavior, general import/export, multiple-active/indexed-scale comparison/persistence and power-loss/theme/DPI/AT/package controls. The complete persistence gate remains unqualified.' if line.startswith('W3:') else line for line in progress['nextWork']]
(out/'progress.json').write_text(json.dumps(progress,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
(out/'HISTORY_LIFECYCLE_QUALIFICATION.md').write_text('''# History retention and damaged-copy removal

Enabled history now uses one bounded worker at application startup and every 30 minutes. Reviewed policy changes and actual closure of protected work wake it early; wakeups coalesce. Off/read-only policy creates no history store. Cancellation retains actual native ownership through completion and cleanup. Under the controller writer, every exact-object deletion review is prepared before automatic pruning starts. Count, bytes and age apply without another acquisition. Future timestamps, unresolved state and corrupt candidates preserve existing copies and defer automatic changes. Known removals remain reported if final cleanup fails.

A separate negative-default review removes only the exact reviewed private history object, with its name/location, actual size, SHA256 and possible loss of recovery. It makes no inventory-validity claim. Native identity, complete bounded bytes, profile/catalog state and active protections are rechecked. Cataloged/pending entries use existing metadata transactions; unindexed copies have no catalog record. Opaque reviews cannot restore corrupt bytes. Named analyses and source files stay outside this operation.

Twenty-two new cases pass on Windows, Mac and Ubuntu: separate idle caps; future/corrupt/unindexed deferral; startup protection and wake after closure; reviewed policy wake; disabled startup/preservation; empty/truncated indexed/unindexed deletion; changed-byte and identical-content replacement refusal plus fresh intent; narrow/large-text preserving reviews; active-view protection; and present/absent pending cleanup. Broad Core/native/rendering and Settings regressions pass with unchanged skip identities. The [r1058 manifest](source-manifest-r1058-lifecycle.json) records sources, binary inputs and exact counts. Raw reports are retained once per batch archive.

Windows initially stopped at 58/60 preflight cases: newly recreated replacement fixtures lacked the required protected ACL. The guard refused them correctly. The fixture now publishes replacement bytes through the native private store, preserving the ACL and proving identical content with a different native identity. Ubuntu's broad run exposed the existing pointer regression: status reflow moved the pressed checkbox 19 pixels, so release missed it. A controlled multiline-status test reproduces the failure on the unchanged product (four native replacement controls pass, pointer case fails). Its identical test DLL passes on the fixed build, which retains the status area's rendered height throughout the gesture and restores automatic layout afterwards. One misplaced intermediate test setup was corrected before using the controlled reproduction as evidence.

The verified UI delta changes only the App and rendering-test DLL/PDB pairs. Mac/Ubuntu Core results reuse identical verified inputs; their affected renderer and Settings suites rerun. Windows runs all broad suites because its earlier batch stopped at preflight. The old failures and negative control remain retained. All final task-process inventories are clear.

This is native filesystem and headless qualification. Physical desktop startup/input, the complete native failure/power-loss/resource/scale/AT/theme/DPI/package matrix and final independent UX/UI/functional/security review remain open. In particular, concurrent root-close deletion outcomes need a dedicated fault cohort. Public analyzer registration remains off.
''',encoding='utf-8')
print(json.dumps({'revision':revision,'sources':len(sources),'rawArchives':7,'newCasesPerPlatform':22,'renderPassed':{p:matrix[p+'/render']['passed'] for p in ['windows','mac','ubuntu']},'historyFullyQualified':False}))
