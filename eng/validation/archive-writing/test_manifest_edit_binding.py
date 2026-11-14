"""What the release manifest must refuse for a listed edit-storage route: a label with nothing behind it.

`NativeEditStorage.Qualified` lists a route only with its own evidence (`AWQ-<rid>-<fs>/edit`), and the manifest refuses
a listed route whose evidence it does not carry. That refusal alone is satisfied by any file under the right ID: a
reviewer wrote a manifest whose three edit routes were satisfied by a 32-byte text file, and another whose
`.../edit/check` report said in its own words that the archive was NOT edit-storage evidence. The listing of `a4606689`
shows what it permits in practice - its three `.../edit` entries are byte-identical to its `.../full` entries, the
archive lane's own archive under a second label.

So `check_edit_storage_evidence.py` names the archive it accepted by digest, and the manifest requires the report
carried under a route's own `.../check` to accept the very archive carried under the route's ID. These cases are that
requirement: what it accepts, and the five ways of satisfying the old refusal that it now turns down.

Run by hand, as its sibling is, from the checkout's root (isolated Python keeps the working folder off the path, so
discovery is what puts this folder on it):

    python -I -X utf8 -m unittest discover -s eng/validation/archive-writing -p "test_manifest_edit_binding.py" -v

It builds the manifest tool from the checkout it lives in - the tool refuses to describe a commit its own build does
not come from - so it needs the .NET SDK and about a minute the first time.
"""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
PROJECT = ROOT / 'eng' / 'ArchiveReleaseManifest' / 'ArchiveReleaseManifest.csproj'
# The three routes the build lists; the tool names the ID it refuses, so a changed list fails here by name.
ROUTES = ('AWQ-win-x64-NTFS', 'AWQ-linux-x64-ext4', 'AWQ-osx-arm64-apfs')


def load(name):
    specification = importlib.util.spec_from_file_location(name, HERE / (name + '.py'))
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


checker = load('check_edit_storage_evidence')
fixtures = load('test_check_edit_storage_evidence')  # its archive fixtures, so one source makes both sides' evidence


@unittest.skipIf(shutil.which('dotnet') is None, 'the .NET SDK is not on this machine')
class ManifestEditBinding(unittest.TestCase):
    tool: Path

    @classmethod
    def setUpClass(cls):
        built = subprocess.run(['dotnet', 'build', str(PROJECT), '-c', 'Release', '--nologo', '-v', 'q'],
                               capture_output=True, text=True)
        if built.returncode != 0:
            raise unittest.SkipTest('the manifest tool does not build here: ' + (built.stdout or built.stderr)[-400:])
        cls.tool = PROJECT.parent / 'bin' / 'Release' / 'net10.0' / 'FileCat.ArchiveReleaseManifest.dll'
        if not cls.tool.is_file():
            raise unittest.SkipTest('the built tool is not where it was looked for: %s' % cls.tool)

    def manifest(self, evidence):
        """Run the tool over one set of (id, file) evidence and return its exit code and what it said."""
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory) / 'manifest.json'
            arguments = ['dotnet', str(self.tool), '--repo', str(ROOT), '--out', str(out), '--allow-dirty']
            for name, path in evidence:
                arguments += ['--evidence', '%s=%s' % (name, path)]
            done = subprocess.run(arguments, capture_output=True, text=True)
            written = json.loads(out.read_text(encoding='utf-8')) if out.is_file() else None
        return done.returncode, (done.stdout + done.stderr), written

    def accepted(self, directory, leaf='edit-lane-evidence.zip', contents=None):
        """An archive the committed check accepts, with that check's own report on it."""
        archive = fixtures.archive(directory, leaf, contents or fixtures.files())
        report = Path(directory) / (leaf + '.check.txt')
        self.assertEqual([], checker.check(archive)[0])
        report.write_text(self.report_for(archive), encoding='utf-8', newline='\n')
        return archive, report

    @staticmethod
    def report_for(archive: Path) -> str:
        problems, facts = checker.check(archive)
        assert not problems, problems
        suites = facts['suites']
        passed = sum(int(counters.get('passed', 0)) for counters in suites.values())
        skipped = sum(int(counters.get('skipped', 0)) for counters in suites.values())
        return ('%s: edit-storage evidence, %d passed, %d skipped, no failure, %s in force, sha256 %s\n'
                % (archive.name, passed, skipped, checker.GATE, checker.accepted_digest(archive)))

    def archive_routes(self, directory):
        """The three archive-write routes' own evidence, which this requirement does not speak about."""
        full = Path(directory) / 'full-run-evidence.zip'
        full.write_text('the archive lanes have their own evidence and their own checks', encoding='utf-8')
        return [(route + '/full', full) for route in ROUTES]

    def test_accepts_every_route_whose_check_accepts_the_archive_it_carries(self):
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            for index, route in enumerate(ROUTES):
                archive, report = self.accepted(directory, 'edit-lane-%d-evidence.zip' % index)
                evidence += [(route + '/edit', archive), (route + '/edit/check', report)]
            code, said, written = self.manifest(evidence)
            self.assertEqual(0, code, said)
            self.assertIn('3 edit-storage routes listed', said)
            self.assertEqual(9, len(written['evidence']))

    def test_refuses_a_route_that_carries_an_archive_and_no_check_of_it(self):
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            for index, route in enumerate(ROUTES):
                archive, _ = self.accepted(directory, 'edit-lane-%d-evidence.zip' % index)
                evidence.append((route + '/edit', archive))
            code, said, written = self.manifest(evidence)
            self.assertEqual(2, code, said)
            self.assertIn('needs the check of its archive under AWQ-win-x64-NTFS/edit/check, not 0 of them', said)
            self.assertIsNone(written)

    def test_refuses_a_file_that_is_not_the_archive_its_check_accepted(self):
        """The 32-byte text file a reviewer got a manifest out of, now with a real report beside it."""
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            archive, report = self.accepted(directory)
            label = Path(directory) / 'holiday-photo-notes.txt'
            label.write_text('nothing was run to make this file', encoding='utf-8')
            for route in ROUTES:
                evidence += [(route + '/edit', label), (route + '/edit/check', report)]
            code, said, written = self.manifest(evidence)
            self.assertEqual(2, code, said)
            self.assertIn('does not accept the archive carried under AWQ-win-x64-NTFS/edit', said)
            self.assertIn('holiday-photo-notes.txt', said)
            self.assertIsNone(written)

    def test_refuses_a_check_of_another_route_s_archive(self):
        """Each report names one archive by digest, so one route's report cannot stand for another's lane."""
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            lanes = [self.accepted(directory, 'edit-lane-%d-evidence.zip' % index) for index in range(len(ROUTES))]
            for index, route in enumerate(ROUTES):
                archive, _ = lanes[index]
                _, report = lanes[(index + 1) % len(lanes)]  # its neighbour's report: a real one, of another archive
                evidence += [(route + '/edit', archive), (route + '/edit/check', report)]
            code, said, written = self.manifest(evidence)
            self.assertEqual(2, code, said)
            self.assertIn('does not accept the archive carried under', said)
            self.assertIsNone(written)

    def test_refuses_a_check_that_says_the_archive_is_not_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            # A run whose machine record does not name the gate: the check refuses it, in its own words.
            contents = fixtures.files()
            contents['machine.json'] = json.dumps({'Environment': {}})
            archive = fixtures.archive(directory, 'edit-skipped-evidence.zip', contents)
            self.assertNotEqual([], checker.check(archive)[0])
            report = Path(directory) / 'refusal.txt'
            report.write_text('%s: NOT edit-storage evidence\n    the run does not record %s\n'
                              % (archive.name, checker.GATE), encoding='utf-8', newline='\n')
            for route in ROUTES:
                evidence += [(route + '/edit', archive), (route + '/edit/check', report)]
            code, said, written = self.manifest(evidence)
            self.assertEqual(2, code, said)
            self.assertIn('does not accept the archive carried under', said)
            self.assertIsNone(written)

    def test_refuses_two_archives_under_one_route_s_id(self):
        """Which of them the check accepted would decide the listing, so the manifest will not choose."""
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            for index, route in enumerate(ROUTES):
                archive, report = self.accepted(directory, 'edit-lane-%d-evidence.zip' % index)
                evidence += [(route + '/edit', archive), (route + '/edit/check', report)]
            spare, _ = self.accepted(directory, 'edit-spare-evidence.zip')
            evidence.append((ROUTES[0] + '/edit', spare))
            code, said, written = self.manifest(evidence)
            self.assertEqual(2, code, said)
            self.assertIn('needs one archive of its own under AWQ-win-x64-NTFS/edit, not 2', said)
            self.assertIsNone(written)


if __name__ == '__main__':
    unittest.main()
