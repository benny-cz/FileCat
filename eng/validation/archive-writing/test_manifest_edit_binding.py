"""What the release manifest must refuse for a listed edit-storage route: a label with nothing behind it.

`NativeEditStorage.Qualified` lists a route only with its own evidence (`AWQ-<rid>-<fs>/edit`), and the manifest refuses
a listed route whose evidence it does not carry. That refusal alone was satisfied by any file under the right ID: one
review wrote a manifest whose three edit routes were satisfied by a 32-byte text file, and another whose
`.../edit/check` report said in its own words that the archive was NOT edit-storage evidence. The listing of `a4606689`
shows what it permitted in practice - its three `.../edit` entries are byte-identical to its `.../full` entries, the
archive lane's own archive under a second label. Binding the report to the archive by digest was not enough either: the
next review carried one machine's lane, with its own true report, under all three routes, and the manifest took it for
all three.

So `check_edit_storage_evidence.py` names the archive it accepted by digest and the system that ran it, and the manifest
requires the report carried under a route's own `.../check` to accept the very archive carried under that route's ID, on
that route's own system, by a line whose whole tail it reads. These cases are that requirement: what it accepts, and
every way of satisfying the older, weaker refusals that it now turns down. Each report here is written by the committed
check itself, so nothing in this file can agree with the manifest while the check disagrees.

Run by hand, as its sibling is, from the checkout's root (isolated Python keeps the working folder off the path, so
discovery is what puts this folder on it):

    python -I -X utf8 -m unittest discover -s eng/validation/archive-writing -p "test_manifest_edit_binding.py" -v

It builds the manifest tool from the checkout it lives in - the tool refuses to describe a commit its own build does
not come from - so it needs the .NET SDK and about a minute the first time.
"""
from __future__ import annotations

import contextlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
import unittest.mock

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
PROJECT = ROOT / 'eng' / 'ArchiveReleaseManifest' / 'ArchiveReleaseManifest.csproj'
# The three routes this build lists, and the first line each machine's own record opens with. The tool names the ID it
# refuses, so a changed list fails here by name rather than silently passing.
ROUTES = {
    'AWQ-win-x64-NTFS': 'Microsoft Windows 11 Pro 10.0.26220.0',
    'AWQ-linux-x64-ext4': 'Linux filecat-ubuntu2404 7.0.0-43-generic #43 SMP x86_64 GNU/Linux',
    'AWQ-osx-arm64-apfs': 'Darwin Mareks-MacBook-Pro.local 27.0.0 Darwin Kernel Version 27.0.0 arm64',
}


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

    # ---------------------------------------------------------------- the evidence these cases are made of

    def accepted(self, folder, system, leaf='edit-lane-evidence.zip', **differences):
        """An archive the committed check accepts, and that check's own report on it, written by the check itself."""
        folder = Path(folder)
        folder.mkdir(parents=True, exist_ok=True)
        archive = fixtures.archive(folder, leaf, fixtures.files(system=system, **differences))
        report = folder / (leaf + '.check.txt')
        with contextlib.redirect_stdout(io.StringIO()):
            with unittest.mock.patch.object(sys, 'argv', ['check', str(archive), '--out', str(report)]):
                self.assertEqual(0, checker.main(), 'the check refuses its own fixture')
        return archive, report

    def archive_routes(self, directory):
        """The three archive-write routes' own evidence, which this requirement does not speak about."""
        full = Path(directory) / 'full-run-evidence.zip'
        full.write_text('the archive lanes have their own evidence and their own checks', encoding='utf-8')
        return [(route + '/full', full) for route in ROUTES]

    def every_route(self, directory):
        """One accepted archive and report per route, each recording that route's own system."""
        return {route: self.accepted(Path(directory) / route, system)
                for route, system in ROUTES.items()}

    def manifest(self, evidence):
        """Run the tool over one set of (id, file) evidence: its exit code, what it said, and what it wrote."""
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory) / 'manifest.json'
            arguments = ['dotnet', str(self.tool), '--repo', str(ROOT), '--out', str(out), '--allow-dirty']
            for name, path in evidence:
                arguments += ['--evidence', '%s=%s' % (name, path)]
            done = subprocess.run(arguments, capture_output=True, text=True)
            written = json.loads(out.read_text(encoding='utf-8')) if out.is_file() else None
        return done.returncode, (done.stdout + done.stderr), written

    # ---------------------------------------------------------------- what it accepts

    def test_accepts_every_route_whose_check_accepts_the_archive_it_carries(self):
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            for route, (archive, report) in self.every_route(directory).items():
                evidence += [(route + '/edit', archive), (route + '/edit/check', report)]
            code, said, written = self.manifest(evidence)
            self.assertEqual(0, code, said)
            self.assertIn('3 edit-storage routes listed', said)
            self.assertEqual(9, len(written['evidence']))

    # ---------------------------------------------------------------- and what it does not

    def test_refuses_a_route_that_carries_an_archive_and_no_check_of_it(self):
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            for route, (archive, _) in self.every_route(directory).items():
                evidence.append((route + '/edit', archive))
            code, said, written = self.manifest(evidence)
            self.assertEqual(2, code, said)
            self.assertIn('needs the check of its archive under AWQ-win-x64-NTFS/edit/check, not 0 of them', said)
            self.assertIsNone(written)

    def test_refuses_a_file_that_is_not_the_archive_its_check_accepted(self):
        """The 32-byte text file a review got a manifest out of, now with a real report beside it."""
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            lanes = self.every_route(directory)
            label = Path(directory) / 'holiday-photo-notes.txt'
            label.write_text('nothing was run to make this file', encoding='utf-8')
            for route, (_, report) in lanes.items():
                evidence += [(route + '/edit', label), (route + '/edit/check', report)]
            code, said, written = self.manifest(evidence)
            self.assertEqual(2, code, said)
            self.assertIn('does not accept the archive carried under AWQ-win-x64-NTFS/edit', said)
            self.assertIn('holiday-photo-notes.txt', said)
            self.assertIsNone(written)

    def test_refuses_a_check_of_another_archive_of_the_same_name_and_system(self):
        """Only the digest can refuse this one. Each route here carries one `edit-lane-evidence.zip` and the real
        report of a second archive of that same name, from that same route's own system, differing only in bytes -
        which is how two rounds' real archives differ, by content and never by name. Swapping reports between routes
        would not test the digest: their systems differ, and the system would refuse it first."""
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            for route, system in ROUTES.items():
                archive, _ = self.accepted(Path(directory) / route / 'ran', system)
                twin, report = self.accepted(Path(directory) / route / 'also-ran', system, log='[xUnit.net] again\n')
                self.assertNotEqual(archive.read_bytes(), twin.read_bytes())
                evidence += [(route + '/edit', archive), (route + '/edit/check', report)]
            code, said, written = self.manifest(evidence)
            self.assertEqual(2, code, said)
            self.assertIn('does not accept the archive carried under', said)
            self.assertIn('sha256', said)
            self.assertIsNone(written)

    def test_refuses_one_system_s_lane_carried_under_another_route(self):
        """An archive bound only by its digest is bound to no route: the Windows lane, with its own true report,
        satisfied all three listed routes until the report named the system it had read."""
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            archive, report = self.accepted(Path(directory) / 'one-machine', ROUTES['AWQ-win-x64-NTFS'])
            for route in ROUTES:
                evidence += [(route + '/edit', archive), (route + '/edit/check', report)]
            code, said, written = self.manifest(evidence)
            self.assertEqual(2, code, said)
            self.assertIn('AWQ-linux-x64-ext4/edit/check', said)
            self.assertIn(', linux, sha256 ', said)  # what that route's own report would have had to say
            self.assertIsNone(written)

    def test_refuses_a_check_that_says_the_archive_is_not_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            # A run whose machine record does not name the gate: the check refuses it, in its own words.
            contents = fixtures.files(gate=False)
            archive = fixtures.archive(directory, 'edit-skipped-evidence.zip', contents)
            self.assertNotEqual([], checker.check(archive)[0])
            report = Path(directory) / 'refusal.txt'
            with contextlib.redirect_stdout(io.StringIO()):
                with unittest.mock.patch.object(sys, 'argv', ['check', str(archive), '--out', str(report)]):
                    self.assertEqual(1, checker.main())
            self.assertIn('NOT edit-storage evidence', report.read_text(encoding='utf-8'))
            for route in ROUTES:
                evidence += [(route + '/edit', archive), (route + '/edit/check', report)]
            code, said, written = self.manifest(evidence)
            self.assertEqual(2, code, said)
            self.assertIn('does not accept the archive carried under', said)
            self.assertIsNone(written)

    def test_refuses_a_line_that_claims_the_gate_was_off_between_its_two_ends(self):
        """The whole tail is read, not the two ends: a hand-written line with the right head and the right digest, and
        the gate at 0 in between, is a line the check would never write."""
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            for route, system in ROUTES.items():
                archive, report = self.accepted(Path(directory) / route, system)
                verdict = report.read_text(encoding='utf-8').splitlines()[0]
                report.write_text(verdict.replace(checker.GATE + ' in force', checker.GATE_NAME + '=0 in force')
                                  .replace('passed,', 'passed, nothing ran,') + '\n', encoding='utf-8', newline='\n')
                evidence += [(route + '/edit', archive), (route + '/edit/check', report)]
            code, said, written = self.manifest(evidence)
            self.assertEqual(2, code, said)
            self.assertIn('does not accept the archive carried under', said)
            self.assertIsNone(written)

    def test_refuses_two_archives_under_one_route_s_id(self):
        """Which of them the check accepted would decide the listing, so the manifest will not choose."""
        with tempfile.TemporaryDirectory() as directory:
            evidence = self.archive_routes(directory)
            for route, (archive, report) in self.every_route(directory).items():
                evidence += [(route + '/edit', archive), (route + '/edit/check', report)]
            spare, _ = self.accepted(Path(directory) / 'spare', ROUTES['AWQ-win-x64-NTFS'])
            evidence.append(('AWQ-win-x64-NTFS/edit', spare))
            code, said, written = self.manifest(evidence)
            self.assertEqual(2, code, said)
            self.assertIn('needs one archive of its own under AWQ-win-x64-NTFS/edit, not 2', said)
            self.assertIsNone(written)


if __name__ == '__main__':
    unittest.main()
