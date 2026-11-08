"""Static snapshot checkpoint verification; no product, decoder, or test execution on the host."""
from pathlib import Path
import importlib.util,json

ROOT=Path(__file__).resolve().parents[3]
spec=importlib.util.spec_from_file_location('snapshot_storage',Path(__file__).with_name('verify_edit_storage_checkpoint.py'))
storage=importlib.util.module_from_spec(spec);spec.loader.exec_module(storage)

def verify(platform):
    failed=storage.verify(platform,'awe-storage-v6');current=storage.verify(platform,'awe-storage-v7')
    assert failed['tests']['native-snapshot']==({'Passed':9,'Failed':17} if platform=='windows' else {'Passed':10,'Failed':16})
    _,old_index,_=storage.base.transport(ROOT/'.tmp/storage-evidence'/platform/'awe-storage-v6-evidence.tar.gz','awe-storage-v6')
    _,new_index,_=storage.base.transport(ROOT/'.tmp/storage-evidence'/platform/'awe-storage-v7-evidence.tar.gz','awe-storage-v7')
    product=[]
    for name,row in new_index.items():
        if not name.startswith('binaries/') or '.Tests.' in name or not name.endswith(('.dll','.pdb')):continue
        assert row==old_index[name],name;product.append(name)
    assert product and current['focused_gates_complete']
    return dict(platform=platform,history=failed,current=current,unchanged_product_binaries=product,
        product_bytes_unchanged_after_fixture_corrections=True)

if __name__=='__main__':
    checkpoint=json.loads((ROOT/'docs/validation/archive-writing/edit-snapshots-checkpoint.json').read_text())
    assert checkpoint['schema']==1 and not checkpoint['production_backend_qualified'] and not checkpoint['application_composition_uses_native_store']
    for row in checkpoint['platforms']:assert verify(row['platform'])==row,row['platform']
    rows=[row[role] for row in checkpoint['platforms'] for role in ('history','current')]
    print(json.dumps(dict(bundles_verified=len(rows),bytes_verified=sum(row['bytes']for row in rows),production_backend_qualified=False)))
