import hashlib,io,json,pathlib,tarfile
base=pathlib.Path.cwd()
rows=json.loads((base/'.tmp/source-candidate-r1142.json').read_text(encoding='utf-8-sig'))
fixture='tests/FileCat.Core.Tests/AnalysisDatasetSummaryTests.IndexedComparisonQuery.cs'
with tarfile.open(base/'.tmp/source-r1141-indexed-comparison-query.tgz') as original:
    data={m.name:original.extractfile(m).read() for m in original.getmembers()}
text=data[fixture].decode('utf-8')
lines=[]
for line in text.splitlines(keepends=True):
    if 'await Assert.' in line and '() => result.QueryAsync(' in line:
        line=line.replace('() => result.QueryAsync(', '() => DiscardUnexpectedQueryAsync(result.QueryAsync(')
        line=line.rstrip('\n')[:-1]+');\n'
        line=line.replace('Metric = AnalysisMetric.UniqueFileSize, Query = "path=A/*"', 'Metric = AnalysisMetric.UniqueFileSize, Query = "name=x.bin"')
    line=line.replace('() => running);', '() => DiscardUnexpectedQueryAsync(running));')
    lines.append(line)
text=''.join(lines)
at=text.rfind('\n}')
text=text[:at]+'\n    private static async Task DiscardUnexpectedQueryAsync(Task<AnalysisIndexedComparison.Query> pending)\n    { await using var unexpected = await pending; }'+text[at:]
data[fixture]=text.encode('utf-8')
assert set(data)=={r['path'] for r in rows}
assert all(hashlib.sha256(data[r['path']]).hexdigest()==r['sha256'] for r in rows)
target=base/'.tmp/source-r1142-indexed-comparison-query.tgz'
assert not target.exists()
with tarfile.open(target,'w:gz') as archive:
    for row in rows:
        payload=data[row['path']]; member=tarfile.TarInfo(row['path']);member.size=len(payload)
        archive.addfile(member,io.BytesIO(payload))
print('Preserved exact prior sources:',len(rows))
