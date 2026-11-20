import json,pathlib,re,xml.etree.ElementTree as E
root=pathlib.Path('.tmp');post=json.loads((root/'windows-post-staging-r492.json').read_text(encoding='utf-8-sig'))
users={E.parse(root/f'windows-{kind}-{rev}.xml').getroot().get('user','').lower() for kind,rev in [('core','r490'),('native','r490'),('render','r491')]}
assert users=={'filecatdsa_r2'},users
owned=[];unknown=[];other=[]
for row in post['filecatOrDotnet']:
 match=re.search(r'owner=([^,]*),',row);owner=match.group(1).strip() if match else ''
 if not owner:unknown.append(row)
 elif owner.split('\\')[-1].lower()=='filecatdsa_r2':owned.append(row)
 else:other.append(row)
result={'observedUtc':post['observedUtc'],'exitCode':post['exitCode'],'source':'VMware Tools process inventory plus actual test-result account fields','expectedTestAccount':'filecatdsa_r2','testResultAccounts':sorted(users),'ownedTestAccountProcesses':owned,'unknownOwners':unknown,'otherAccountProcesses':other,'otherProcessesPreserved':True}
assert not owned and not unknown,result
(root/'windows-staging-process-identities-r492.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
