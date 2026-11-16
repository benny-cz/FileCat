from pathlib import Path
import hashlib,importlib.util,json,sys,tarfile,xml.etree.ElementTree as E

root,out,task=map(Path,sys.argv[1:])
# The private Windows embedded Python uses an isolated ._pth. Load this exact packet's helper explicitly.
spec=importlib.util.spec_from_file_location('filecat_lzma_end_sdk',root/'eng/validation/archive-writing/sevenzip-feasibility/verify_lzma_end_sdk.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
tests=E.parse(out/'results/archive-worker-secrets.trx').findall('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult')
assert len(tests)==1138 and not any(t.attrib['outcome']=='Failed' for t in tests)
assert sum('.StrictLzmaStreamTests.' in t.attrib['testName'] and t.attrib['outcome']=='Passed' for t in tests)==88
tools=out/'tools';tools.mkdir();packet=task/'lzma-end-tools-v1.tar.gz'
assert hashlib.sha256(packet.read_bytes()).hexdigest()=='aa7ee1ff6f335e52a8bcee38d04e645112957b83298ca5994e03a1654f76e2ee'
with tarfile.open(packet,'r:gz') as tar:tar.extractall(tools,filter='data')
for row in json.loads((tools/'files.json').read_text())['files']:
 p=tools/row['path'];assert p.stat().st_size==row['bytes'] and hashlib.sha256(p.read_bytes()).hexdigest()==row['sha256']
module.run(out/'vectors',tools/'sdk-lzma-end-oracle.exe',out/'sdk-comparison')
