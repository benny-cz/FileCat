import datetime, hashlib, json, pathlib, shutil, subprocess, xml.etree.ElementTree as ET

r=pathlib.Path.cwd();t=r/'.tmp';out=r/'docs/validation/disk-space';manifest='r853'
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
sources=read(t/'source-candidate-r852.json');assert all(sha(r/item['path'])==item['sha256']for item in sources)
files=['build-r843.txt','build-r845.txt','build-r848.txt','build-r851.txt','format-r844.txt','format-folder-r844.txt',
 'source-candidate-r846.json','source-candidate-r849.json','source-candidate-r852.json','pack-timing-r846.json','pack-timing-r849.json','delta-proof-r849.json','focused-delta-proof-r852.json',
 'windows-batch-r846-observed.txt','windows-batch-r846-resumed-observed.txt','windows-batch-r846-state.txt','windows-batch-r846-post.json','windows-batch-r846-smoke-r846.log','windows-batch-r846-results.tgz']
assert (t/'windows-batch-r846-state.txt').read_text(encoding='utf-8-sig').strip()=='FINISHED exit=1 phase=smoke'
failed_log=(t/'windows-batch-r846-smoke-r846.log').read_text(encoding='utf-16');assert 'Stack overflow' in failed_log and 'FragmentedComparisonStream' in failed_log
assert not (t/'windows-batch-r846-smoke-r846.xml').exists()
matrix={};controls={};post={};cache={}
for candidate in ('r849','r852'):
 for platform in ('windows','mac','ubuntu'):
  phases=('smoke','preview','core','native','render')if platform=='windows'and candidate=='r849'else('core','render')if candidate=='r849'else('render',)
  for phase in phases:
   stem=f'{platform}-batch-{candidate}-{phase}-{candidate}';a=ET.parse(t/(stem+'.xml')).getroot().find('assembly')
   assert a.get('failed')==a.get('errors')=='0',(stem,a.attrib)
   if candidate=='r852': expected=1
   elif phase=='core': expected=1400 if platform=='windows' else 1382
   else: expected={'smoke':20,'preview':7,'native':75,'render':281}[phase]
   assert int(a.get('total'))==expected,(stem,a.attrib)
   key=f'{platform}/{candidate}/{phase}';matrix[key]={k:a.get(k)for k in ('total','passed','failed','skipped','errors','time')}
   if phase in ('smoke','preview')or candidate=='r852':assert a.get('passed')==a.get('total')
   if candidate=='r849'and phase=='render':
    selected=[test for test in a.findall('.//test')if any(value in test.get('name','')for value in ('SavedComparisonNativeInputsRoundtrip','ComparisonSaveCaptureOwns','ComparisonSavingRefuses','ComparisonSaveAndOpenDialogsUse'))]
    assert len(selected)==7 and all(test.get('result')=='Pass'for test in selected);controls[key]=[test.get('name')for test in selected]
   files.extend((stem+'.xml',stem+'.log'))
  state=f'{platform}-batch-{candidate}-state.txt';assert(t/state).read_text(encoding='utf-8-sig').strip()=='FINISHED exit=0'
  post_file=f'{platform}-batch-{candidate}-post.json';processes=read(t/post_file)['filecatOrDotnet'];assert not any(f'batch-{candidate}'in row for row in processes)
  post[f'{platform}/{candidate}']=processes;files.extend((state,post_file,f'{platform}-batch-{candidate}-results.tgz'))
  if candidate=='r852'or platform=='windows':
   proof=f'{platform}-batch-{candidate}-cache-proof.txt';data=(t/proof).read_text(encoding='utf-8-sig').strip();assert data.startswith('VERIFIED cachedFiles=')and data.endswith('changedFiles=2');cache[f'{platform}/{candidate}']=data;files.append(proof)
for platform in ('windows','mac','ubuntu'):
 for revision in ('r846','r846check1','r852'):files.append(f'{platform}-pre-{revision}.json')
 assert not read(t/f'{platform}-pre-r852.json')['filecatOrDotnet']
files.append('windows-pre-r849.json')
for revision in ('r845','r848','r851'):
 text=(t/f'build-{revision}.txt').read_text(encoding='utf-8-sig');assert all(value in text for value in ('Build succeeded.','0 Warning(s)','0 Error(s)'))
drivers=['preflight-indexed-r732.ps1','deploy-batch.ps1','collect-batch-archive.ps1','pack-batch-direct.ps1','prepare-focused-test-delta.ps1','run-focused-test-delta.ps1','run-focused-test-delta.py','finalize-saved-comparison.py',
 'prepare-comparison-save-r846.ps1','prepare-comparison-save-r849.ps1','pack-full-comparison-save-r849.ps1']
for candidate,platforms in [('r846',('windows',)),('r849',('windows','mac','linux')),('r852',('windows','mac','linux'))]:
 drivers.extend(f'batch-{platform}-{candidate}.{"ps1"if platform=="windows"else"sh"}'for platform in platforms)
for name in dict.fromkeys(files):shutil.copy2(t/name,out/name)
for name in dict.fromkeys(drivers):
 if(t/name).exists():shutil.copy2(t/name,out/'drivers'/name)
production={}
for name in ('FileCat.Core.dll','FileCat.dll','FileCat.Platform.Windows.dll'):
 old=t/'batch-r849-windows/render'/name;current=r/'tests/FileCat.Rendering.Tests/bin/Debug/net10.0'/name
 assert sha(old)==sha(current);production[name]=sha(current)
archives=[]
for candidate in ('r849','r852'):
 for platform in ('windows','mac','linux'):
  name=f'dsa-batch-{platform}-{candidate}.tgz';archives.append({'name':name,'sha256':sha(t/name),'bytes':(t/name).stat().st_size})
qualification={'savedComparisonMemoryAndNativePrivateSubsetPassedAllThree':True,'appliedViewAndDraftCloseControlPassedAllThree':True,
 'allApplicableBroadSuitesPassed':True,'testOnlyFocusedDeltasPreservedProductionHashes':True,'fullNativeDesktopQualified':False,
 'indexedSavedComparisonQualified':False,'managedReplacementDeletionHistoryQualified':False,'generalExportImportQualified':False,
 'fullResourceGateQualified':False,'publicFeatureRegistered':False,'finalIndependentReviewPerformed':False}
record={'purpose':'Atomic private saved comparisons, exact captured inputs, applied query/chart/focus restoration and preserving draft closure; full W0-W8 remains open',
 'observedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'gitBase':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),'testedWorkingTreeChanges':True,
 'sources':sources,'artifacts':[{'path':(out/name).relative_to(r).as_posix(),'sha256':sha(out/name)}for name in dict.fromkeys(files)],
 'drivers':[{'path':(out/'drivers'/name).relative_to(r).as_posix(),'sha256':sha(out/'drivers'/name)}for name in dict.fromkeys(drivers)],'archives':archives,
 'matrix':matrix,'newRenderingControls':controls,'newCoreControls':20,'newRenderingCases':7,'focusedStrengthenedUiCasesPerPlatform':1,
 'productionHashesUnchangedForFocusedUi':production,'verifiedCacheProofs':cache,'postCollectionProcesses':post,'qualification':qualification,
 'supersededFailure':{'candidate':'r846','terminalState':'FINISHED exit=1 phase=smoke','xmlAbsentDueToProcessFailure':True,
  'cause':'Fragmented cancellation test helper derived MemoryStream and recursively forwarded Span/array reads. Fixed by composition; product code unchanged.',
  'busyAttemptBeforeFailure':'Initial attempt stopped BUSY phase=verify. After confirmed free, same staged bundle resumed without retransferring.'},
 'scope':'Ordinary guest runtimes, actual generated native filename captures and native private stores, managed parser/admission controls and headless window/control workflows. Full physical desktop/a11y/package/indexed-scale/general export/history/maintenance qualification remains open.',
 'workflow':'One coherent feature batch/platform; short Windows codec/native-UI preflight before broad suites. Helper-only Core rebuild reused226 verified Windows inputs with2.4MB delta. Stronger UI test reused109/105/104 verified inputs with375532/376544/376499-byte deltas and one selected case/platform. One archive per completed machine; all original runner/collector handles terminal; no host application tests.'}
(out/f'source-manifest-{manifest}.json').write_text(json.dumps(record,indent=2,ensure_ascii=False)+'\n',encoding='utf-8',newline='\n')
summary=f'''Named comparisons now save both entire captured inventories and the applied metric, cohort, sort, query/case/side/uncertain cohort, chart side/scope and focus in one private create-new `.fcdsc` bundle. This private schema is separate from the unchanged portable `.fcdsa` inventory format: it embeds two exact ordinary inventory streams with bounded metadata, explicit byte boundaries, input UUID checks, their existing trailers and one outer SHA-256/EOF check. No output-sized buffer or separately published input file is needed. Cancellation/failure before publication leaves no half bundle; existing names are preserved. Checksums detect corruption rather than authenticate origin. Both imported inputs remain inert/unverified and preserve joined privileged taint.

Original root display IDs are retained as bounded private query context while the embedded inventory namespace remains numeric; structural mappings are translated exactly and root-ID predicates retain their meaning on reopen and second save. No locator, source connection or action/review authority is restored. Model captures retain actual readers and snapshots across dialogs and queued work. Save is unavailable during metric changes and pending/unapplied chart choices. Only applied query state is saved; unapplied edits remain temporary work and keep the preserving close prompt. Root Up is disabled when already at all locations.

Save/Open comparison controls use the existing private native directory and admitted I/O route. Save names its private destination and privileged classification with Cancel default. Opening validates/recomputes both inputs, reapplies the query, and checks focus/chart scope before publishing a new tab. The names browser is bounded and filenames cannot select arbitrary profile/source artifacts. Managed replacement/deletion/history and general external import/export remain separate required work.

Twenty new codec/publication/cancellation/name controls and seven new rendering/native-store cases pass on Windows, Mac and Ubuntu. Native cases preserve generated source payloads and exact raw names across all four metrics. Broad suites pass: Core Windows1352 pass/48 platform skips, Mac/Ubuntu1378 pass/4 platform skips; Windows native75 pass; renderer Windows280 pass/1 Unix-only skip and Mac/Ubuntu281 pass. A strengthened UI journey then passes on each platform, restoring non-default query/case/side/sort/chart/focus and preserving unapplied-draft closure. [Provenance](source-manifest-{manifest}.json).

The Windows preflight first caught a stack overflow in the test's fragmented-stream helper, fixed by composing a concrete MemoryStream. Its failed log and absent XML are preserved. The helper-only rebuild reused226 verified Windows files and transferred only the two changed test files in a2.4MB bundle. The final test-only UI reruns reused unchanged production inputs with under0.4MB per platform and one focused case (2.1–2.6seconds), avoiding repeated broad suites. All owned processes exited and result collection used one archive/completed machine.

These are actual guest/native private-store and headless control subsets, not complete physical desktop, indexed-scale or whole-process resource qualification. Remaining W0–W8 work and the final fresh independent UX/UI/functional/security review remain open. Public and other unqualified capability gates stay off.'''
for path in (out/'README.md',r/'docs/design/FILECAT_DISK_SPACE_EXPLORATION.md'):
 text=summary if path.parent==out else summary.replace(f'(source-manifest-{manifest}.json)',f'(../validation/disk-space/source-manifest-{manifest}.json)')
 old=path.read_text(encoding='utf-8')
 if text not in old:path.write_text(old.rstrip()+'\n\n'+text+'\n',encoding='utf-8',newline='\n')
progress=read(out/'progress.json');behavior='Atomic private .fcdsc comparisons restore both inert captured inputs and applied metric/query/chart/focus state, preserve root-ID query meaning and privileged taint, refuse existing names and retain unapplied-draft close protection.20 new Core and7 rendering/native-store cases plus a strengthened UI journey pass all three guest runtimes; full W0-W8 qualification remains open.'
if behavior not in progress['completedBehaviors']:progress['completedBehaviors'].append(behavior)
progress['validation']['savedComparison']={'manifest':f'source-manifest-{manifest}.json','matrix':matrix,'newCoreControls':20,'newRenderingCases':7,**qualification}
progress['validation']['testingWorkflow']['batchEvidence']=f'source-manifest-{manifest}.json'
progress['validation']['testingWorkflow']['testDeltaPolicy']='Copy only declared SHA-verified cached inputs to a fresh payload; overlay only test DLL/PDB; reject production changes; one archive/result collection; affected method selectors.'
for index,item in enumerate(progress['nextWork']):
 if item.startswith('W3:'):progress['nextWork'][index]='W3: complete managed catalog maintenance/replacement/deletion, optional capped history, general import/export and indexed-scale comparison/persistence, plus full native desktop qualification. Named private comparisons now capture both inputs and applied view with strict root-ID query context and preserving draft closure, tested across all3 guest runtimes. The persistence gate remains unqualified; ordinary saved root-ID query context and all remaining plan requirements need audit.'
(out/'progress.json').write_text(json.dumps(progress,indent=2,ensure_ascii=False)+'\n',encoding='utf-8',newline='\n')
print(json.dumps({'manifest':manifest,'sources':len(sources),'artifacts':len(set(files)),'drivers':len(set(drivers)),'matrix':matrix},indent=2))
