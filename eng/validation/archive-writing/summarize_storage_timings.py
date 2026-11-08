"""Summarize preserved guest logs/TRX files without executing product code or changing evidence."""
from pathlib import Path,PurePosixPath
from datetime import datetime
import argparse,hashlib,json,re,tarfile,xml.etree.ElementTree as ET

NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
FULL={'complete-core','complete-remote','complete-app'}

def seconds(value):
    parts=value.split(':')
    if len(parts)!=3:raise ValueError('Unexpected TRX duration')
    return int(parts[0])*3600+int(parts[1])*60+float(parts[2])

def summarize(path):
    path=Path(path);folder=path.name.removesuffix('-evidence.tar.gz');suites=[];builds=[];slow=[]
    with tarfile.open(path,'r:gz') as archive:
        for item in archive:
            name=PurePosixPath(item.name)
            if not item.isfile() or '..' in name.parts or name.is_absolute() or len(name.parts)<2 or name.parts[0]!=folder:continue
            trx=len(name.parts)==3 and name.parts[1]=='results' and name.suffix=='.trx'
            build=len(name.parts)==2 and name.name.endswith('-build.log')
            if not(trx or build):continue
            if item.size>128*1024*1024:raise ValueError('Oversized timing input: '+item.name)
            data=archive.extractfile(item).read()
            if build:
                found=re.search(r'^Time Elapsed (\d+:\d+:\d+(?:\.\d+)?)\s*$',data.decode('utf-8',errors='replace'),re.MULTILINE)
                if found:builds.append(dict(name=name.name,seconds=round(seconds(found[1]),3)))
                continue
            root=ET.fromstring(data);times=root.find('t:Times',NS);counters=root.find('.//t:Counters',NS)
            if times is None or counters is None:raise ValueError('Incomplete TRX: '+item.name)
            elapsed=(datetime.fromisoformat(times.get('finish'))-datetime.fromisoformat(times.get('start'))).total_seconds()
            if elapsed<0:raise ValueError('Negative suite timing: '+item.name)
            suites.append(dict(name=name.stem,seconds=round(elapsed,3),counters=dict(counters.attrib)))
            if name.stem in FULL:
                slow.extend(dict(suite=name.stem,name=row.get('testName'),outcome=row.get('outcome'),
                    seconds=round(seconds(row.get('duration','00:00:00')),3)) for row in root.findall('.//t:UnitTestResult',NS))
    with path.open('rb') as source:digest=hashlib.file_digest(source,'sha256').hexdigest()
    return dict(evidence_path=path.as_posix(),bytes=path.stat().st_size,sha256=digest,
        note='Observed wall times, not CPU time or proof of qualification. Test cases may overlap; do not sum individual case durations.',
        build_seconds=round(sum(row['seconds'] for row in builds),3),
        focused_suite_seconds=round(sum(row['seconds'] for row in suites if row['name'] not in FULL),3),
        full_suite_seconds=round(sum(row['seconds'] for row in suites if row['name'] in FULL),3),
        builds=sorted(builds,key=lambda row:row['name']),suites=sorted(suites,key=lambda row:row['name']),
        slowest_cases=sorted(slow,key=lambda row:row['seconds'],reverse=True)[:20])

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('evidence',nargs='+',type=Path)
    parser.add_argument('--output',type=Path);args=parser.parse_args()
    value=json.dumps(dict(schema=1,production_backend_qualified=False,reports=[summarize(path) for path in args.evidence]),indent=2)+'\n'
    if args.output:args.output.write_text(value,encoding='utf-8')
    else:print(value,end='')
