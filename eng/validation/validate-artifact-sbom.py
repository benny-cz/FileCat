#!/usr/bin/env python3
import argparse
import copy
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
RUNTIME_ARCHIVE_BYTES = b"reviewed-runtime-package-fixture\x00\x01"
RUNTIME_ARCHIVE_SHA256 = hashlib.sha256(RUNTIME_ARCHIVE_BYTES).hexdigest()


def digest(path: pathlib.Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path: pathlib.Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def deps(include_runtime=True, package_hash="pkg-content", package_id="Fixture.Package",
         reference_id="Fixture.Package.Core", reference_hash=""):
    libraries = {
        f"{package_id}/1.2.3": {"type": "package", "serviceable": True, "sha512": "sha512-" + package_hash},
        "FileCat/1.0.0": {"type": "project", "serviceable": False, "sha512": ""},
        f"{reference_id}/1.2.3": {"type": "reference", "serviceable": False, "sha512": reference_hash},
    }
    if include_runtime:
        libraries["runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.14"] = {
            "type": "runtimepack", "serviceable": False, "sha512": ""
        }
    return {"runtimeTarget": {"name": ".NETCoreApp,Version=v10.0/win-x64", "signature": ""}, "libraries": libraries}


def snapshot(extra_package=False):
    packages = [{
        "ID": "Fixture.Package", "Version": "1.2.3", "NuGetContentHash": "pkg-content",
        "PackageArchiveSHA256": "1" * 64,
        "NuGetLicenseDeclaration": {"Text": "MIT", "Attributes": {"type": "expression"}},
        "NuGetRepositoryDeclaration": None,
        "RetainedNoticePaths": ["fixture/LICENSE"],
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
            "NuGetContentHashFromMetadata": "runtime-content", "PackageArchiveSHA256": RUNTIME_ARCHIVE_SHA256,
            "RetainedNoticePaths": ["runtime/LICENSE", "runtime/THIRD-PARTY-NOTICES"],
        }],
    }


def prepare(folder: pathlib.Path, *, include_runtime=True, package_hash="pkg-content", runtime_hash="runtime-content",
            package_id="Fixture.Package", reference_id="Fixture.Package.Core", reference_hash="",
            extra_package=False):
    payload = folder / "payload"
    payload.mkdir(parents=True)
    write_json(payload / "FileCat.deps.json", deps(include_runtime, package_hash, package_id, reference_id, reference_hash))
    (payload / "FileCat.dll").write_bytes(b"managed-fixture\x00\x01")
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
    pathlib.Path(str(archive) + ".sha512").write_text("runtime-content\n", encoding="utf-8")
    return payload, index, packages_root


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
            runtime_hash="runtime-content", package_id="Fixture.Package", extra_package=False,
            reference_id="Fixture.Package.Core", reference_hash="", output_inside=False, precreate=False,
            extras=(), mutate=None, mutate_runtime_archive=False):
        folder = evidence / "cases" / name
        payload, index, packages_root = prepare(folder, include_runtime=include_runtime, package_hash=package_hash,
                                                runtime_hash=runtime_hash, package_id=package_id,
                                                reference_id=reference_id, reference_hash=reference_hash,
                                                extra_package=extra_package)
        if mutate:
            mutate(payload, index)
        if mutate_runtime_archive:
            archive = packages_root / "microsoft.netcore.app.runtime.win-x64" / "10.0.14" / "microsoft.netcore.app.runtime.win-x64.10.0.14.nupkg"
            archive.write_bytes(b"tampered-runtime-package")
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
            }
            passed = (passed and details["SPDXVersion"] == "SPDX-2.3" and details["SortedUniqueFiles"]
                      and details["GeneratorAssemblyHashBound"])
        elif precreate:
            passed = passed and output.read_text(encoding="utf-8") == "preserve"
        cases.append({
            "Name": name, "ExpectedAllowed": expected, "Allowed": allowed, "Passed": passed,
            "ExitCode": result.returncode, "Command": command, "StdoutSHA256": digest(folder / "stdout.txt"),
            "StderrSHA256": digest(folder / "stderr.txt"), "Details": details,
        })

    run("self-contained", True)
    run("framework-dependent", True, kind="fdd", include_runtime=False)
    extra = evidence / "extra-runtime"
    extra.write_bytes(b"appimage-runtime")
    run("extra-file", True, kind="appimage", extras=(f".appimage/runtime={extra}",))
    run("wrong-package-hash", False, package_hash="wrong")
    run("unknown-package", False, package_id="Unknown.Package")
    run("unused-reviewed-package", True, extra_package=True)
    run("unknown-reference-origin", False, reference_id="Other.Component")
    run("hashed-reference", False, reference_hash="sha512-unreviewed")
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
