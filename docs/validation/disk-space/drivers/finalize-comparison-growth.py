import datetime, hashlib, json, pathlib, shutil, subprocess, xml.etree.ElementTree as ET

r = pathlib.Path.cwd(); t = r / '.tmp'; out = r / 'docs/validation/disk-space'
candidate = 'r840'; rendering = 'r841'; manifest = 'r842'
def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
sources = read(t/f'source-candidate-{candidate}.json')
assert all(sha(r/item['path']) == item['sha256'] for item in sources)
files = [f'build-{rev}.txt' for rev in ('r824','r826','r827','r831','r832','r835','r839')]
files += [f'source-candidate-{rev}.json' for rev in ('r828','r833','r836',candidate)]
files += [f'pack-timing-{rev}.json' for rev in ('r828','r833','r836',candidate)]
files += [f'unchanged-check-{rev}.json' for rev in ('r833','r836',candidate)]
matrices = {}; failures = {}; controls = {}; exits = {}
for rev, platforms in [('r828',('mac','ubuntu','windows')),('r833',('windows',)),('r836',('mac','ubuntu','windows')),(candidate,('mac','ubuntu','windows'))]:
    for platform in platforms:
        phases = ['core','render']
        if platform == 'windows': phases = ['smoke','core','native','render'] if rev == 'r828' else ['smoke','core','render']
        for phase in phases:
            stem=f'{platform}-batch-{rev}-{phase}-{rev}'; xml=t/(stem+'.xml')
            a=ET.parse(xml).getroot().find('assembly'); assert a.get('errors') == '0'
            key=f'{platform}/{rev}/{phase}'; matrices[key]={k:a.get(k) for k in ('total','passed','failed','skipped','errors','time')}
            failed=[test.get('name') for test in a.findall('.//test') if test.get('result') == 'Fail']; failures[key]=failed
            if rev == candidate:
                assert not failed, (key,failed)
                assert int(a.get('total')) == (274 if phase == 'render' else 6 if phase == 'smoke' else 2)
                if phase == 'render':
                    selected=[test for test in a.findall('.//test') if '.AnalysisRenderingTests.ComparisonMetric' in test.get('name','') or '.AnalysisRenderingTests.ComparisonChart' in test.get('name','')]
                    assert len(selected)==8 and all(test.get('result')=='Pass' for test in selected)
                    controls[key]=[test.get('name') for test in selected]
            elif rev == 'r836' or phase != 'render': assert not failed, (key,failed)
            else: assert len(failed)==2, (key,failed)
            files += [stem+'.xml',stem+'.log']
        state=f'{platform}-batch-{rev}-state.txt'; post=f'{platform}-batch-{rev}-post.json'
        assert (t/state).read_text(encoding='utf-8-sig').strip()==('FINISHED exit=0' if rev in (candidate,'r836') else 'FINISHED exit=1')
        processes=read(t/post)['filecatOrDotnet']; assert not any(f'batch-{rev}' in row for row in processes)
        exits[f'{platform}/{rev}']=processes; files += [state,post]
for platform in ('mac','ubuntu','windows'):
    files += [f'{platform}-pre-r825.json', f'{platform}-pre-r833.json',f'{platform}-pre-{candidate}.json']
    latest = candidate+'check1' if platform == 'ubuntu' else candidate
    assert not read(t/f'{platform}-pre-{latest}.json')['filecatOrDotnet']
files += ['mac-pre-r833check1.json','ubuntu-pre-r833check1.json','ubuntu-pre-r840check1.json']
proof=read(t/f'unchanged-check-{candidate}.json')
for item in proof: assert sha(r/item['current']) == sha(r/item['baseline']) == item['sha256']
native_test_baseline = t/'native-r828/FileCat.Platform.Windows.Tests.dll'
native_test_current = r/'tests/FileCat.Platform.Windows.Tests/bin/Debug/net10.0/FileCat.Platform.Windows.Tests.dll'
assert sha(native_test_baseline) == sha(native_test_current)
build=(t/'build-r839.txt').read_text(encoding='utf-8-sig')
assert all(value in build for value in ('Build succeeded.','0 Warning(s)','0 Error(s)'))
binary={}
for name in ('FileCat.Core.dll','FileCat.dll','FileCat.Platform.Windows.dll'):
    paths=[t/f'render-{rendering}-{platform}'/name for platform in ('mac','linux','windows')]
    binary[name]=[sha(path) for path in paths]; assert len(set(binary[name])) == 1
drivers=['preflight-indexed-r732.ps1','deploy-batch.ps1','collect-batch-archive.ps1','pack-batch-direct.ps1','finalize-comparison-growth.py']
for rev, platforms in [('r828',('mac','linux','windows')),('r833',('windows',)),('r836',('mac','linux','windows')),(candidate,('mac','linux','windows'))]:
    drivers += [f'batch-{platform}-{rev}.{"ps1" if platform=="windows" else "sh"}' for platform in platforms]
drivers += [f'prepare-comparison-{rev}.ps1' for rev in ('r828','r833','r836',candidate)]
for file in files: shutil.copy2(t/file,out/file)
for file in drivers:
    if (t/file).exists(): shutil.copy2(t/file,out/'drivers'/file)
archives=[]; screenshots=[]
for platform in ('mac','ubuntu','windows'):
    for rev in ('r828','r836',candidate) if platform!='windows' else ('r828','r833','r836',candidate):
        file=f'{platform}-batch-{rev}-results.tgz'; shutil.copy2(t/file,out/file); files.append(file)
    folder=t/f'{platform}-batch-{candidate}-results/payload/render/comparison-growth-renders'
    assert len(list(folder.glob('*.png'))) == 28
    for image in ('Classic-1180-1.png','HighContrast-380-2.png'):
        name=f'comparison-growth-{candidate}-{platform}-{image}'; shutil.copy2(folder/image,out/name); screenshots.append(name); files.append(name)
    package='linux' if platform=='ubuntu' else platform
    archives.append({'platform':platform,'name':f'dsa-batch-{package}-{candidate}.tgz','sha256':sha(t/f'dsa-batch-{package}-{candidate}.tgz')})
qualification={'newComparisonControlsPassedAllThree':True,'headlessRenderingAndNativePrivateCatalogSubset':True,
 'allCurrentRenderingSuitesPassed':True,'coreAndWindowsNativeBaselineReusedWithIdenticalProductionHashes':True,
 'fullNativeDesktopComparisonQualified':False,'indexedComparisonQualified':False,'savedComparisonQualified':False,
 'fullResourceGateQualified':False,'publicFeatureRegistered':False,'finalIndependentReviewPerformed':False}
record={'purpose':'Offline comparison metric switching, current/previous growth charts and compact layout; mandatory W0-W8 work remains open',
 'observedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'gitBase':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
 'sources':sources,'testedWorkingTreeChanges':True,'matrix':matrices,'supersededFailures':failures,'newControls':controls,
 'artifacts':[{'path':(out/name).relative_to(r).as_posix(),'sha256':sha(out/name)} for name in dict.fromkeys(files)],
 'drivers':[{'path':(out/'drivers'/name).relative_to(r).as_posix(),'sha256':sha(out/'drivers'/name)} for name in dict.fromkeys(drivers)],
 'archives':archives,'productionHashes':binary,'unchangedProductionProof':proof,'nativeTestAssemblyReuseProof':{'baseline':native_test_baseline.relative_to(r).as_posix(),'current':native_test_current.relative_to(r).as_posix(),'sha256':sha(native_test_current)},'postCollectionProcesses':exits,
 'newCoreControls':2,'newRenderingControls':8,'screenshots':screenshots,'qualification':qualification,
 'scope':'Actual ordinary guest runtimes, native private saved inventories and headless window/input controls; seven themes at1180 and380/20-DIP text. Full physical desktop, all source/provider, indexed-scale, saved comparison, resource and security qualification remain pending.',
 'workflow':'One immutable bundle/platform; initial Windows smoke before broad suites. Final six-case Windows preflight, two changed Core controls and full274-case renderers. Complete Core and Windows75-case native suites are reused from r828 only after production Core/Windows-platform SHA equality. One completed result archive/platform; busy Mac/Ubuntu were postponed; no host application tests.'}
(out/f'source-manifest-{manifest}.json').write_text(json.dumps(record,indent=2,ensure_ascii=False)+'\n',encoding='utf-8',newline='\n')
summary=f'''Offline comparisons now switch among all four metrics using their retained captured inputs. Changing metric retires table/query/chart readers and waits for actual result-budget release before rebuilding; cancellation retains inputs for explicit retry. Structural root mappings survive the switch. Current and Previous Treemap/Sunburst charts use the same captured positive-area hierarchy, preserving shared charges and unknown amounts. Growth coloring has text/symbol legends, attribution and unknown/mixed states; removed entries have area only on Previous. Table focus follows accounting rows, chart-only selection does not silently clear table filters, and historical inspection supplies no file action authority.

Comparison details and query controls are collapsible; compatibility/structural context remains named in the collapsed header. Explicit chart/table reveal controls support narrow windows. Every chart retains its recorded side and metric inside its drawing, and comparison Sunburst values include units. Seven themes and both chart modes render at1180 and380 with20-DIP text. Broad ordinary-color controls explicitly cover the three non-comparison choices; saved ordinary views reject Growth. Eight new rendering controls and two new Core controls pass on all three guest runtimes. The final renderers have274 cases each (Windows273 pass/1 platform skip; Mac/Ubuntu274 pass). [Provenance](source-manifest-{manifest}.json).

Superseded r828 ordinary-color iteration failures and r833 collapsed-control test failures remain recorded. The r836 automated suites passed, but screenshot review found crowded Sunburst center labels at large text sizes. The final center uses measured line heights, an adaptive bounded hole and matching draw/hit boundaries; labels fit inside the hole without crossing rings, and font geometry remains frozen through pointer release. Its final affected suites and screenshots were rechecked. Native private stores and headless controls are actual guest evidence, not full physical desktop qualification. Source files were not connected by comparison/chart work. Full indexed comparison, saved comparison/export, resource/scale/native desktop/package qualification, remaining W0–W8 packets and the final independent UX/UI/functional/security review remain open; public and other unqualified gates remain off.

The workflow now uses a short Windows preflight, identical-production hash checks to reuse unchanged Core/native qualification, focused changed-Core controls, full affected rendering suites, three direct deployment archives and one result archive/completed machine. Mac/Ubuntu archive-test occupancy was detected and postponed. Every owned original runner and collector is terminal; post inventories contain no owned candidate instance.'''
for path in (out/'README.md',r/'docs/design/FILECAT_DISK_SPACE_EXPLORATION.md'):
    text=summary if path.parent==out else summary.replace(f'(source-manifest-{manifest}.json)',f'(../validation/disk-space/source-manifest-{manifest}.json)')
    old=path.read_text(encoding='utf-8')
    if text not in old: path.write_text(old.rstrip()+'\n\n'+text+'\n',encoding='utf-8',newline='\n')
progress=read(out/'progress.json'); behavior='Offline comparison metric switching and positive-area Current/Previous growth charts retain actual readers, shared charges, unknown/attribution distinctions and explicit structural mappings. Compact disclosures, persistent chart side/metric captions and reveal navigation pass eight new rendering controls all three guest runtimes; full W0-W8 and physical desktop qualification remain open.'
if behavior not in progress['completedBehaviors']: progress['completedBehaviors'].append(behavior)
progress['validation']['comparisonGrowth']={'manifest':f'source-manifest-{manifest}.json','matrix':matrices,'newCoreControls':2,'newRenderingControls':8,**qualification}
progress['validation']['testingWorkflow']['batchEvidence']=f'source-manifest-{manifest}.json'
(out/'progress.json').write_text(json.dumps(progress,indent=2,ensure_ascii=False)+'\n',encoding='utf-8',newline='\n')
print(json.dumps({'manifest':manifest,'sources':len(sources),'artifacts':len(set(files)),'drivers':len(set(drivers)),'matrix':matrices},indent=2))
