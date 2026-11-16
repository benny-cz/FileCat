from pathlib import Path
import hashlib,json,subprocess,sys

root,out,task=map(Path,sys.argv[1:]);windows=sys.platform=='win32';mac=sys.platform=='darwin';which='windows' if windows else 'mac' if mac else 'ubuntu'
helpers=root/'eng/validation/archive-writing/sevenzip-feasibility';tools=task/'password-oracle-tools-v1'
def execute(name,args):
 with (out/(name+'.log')).open('xb') as log:subprocess.run(args,cwd=root,stdout=log,stderr=subprocess.STDOUT,check=True,timeout=1800)
execute('reader-module-reuse',[sys.executable,'-B',str(helpers/'verify_oracle_module_reuse.py'),str(tools),which])
tool=tools/'7zip/7z.exe' if windows else task/'sevenzip-ppmd-filter-v2/tools'/('mac' if mac else 'linux')/'7zz'
expected='94205cfc054dd3bbeb978905bc619a65b3f04e9e73bb2717841110b6f46d962e' if windows else json.loads((tools/'official-tool-identities.json').read_text(encoding='utf-8-sig'))[which]['sha256']
assert hashlib.sha256(tool.read_bytes()).hexdigest().lower()==expected.lower()
execute('preservation-independent',[sys.executable,'-B',str(root/'eng/validation/archive-writing/rar5-independent-probe/verify_preservation.py'),str(out/'vectors'),str(tool),str(tools/'modules')])
