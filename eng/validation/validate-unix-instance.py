#!/usr/bin/env python3
"""Run real instance/recovery tests in fresh processes with Unix socket-boundary TMPDIRs."""
import argparse
from collections import Counter
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import tempfile
import uuid
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--test-dll', required=True, type=pathlib.Path)
    parser.add_argument('--evidence-dir', required=True, type=pathlib.Path)
    args = parser.parse_args()
    if os.name != 'posix' or args.evidence_dir.exists():
        parser.error('Requires Unix and a new evidence directory')
    dll = args.test_dll.resolve(strict=True)
    args.evidence_dir.mkdir(mode=0o700)
    scratch = pathlib.Path(tempfile.mkdtemp(prefix='filecat-instance-', dir='/tmp'))
    (scratch / '.filecat-owned').write_text(uuid.uuid4().hex)
    edge = scratch / ('edge-' + 'a' * (72 - len(os.fsencode(str(scratch))) - 1 - 5))
    deep = scratch / ('long-' + 'a' * 160)
    unicode = scratch / ('unicode-' + 'ž' * 60)
    manifest = {'test_dll': str(dll), 'sha256': hashlib.sha256(dll.read_bytes()).hexdigest(),
                'scratch': str(scratch), 'results': []}
    required = {'Data_names_a_folder_of_its_own_and_another_instance',
                'Profile_names_that_name_one_folder_are_one_instance',
                'A_disk_that_holds_FileCats_own_files_is_not_scanned_and_the_safe_way_is_given',
                'An_instance_connection_outside_the_temporary_folder_is_guarded_before_scanning',
                'A_running_usual_instances_connection_is_guarded_and_an_unknown_location_is_refused',
                'The_runtime_temporary_folder_is_guarded_before_scanning',
                'An_independent_instance_is_visible_until_release_and_stale_files_are_ignored',
                'A_usual_instance_with_another_profile_is_guarded_before_scanning',
                'Case_aliases_of_one_Windows_profile_find_the_running_instance',
                'The_process_census_ignores_itself_detects_a_live_process_and_refuses_an_unavailable_inventory',
                'Device_recovery_waits_for_other_FileCat_processes_even_with_its_own_folders_elsewhere',
                'A_portable_recovery_finds_the_per_user_owner_and_independent_windows',
                'The_usual_profile_catalog_includes_portable_profiles_and_the_distinct_DEFAULT_profile'}
    expected_cases = {method: (3 if method == 'Device_recovery_waits_for_other_FileCat_processes_even_with_its_own_folders_elsewhere' else 2 if method in {
        'A_portable_recovery_finds_the_per_user_owner_and_independent_windows',
        'The_usual_profile_catalog_includes_portable_profiles_and_the_distinct_DEFAULT_profile'} else 1)
        for method in required}
    for label, temp in [('edge', edge), ('long', deep), ('unicode', unicode)]:
        temp.mkdir(mode=0o700)
        xml = args.evidence_dir / f'{label}.xml'
        result = {'case': label, 'temp': str(temp), 'temp_bytes': len(os.fsencode(str(temp))) + 1,
                  'passed': False}
        env = {**os.environ, 'TMPDIR': str(temp), 'DOTNET_EnableDiagnostics': '0'}
        try:
            # Direct xUnit executable: the SDK/test-platform's own pipe paths cannot obscure FileCat's behavior.
            run = subprocess.run([shutil.which('dotnet') or 'dotnet', str(dll), '-class',
                                  'FileCat.App.Tests.RecoverySafetyTests', '-xml', str(xml)],
                                 env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
            (args.evidence_dir / f'{label}.log').write_bytes(run.stdout)
            result['exit'] = run.returncode
            tests = ET.parse(xml).findall('.//test')
            result['tests'] = [{'name': test.get('method'), 'result': test.get('result'),
                                'output': ' '.join(test.itertext()).strip()} for test in tests]
            if Counter(test.get('method') for test in tests) != expected_cases:
                raise RuntimeError('Required instance/recovery tests were not all executed')
            expected_skips = 1 + (1 if label == 'edge' else 0) # Windows identity case plus the optional Unix fallback.
            if run.returncode or any(test.get('result') == 'Fail' for test in tests):
                raise RuntimeError('Native instance/recovery tests failed')
            if sum(test.get('result') == 'Skip' for test in tests) != expected_skips:
                raise RuntimeError('Unexpected prerequisite skip')
            if sum(test.get('result') == 'Pass' for test in tests) != sum(expected_cases.values()) - expected_skips:
                raise RuntimeError('Unexpected native test outcomes')
            result['passed'] = True
        except Exception as error:
            result['error'] = str(error)
        manifest['results'].append(result)
        print(json.dumps(result, ensure_ascii=False), flush=True)
    (args.evidence_dir / 'results.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2))
    # Retain every fixture, including failures, with the case evidence.
    shutil.make_archive(str(args.evidence_dir / 'fixtures'), 'gztar', scratch.parent, scratch.name)
    return 0 if all(result['passed'] for result in manifest['results']) else 1


if __name__ == '__main__':
    raise SystemExit(main())
