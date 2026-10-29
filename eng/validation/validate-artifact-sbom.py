#!/usr/bin/env python3
import argparse
import base64
import copy
import hashlib
import io
import json
import os
import pathlib
import shutil
import subprocess
import sys
import urllib.parse
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
REFERENCE_BYTES = b"reviewed-reference-assembly-fixture\x00\x02"
REFERENCE_PATH = "lib_manual/netcoreapp3.0/Fixture.Package.Core.dll"
# The files publishing takes from the restored package and runtime pack folders, by archive member name (NuGet escapes
# member names and restores them unescaped).
PACKAGE_MEMBERS = {
    "lib/net10.0/Fixture.Package.dll": b"package-runtime-fixture\x00\x03",
    "runtimes/win-x64/native/fixture.native.dll": b"package-native-fixture\x00\x04",
    "lib/net10.0/de/Fixture%2BPackage.resources.dll": b"package-resources-fixture\x00\x05",
    REFERENCE_PATH: REFERENCE_BYTES,
}
RUNTIME_MEMBERS = {
    "runtimes/win-x64/lib/net10.0/System.Fixture.dll": b"runtime-managed-fixture\x00\x06",
    "runtimes/win-x64/native/coreclr.fixture.dll": b"runtime-native-fixture\x00\x07",
}


def make_archive(members) -> bytes:
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_STORED) as archive:
        for name, data in members.items():
            archive.writestr(zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0)), data)
    return buffer.getvalue()


def restore(folder: pathlib.Path, members) -> None:
    for name, data in members.items():
        target = folder / urllib.parse.unquote(name)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)


RUNTIME_ARCHIVE_BYTES = make_archive(RUNTIME_MEMBERS)
RUNTIME_ARCHIVE_SHA256 = hashlib.sha256(RUNTIME_ARCHIVE_BYTES).hexdigest()
RUNTIME_ARCHIVE_SHA512 = base64.b64encode(hashlib.sha512(RUNTIME_ARCHIVE_BYTES).digest()).decode("ascii")
PACKAGE_ARCHIVE_BYTES = make_archive(PACKAGE_MEMBERS)
PACKAGE_ARCHIVE_SHA256 = hashlib.sha256(PACKAGE_ARCHIVE_BYTES).hexdigest()
PACKAGE_ARCHIVE_SHA512 = base64.b64encode(hashlib.sha512(PACKAGE_ARCHIVE_BYTES).digest()).decode("ascii")


def digest(path: pathlib.Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path: pathlib.Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def deps(include_runtime=True, package_hash="pkg-content", package_id="Fixture.Package",
         reference_id="Fixture.Package.Core", reference_hash="", projects=(), project_runtime=None):
    libraries = {
        f"{package_id}/1.2.3": {"type": "package", "serviceable": True, "sha512": "sha512-" + package_hash},
        "FileCat/1.0.0": {"type": "project", "serviceable": False, "sha512": ""},
        f"{reference_id}/1.2.3": {"type": "reference", "serviceable": False, "sha512": reference_hash},
    }
    for project in projects:
        libraries[f"{project}/1.0.0"] = {"type": "project", "serviceable": False, "sha512": ""}
    # The files each library contributes, as a real manifest's targets name them: package files by their path inside the
    # package, a reference's and a runtime pack's by bare name.
    target = {
        f"{package_id}/1.2.3": {
            "runtime": {"lib/net10.0/Fixture.Package.dll": {}},
            "native": {"runtimes/win-x64/native/fixture.native.dll": {}},
            "resources": {"lib/net10.0/de/Fixture+Package.resources.dll": {"locale": "de"}},
        },
        f"{reference_id}/1.2.3": {"runtime": {f"{reference_id}.dll": {}}},
    }
    if include_runtime:
        libraries["runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.14"] = {
            "type": "runtimepack", "serviceable": False, "sha512": ""
        }
        target["runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.14"] = {
            "runtime": {"System.Fixture.dll": {}}, "native": {"coreclr.fixture.dll": {}}}
    for project, asset in (project_runtime or {}).items():
        target[f"{project}/1.0.0"] = {"runtime": {asset: {}}}
    return {"runtimeTarget": {"name": ".NETCoreApp,Version=v10.0/win-x64", "signature": ""}, "libraries": libraries,
            "targets": {".NETCoreApp,Version=v10.0/win-x64": target}}


def snapshot(extra_package=False):
    packages = [{
        "ID": "Fixture.Package", "Version": "1.2.3", "NuGetContentHash": "pkg-content",
        "PackageArchiveSHA256": PACKAGE_ARCHIVE_SHA256,
        "NuGetLicenseDeclaration": {"Text": "MIT", "Attributes": {"type": "expression"}},
        "NuGetRepositoryDeclaration": None,
        "RetainedNoticePaths": ["fixture/LICENSE"],
        "ReferenceComponents": [{"ID": "Fixture.Package.Core", "PackagePath": REFERENCE_PATH}],
    }]
    if extra_package:
        packages.append({
            "ID": "Missing.Package", "Version": "9.0.0", "NuGetContentHash": "missing-content",
            "PackageArchiveSHA256": "2" * 64,
            "NuGetLicenseDeclaration": {"Text": "LICENSE", "Attributes": {"type": "file"}},
            "NuGetRepositoryDeclaration": {"Text": None, "Attributes": {"type": "git"}},
            "RetainedNoticePaths": ["missing/LICENSE"],
        })
    return {
        "SchemaVersion": 1,
        "Packages": packages,
        "RuntimePacks": [{
            "ID": "Microsoft.NETCore.App.Runtime.win-x64", "Version": "10.0.14", "RID": "win-x64",
            "NuGetContentHashFromMetadata": RUNTIME_ARCHIVE_SHA512, "PackageArchiveSHA256": RUNTIME_ARCHIVE_SHA256,
            "RetainedNoticePaths": ["runtime/LICENSE", "runtime/THIRD-PARTY-NOTICES"],
        }],
    }


def prepare(folder: pathlib.Path, *, include_runtime=True, package_hash="pkg-content", runtime_hash=RUNTIME_ARCHIVE_SHA512,
            package_id="Fixture.Package", reference_id="Fixture.Package.Core", reference_hash="",
            extra_package=False, projects=(), project_runtime=None, project_files=()):
    payload = folder / "payload"
    payload.mkdir(parents=True)
    write_json(payload / "FileCat.deps.json", deps(include_runtime, package_hash, package_id, reference_id, reference_hash,
                                                   projects, project_runtime))
    for name in project_files:
        (payload / name).write_bytes(b"project-fixture\x00" + name.encode("utf-8"))
    (payload / "FileCat.dll").write_bytes(b"managed-fixture\x00\x01")
    (payload / "Fixture.Package.Core.dll").write_bytes(REFERENCE_BYTES)
    (payload / "nested").mkdir()
    (payload / "nested" / "native.bin").write_bytes(bytes(range(32)))
    index = folder / "index.json"
    index_value = snapshot(extra_package)
    index_value["RuntimePacks"][0]["NuGetContentHashFromMetadata"] = runtime_hash
    write_json(index, index_value)
    packages_root = folder / "nuget-packages"
    runtime_folder = packages_root / "microsoft.netcore.app.runtime.win-x64" / "10.0.14"
    runtime_folder.mkdir(parents=True)
    archive = runtime_folder / "microsoft.netcore.app.runtime.win-x64.10.0.14.nupkg"
    archive.write_bytes(RUNTIME_ARCHIVE_BYTES)
    pathlib.Path(str(archive) + ".sha512").write_text(RUNTIME_ARCHIVE_SHA512 + "\n", encoding="utf-8")
    restore(runtime_folder, RUNTIME_MEMBERS)
    package_folder = packages_root / "fixture.package" / "1.2.3"
    package_folder.mkdir(parents=True)
    package_archive = package_folder / "fixture.package.1.2.3.nupkg"
    package_archive.write_bytes(PACKAGE_ARCHIVE_BYTES)
    pathlib.Path(str(package_archive) + ".sha512").write_text(PACKAGE_ARCHIVE_SHA512 + "\n", encoding="utf-8")
    restore(package_folder, PACKAGE_MEMBERS)
    return payload, index, packages_root


def edit_json(path: pathlib.Path, change) -> None:
    value = json.loads(path.read_text(encoding="utf-8"))
    change(value)
    write_json(path, value)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence-dir", required=True)
    options = parser.parse_args()
    evidence = pathlib.Path(options.evidence_dir).resolve()
    if evidence.exists():
        raise SystemExit("evidence directory must be new")
    evidence.mkdir(parents=True)
    artifacts = evidence / "artifacts"
    build = subprocess.run([
        "dotnet", "build", str(ROOT / "eng/ArtifactSbom/ArtifactSbom.csproj"), "-c", "Release",
        "--artifacts-path", str(artifacts), "-p:RestoreLockedMode=true", "--nologo"
    ], cwd=ROOT, text=True, capture_output=True)
    (evidence / "build.stdout.txt").write_text(build.stdout, encoding="utf-8")
    (evidence / "build.stderr.txt").write_text(build.stderr, encoding="utf-8")
    if build.returncode:
        raise SystemExit(build.returncode)
    tool = artifacts / "bin/ArtifactSbom/release/ArtifactSbom.dll"
    if not tool.is_file():
        raise SystemExit("built tool missing")

    cases = []

    def run(name, expected, *, kind="setup-payload", include_runtime=True, package_hash="pkg-content",
            runtime_hash=RUNTIME_ARCHIVE_SHA512, package_id="Fixture.Package", extra_package=False,
            reference_id="Fixture.Package.Core", reference_hash="", output_inside=False, precreate=False,
            extras=(), mutate=None, mutate_runtime_archive=False, mutate_package_archive=False,
            expected_reference_sha256=None, projects=(), expected_project_licenses=None, project_runtime=None,
            project_files=(), expected_root_comment=None, expected_appimage_runtime=False, expected_inno=False,
            mutate_restored=None, expected_package_comment=None):
        folder = evidence / "cases" / name
        payload, index, packages_root = prepare(folder, include_runtime=include_runtime, package_hash=package_hash,
                                                runtime_hash=runtime_hash, package_id=package_id,
                                                reference_id=reference_id, reference_hash=reference_hash,
                                                extra_package=extra_package, projects=projects,
                                                project_runtime=project_runtime, project_files=project_files)
        if mutate:
            mutate(payload, index)
        if mutate_runtime_archive:
            archive = packages_root / "microsoft.netcore.app.runtime.win-x64" / "10.0.14" / "microsoft.netcore.app.runtime.win-x64.10.0.14.nupkg"
            archive.write_bytes(b"tampered-runtime-package")
        if mutate_package_archive:
            archive = packages_root / "fixture.package" / "1.2.3" / "fixture.package.1.2.3.nupkg"
            archive.write_bytes(b"tampered-package")
        if mutate_restored:
            mutate_restored(packages_root)
        output = payload / "inside.spdx.json" if output_inside else folder / "result.spdx.json"
        if precreate:
            output.write_text("preserve", encoding="utf-8")
        command = ["dotnet", str(tool), str(payload), str(index), "1.0.0-rc.1", "win-x64", kind,
                   "a" * 40, "2026-10-28T12:00:00Z", str(output)] + list(extras)
        environment = dict(os.environ)
        environment["NUGET_PACKAGES"] = str(packages_root)
        result = subprocess.run(command, cwd=ROOT, text=True, capture_output=True, env=environment)
        (folder / "stdout.txt").write_text(result.stdout, encoding="utf-8")
        (folder / "stderr.txt").write_text(result.stderr, encoding="utf-8")
        allowed = result.returncode == 0
        passed = allowed == expected and output.exists() == (expected or precreate)
        details = None
        if allowed:
            document = json.loads(output.read_text(encoding="utf-8"))
            files = document["files"]
            root_package = next(value for value in document["packages"] if value["SPDXID"] == "SPDXRef-Package-FileCat")
            file_names = [value["fileName"] for value in files]
            details = {
                "SPDXVersion": document["spdxVersion"], "Files": len(files), "Packages": len(document["packages"]),
                "Relationships": len(document["relationships"]), "Namespace": document["documentNamespace"],
                "VerificationCode": root_package["packageVerificationCode"]["packageVerificationCodeValue"],
                "SortedUniqueFiles": file_names == sorted(set(file_names)), "OutputSHA256": digest(output),
                "GeneratorAssemblyHashBound": digest(tool) in document["annotations"][0]["comment"],
                "ReferenceFileSHA256": next((checksum["checksumValue"]
                    for value in files if value["fileName"] == "./Fixture.Package.Core.dll"
                    for checksum in value["checksums"] if checksum["algorithm"] == "SHA256"), None),
                "ProjectLicenses": {value["name"]: value["licenseDeclared"] for value in document["packages"]
                                    if value["SPDXID"].startswith("SPDXRef-Project-")},
                "RootComment": root_package["comment"],
                "PackageComment": next((value.get("comment") for value in document["packages"]
                                        if value["name"] == "Fixture.Package"), None),
            }
            runtime_files = [value["SPDXID"] for value in files if value["fileName"] == "./.appimage/runtime"]
            runtime_package = next((value for value in document["packages"] if value["SPDXID"] == "SPDXRef-AppImage-Runtime"), None)
            details["AppImageRuntime"] = None if runtime_package is None else {
                "License": runtime_package["licenseDeclared"],
                "HoldsRuntimeFile": runtime_package.get("hasFiles") == runtime_files and len(runtime_files) == 1,
                "OutsideFileCat": not set(runtime_files) & set(root_package["hasFiles"]),
                "Contained": any(value["spdxElementId"] == "SPDXRef-Package-FileCat" and value["relationshipType"] == "CONTAINS"
                                 and value["relatedSpdxElement"] == "SPDXRef-AppImage-Runtime"
                                 for value in document["relationships"]),
            }
            inno_files = [value["SPDXID"] for value in files if value["fileName"].startswith("./licenses/InnoSetup-")]
            inno_package = next((value for value in document["packages"] if value["SPDXID"] == "SPDXRef-Inno-Setup"), None)
            details["InnoSetup"] = None if inno_package is None else {
                "Version": inno_package["versionInfo"],
                "HoldsLicenseText": inno_package.get("hasFiles") == inno_files and len(inno_files) == 1,
                "OutsideFileCat": not set(inno_files) & set(root_package["hasFiles"]),
                "NamesUninstaller": "unins000.exe" in inno_package["comment"],
            }
            passed = (passed and details["SPDXVersion"] == "SPDX-2.3" and details["SortedUniqueFiles"]
                      and details["GeneratorAssemblyHashBound"])
            if expected_reference_sha256 is not None:
                passed = passed and details["ReferenceFileSHA256"] == expected_reference_sha256
            if expected_project_licenses is not None:
                passed = passed and details["ProjectLicenses"] == expected_project_licenses
            if expected_root_comment is not None:
                passed = passed and expected_root_comment in details["RootComment"]
            if expected_package_comment is not None:
                passed = passed and expected_package_comment in (details["PackageComment"] or "")
            runtime = details["AppImageRuntime"]
            if expected_appimage_runtime:
                passed = passed and runtime == {
                    "License": "MIT AND LGPL-2.1-only AND BSD-2-Clause AND BSD-3-Clause AND Zlib",
                    "HoldsRuntimeFile": True, "OutsideFileCat": True, "Contained": True}
            else:
                passed = passed and runtime is None
            inno = details["InnoSetup"]
            if expected_inno:
                passed = passed and inno == {"Version": "6.7.1", "HoldsLicenseText": True, "OutsideFileCat": True,
                                             "NamesUninstaller": True}
            else:
                passed = passed and inno is None
        elif precreate:
            passed = passed and output.read_text(encoding="utf-8") == "preserve"
        cases.append({
            "Name": name, "ExpectedAllowed": expected, "Allowed": allowed, "Passed": passed,
            "ExitCode": result.returncode, "Command": command, "StdoutSHA256": digest(folder / "stdout.txt"),
            "StderrSHA256": digest(folder / "stderr.txt"), "Details": details,
        })

    run("self-contained", True,
        expected_package_comment=f"Fixture.Package.Core/1.2.3 (from {REFERENCE_PATH})")
    run("framework-dependent", True, kind="fdd", include_runtime=False)
    extra = evidence / "extra-runtime"
    extra.write_bytes(b"appimage-runtime")
    # An AppImage's runtime is type2-runtime, under its own licenses: its own package holds it, not FileCat's.
    run("extra-file", True, kind="appimage", extras=(f".appimage/runtime={extra}",), expected_appimage_runtime=True)
    run("wrong-package-hash", False, package_hash="wrong")
    run("unknown-package", False, package_id="Unknown.Package")
    run("unused-reviewed-package", True, extra_package=True)
    # The installer adds Inno Setup's license text; the setup SBOM files it, with Inno Setup's loader and uninstaller, under
    # Inno Setup's own package.
    inno_text = evidence / "InnoSetup-6.7.1.txt"
    inno_text.write_bytes(b"Inno Setup License fixture\n")
    run("inno-setup-license", True, extras=(f"licenses/InnoSetup-6.7.1.txt={inno_text}",), expected_inno=True)
    # FileCat's projects are MIT, except its XADMaster port (LGPL) and its build of SharpCompress, whose parts keep their
    # own licenses (licenses/sharpcompress/COMPONENTS.md).
    run("project-licenses", True, projects=("FileCat.Core", "FileCat.Archives.Rar15", "FileCat.SharpCompress"),
        expected_project_licenses={
            "FileCat.Archives.Rar15": "LGPL-2.1-or-later",
            "FileCat.Core": "MIT",
            "FileCat.SharpCompress": "MIT AND Apache-2.0 AND MS-PL AND BSD-2-Clause AND BSD-3-Clause AND Zlib",
        })
    # A project the manifest names but whose files the payload lacks (the Windows-only helpers in the Linux and macOS
    # packages) is not shipped there; with its file present it is listed.
    run("project-files-absent-omitted", True, projects=("FileCat.ShellHost",),
        project_runtime={"FileCat.ShellHost": "FileCat.ShellHost.dll"}, expected_project_licenses={},
        expected_root_comment="also names FileCat.ShellHost, whose files this payload does not carry")
    run("project-files-present-listed", True, projects=("FileCat.ShellHost",),
        project_runtime={"FileCat.ShellHost": "FileCat.ShellHost.dll"}, project_files=("FileCat.ShellHost.dll",),
        expected_project_licenses={"FileCat.ShellHost": "MIT"})
    run("unknown-reference-origin", False, reference_id="Other.Component")
    # A reference is a package's only when the reviewed index names it so: sharing the package's ID as a prefix is not
    # enough, and the manifest must name the file the index does.
    run("reference-prefix-only", False, reference_id="Fixture.Package.Other")
    # On Linux and macOS the manifest lists WebView2's Core reference but not the package, which has no files for those
    # RIDs: the reference is still the package's, and the package's archive and the reference's restored file are checked.
    def drop_package(payload, _index):
        def change(value):
            value["libraries"].pop("Fixture.Package/1.2.3")
            value["targets"][".NETCoreApp,Version=v10.0/win-x64"].pop("Fixture.Package/1.2.3")
        edit_json(payload / "FileCat.deps.json", change)

    run("reference-without-package-files", True, mutate=drop_package,
        expected_package_comment=f"Fixture.Package.Core/1.2.3 (from {REFERENCE_PATH})")
    run("reference-without-package-files-changed", False, mutate=drop_package,
        mutate_restored=lambda root: (root / f"fixture.package/1.2.3/{REFERENCE_PATH}").write_bytes(b"tampered-restored"))
    run("reference-not-named-by-index", False,
        mutate=lambda _payload, index: edit_json(index, lambda value: value["Packages"][0].pop("ReferenceComponents")))
    run("reference-file-differs", False,
        mutate=lambda payload, _index: edit_json(payload / "FileCat.deps.json", lambda value: value["targets"][
            ".NETCoreApp,Version=v10.0/win-x64"]["Fixture.Package.Core/1.2.3"].update(
                runtime={"Fixture.Package.Other.dll": {}})))
    # Publishing copies the restored package folders, not the archives whose hashes are reviewed: each file it takes must
    # be the archive member's own bytes.
    for case, relative in (
            ("restored-package-file-changed", "fixture.package/1.2.3/lib/net10.0/Fixture.Package.dll"),
            ("restored-package-resources-changed", "fixture.package/1.2.3/lib/net10.0/de/Fixture+Package.resources.dll"),
            ("restored-reference-file-changed", f"fixture.package/1.2.3/{REFERENCE_PATH}"),
            ("restored-runtime-file-changed",
             "microsoft.netcore.app.runtime.win-x64/10.0.14/runtimes/win-x64/lib/net10.0/System.Fixture.dll")):
        run(case, False, mutate_restored=lambda root, relative=relative: (root / relative).write_bytes(b"tampered-restored"))
    run("restored-file-missing", False, mutate_restored=lambda root: (
        root / "microsoft.netcore.app.runtime.win-x64/10.0.14/runtimes/win-x64/native/coreclr.fixture.dll").unlink())
    run("manifest-file-not-in-archive", False,
        mutate=lambda payload, _index: edit_json(payload / "FileCat.deps.json", lambda value: value["targets"][
            ".NETCoreApp,Version=v10.0/win-x64"]["Fixture.Package/1.2.3"]["native"].update(
                {"runtimes/win-x64/native/unreviewed.dll": {}})))
    run("manifest-file-escapes-package", False,
        mutate=lambda payload, _index: edit_json(payload / "FileCat.deps.json", lambda value: value["targets"][
            ".NETCoreApp,Version=v10.0/win-x64"]["Fixture.Package/1.2.3"]["native"].update(
                {"../../fixture.package/1.2.3/lib/net10.0/Fixture.Package.dll": {}})))
    run("hashed-reference", False, reference_hash="sha512-unreviewed")
    run("generated-reference-bytes-recorded", True,
        mutate=lambda payload, _index: (payload / "Fixture.Package.Core.dll").write_bytes(b"tampered-reference"),
        expected_reference_sha256=hashlib.sha256(b"tampered-reference").hexdigest())
    run("wrong-reference-package-archive", False, mutate_package_archive=True)
    run("wrong-runtime-hash", False, runtime_hash="wrong")
    run("wrong-runtime-archive", False, mutate_runtime_archive=True)
    run("missing-runtime", False, include_runtime=False)
    run("runtime-in-fdd", False, kind="fdd")
    run("output-inside-payload", False, output_inside=True)
    run("existing-output-preserved", False, precreate=True)
    run("duplicate-extra-logical-path", False, kind="appimage",
        extras=(f"nested/native.bin={extra}",))
    if os.name != "nt":
        run("symbolic-link", True,
            mutate=lambda payload, _index: (payload / "native-link").symlink_to("nested/native.bin"))

        def link_restored(root: pathlib.Path) -> None:
            restored = root / "fixture.package/1.2.3/lib/net10.0/Fixture.Package.dll"
            copy = root / "outside-copy.dll"
            copy.write_bytes(restored.read_bytes())
            restored.unlink()
            restored.symlink_to(copy)

        run("restored-file-linked", False, mutate_restored=link_restored)

    if not all(case["Passed"] for case in cases):
        write_json(evidence / "artifact-sbom-controls.json", {"Cases": cases})
        return 1
    summary = {
        "SourceCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "SourceTreeDirty": bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip()),
        "BuildExitCode": build.returncode, "ToolSHA256": digest(tool), "Controls": len(cases),
        "Platform": sys.platform,
        "Allowed": sum(case["Allowed"] for case in cases), "Refused": sum(not case["Allowed"] for case in cases),
        "Passed": sum(case["Passed"] for case in cases), "Cases": cases,
        "CandidateQualified": False, "FinalArtifactCompositionQualified": False,
    }
    write_json(evidence / "artifact-sbom-controls.json", summary)
    print(json.dumps({key: summary[key] for key in ("Controls", "Allowed", "Refused", "Passed", "SourceCommit", "SourceTreeDirty")}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
