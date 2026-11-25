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
import struct
import subprocess
import sys
import urllib.parse
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
REFERENCE_BYTES = b"reviewed-reference-assembly-fixture\x00\x02"
REFERENCE_PATH = "lib_manual/netcoreapp3.0/Fixture.Package.Core.dll"
# A file the package's build targets copy into the app folder without the dependency manifest naming it (as WebView2's
# copy its XML documentation and loader), and a license text the notice index retains.
UNLISTED_PATH = "lib/net10.0/Fixture.Package.xml"
UNLISTED_BYTES = b"package-documentation-fixture\x00\x08"
LICENSE_BYTES = b"fixture license text\n"
# The files publishing takes from the restored package and runtime pack folders, by archive member name (NuGet escapes
# member names and restores them unescaped). The empty member is a NuGet placeholder, like a real package's _._ files.
PACKAGE_MEMBERS = {
    "lib/net10.0/Fixture.Package.dll": b"package-runtime-fixture\x00\x03",
    "runtimes/win-x64/native/fixture.native.dll": b"package-native-fixture\x00\x04",
    "lib/net10.0/de/Fixture%2BPackage.resources.dll": b"package-resources-fixture\x00\x05",
    REFERENCE_PATH: REFERENCE_BYTES,
    UNLISTED_PATH: UNLISTED_BYTES,
    "lib/netstandard2.0/_._": b"",
    "LICENSE": LICENSE_BYTES,
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

# The .NET apphost: the SDK binds its template to an app by writing the DLL's name over this placeholder (the SHA-256 text
# of "foobar"). The template also keeps the placeholder's two halves apart, as the binding check that survives binding.
PLACEHOLDER = b"c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2"
APPHOST_LICENSE = b"apphost pack license fixture\n"


def apphost_body(tag: bytes) -> bytes:
    return tag * 8 + PLACEHOLDER[:32] + b"\0" + PLACEHOLDER[32:] + b"\0" + tag * 2 + PLACEHOLDER + bytes(64) + tag * 4


def bind(data: bytes, dll: str) -> bytes:
    name = dll.encode("utf-8")
    at = data.index(PLACEHOLDER)
    return data[:at] + name + bytes(len(PLACEHOLDER) - len(name)) + data[at + len(PLACEHOLDER):]


def elf(body: bytes) -> bytes:
    return b"\x7fELF\x02\x01\x01" + bytes(9) + body


def code_signature(flags: int, cms: bytes) -> bytes:
    """A code-signature SuperBlob (big-endian): a CodeDirectory with these flags (0x2 is ad hoc), then a CMS wrapper."""
    slots = [(0, struct.pack(">IIII", 0xFADE0C02, 24, 0x20400, flags) + bytes(8)),
             (0x10000, struct.pack(">II", 0xFADE0B01, 8 + len(cms)) + cms)]
    offset, index, blobs = 12 + 8 * len(slots), b"", b""
    for slot, blob in slots:
        index += struct.pack(">II", slot, offset)
        blobs += blob
        offset += len(blob)
    return struct.pack(">III", 0xFADE0CC0, offset, len(slots)) + index + blobs


def macho(body: bytes, signature: bytes) -> bytes:
    """A thin arm64 Mach-O: __TEXT over the header and body, __LINKEDIT holding only the code signature."""
    commands = 72 + 72 + 16
    signature_offset = (32 + commands + len(body) + 15) // 16 * 16
    header = struct.pack("<IiiIIIII", 0xFEEDFACF, 0x0100000C, 0, 2, 3, commands, 0x00200085, 0)
    text = struct.pack("<II16sQQQQiiII", 0x19, 72, b"__TEXT", 0x100000000, 0x4000, 0, signature_offset, 5, 5, 0, 0)
    linkedit = struct.pack("<II16sQQQQiiII", 0x19, 72, b"__LINKEDIT", 0x100004000, (len(signature) + 0x3FFF) // 0x4000 * 0x4000,
                           signature_offset, len(signature), 1, 1, 0, 0)
    signature_command = struct.pack("<IIII", 0x1D, 16, signature_offset, len(signature))
    data = header + text + linkedit + signature_command + body
    return data + bytes(signature_offset - len(data)) + signature


def resource_section(rva: int, leaves) -> bytes:
    """A .rsrc section: one type/name/language path per (type, name, language, data) leaf."""
    count = len(leaves)
    names, languages = 16 + 8 * count, 16 + 8 * count + 24 * count
    entries = languages + 24 * count
    offset = entries + 16 * count
    out = struct.pack("<IIHHHH", 0, 0, 0, 0, 0, count)
    out += b"".join(struct.pack("<II", leaf[0], 0x80000000 | (names + 24 * i)) for i, leaf in enumerate(leaves))
    out += b"".join(struct.pack("<IIHHHHII", 0, 0, 0, 0, 0, 1, leaf[1], 0x80000000 | (languages + 24 * i)) for i, leaf in enumerate(leaves))
    out += b"".join(struct.pack("<IIHHHHII", 0, 0, 0, 0, 0, 1, leaf[2], entries + 16 * i) for i, leaf in enumerate(leaves))
    blobs = b""
    for leaf in leaves:
        out += struct.pack("<IIII", rva + offset + len(blobs), len(leaf[3]), 1252, 0)
        blobs += leaf[3] + bytes(-len(leaf[3]) % 4)
    return out + blobs


def pe(sections, *, subsystem=3, timestamp=0x6ADAF2B3, checksum=0, resources=None, certificate=None) -> bytes:
    """A minimal PE32+ (x64) image: its sections in order, then a .rsrc section with the given leaves, then an
    Authenticode-shaped certificate blob."""
    file_align, section_align, headers = 0x200, 0x1000, 0x400
    planned = list(sections) + ([(b".rsrc", None)] if resources is not None else [])
    built, raw, virtual = [], headers, section_align
    for name, data in planned:
        data = resource_section(virtual, resources) if data is None else data
        size = (len(data) + file_align - 1) // file_align * file_align
        built.append((name, data, virtual, raw, size))
        raw += size
        virtual += (len(data) + section_align - 1) // section_align * section_align
    directories = [(0, 0)] * 16
    for name, data, address, _, _ in built:
        if name == b".reloc":
            directories[5] = (address, len(data))
        if name == b".rsrc":
            directories[2] = (address, len(data))
    blob = b""
    if certificate is not None:
        blob = struct.pack("<IHH", 8 + len(certificate), 0x0200, 0x0002) + certificate
        blob += bytes(-len(blob) % 8)
        directories[4] = (raw, len(blob))
    optional = struct.pack("<HBBIIIIIQIIHHHHHHIIIIHHQQQQII", 0x20B, 14, 0, len(built[0][1]), sum(len(b[1]) for b in built[1:]), 0,
                           0x1000, 0x1000, 0x140000000, section_align, file_align, 6, 0, 0, 0, 6, 0, 0, virtual, headers, checksum,
                           subsystem, 0xC160, 0x100000, 0x1000, 0x100000, 0x1000, 0, 16)
    optional += b"".join(struct.pack("<II", *directory) for directory in directories)
    coff = struct.pack("<HHIIIHH", 0x8664, len(built), timestamp, 0, 0, len(optional), 0x22)
    head = b"MZ" + bytes(0x3A) + struct.pack("<I", 0x80) + b"dos-stub-fixture".ljust(0x40, b"\0") + b"PE\0\0" + coff + optional
    head += b"".join(struct.pack("<8sIIIIIIHHI", name, len(data), address, size, pointer, 0, 0, 0, 0, 0x40000040)
                     for name, data, address, pointer, size in built)
    image = head + bytes(headers - len(head))
    for _, data, _, _, size in built:
        image += data + bytes(size - len(data))
    return image + blob


PE_SECTIONS = [(b".text", b"pe-code-fixture;" * 64), (b".data", apphost_body(b"pe-data;")), (b".reloc", b"reloc-fixture;" * 8)]
RESOURCES = [(16, 1, 0, b"version-resource-fixture"), (24, 1, 0, b"<assembly manifest fixture/>")]


def apphost_files(fmt: str, dll: str, *, bound_to=None, change=None, signed=True):
    """The template of an apphost pack and the launcher made from it (bound to the given DLL, or bound_to), with change
    applied to the launcher, plus the DLL its Windows launcher's resources come from."""
    if fmt == "pe":
        template = pe(PE_SECTIONS)
        bound = [(name, bind(data, bound_to or dll) if name == b".data" else data) for name, data in PE_SECTIONS]
        launcher = pe(bound, subsystem=2, timestamp=0x6ADA0000, checksum=0x5330F, resources=RESOURCES,
                      certificate=b"pkcs7-fixture;" * 40 if signed else None)
        app = pe([(b".text", b"managed-app-fixture;" * 16)], resources=RESOURCES)
    elif fmt == "elf":
        template = elf(apphost_body(b"elf-code;"))
        launcher, app = bind(template, bound_to or dll), b"managed-app-fixture"
    else:
        template = macho(apphost_body(b"macho-code;"), code_signature(0x10000, b"vendor-cms-fixture;" * 20))
        launcher = macho(bind(apphost_body(b"macho-code;"), bound_to or dll), code_signature(0x2, b""))
        app = b"managed-app-fixture"
    return template, (change(launcher) if change else launcher), app


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
        "Files": [{"Path": "fixture/LICENSE", "Bytes": len(LICENSE_BYTES), "SHA256": hashlib.sha256(LICENSE_BYTES).hexdigest()}],
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


def add_apphost(payload: pathlib.Path, index: pathlib.Path, packages_root: pathlib.Path, *, fmt: str, rid: str, version="10.0.14",
                bound_to=None, change=None, signed=True, restored=True, packs_root=None, index_change=None):
    """A reviewed apphost pack for rid (its archive restored, or only its template in an SDK packs folder), and the
    launcher made from it beside FileCat.dll in the payload."""
    launcher_name = "FileCat.exe" if rid.startswith("win-") else "FileCat"
    template, launcher, app = apphost_files(fmt, "FileCat.dll", bound_to=bound_to, change=change, signed=signed)
    template_path = f"runtimes/{rid}/native/apphost" + (".exe" if rid.startswith("win-") else "")
    pack_id = f"Microsoft.NETCore.App.Host.{rid}"
    members = {template_path: template, "LICENSE.TXT": APPHOST_LICENSE}
    archive = make_archive(members)
    if restored:
        folder = packages_root / pack_id.lower() / version
        folder.mkdir(parents=True)
        nupkg = folder / f"{pack_id.lower()}.{version}.nupkg"
        nupkg.write_bytes(archive)
        pathlib.Path(str(nupkg) + ".sha512").write_text(base64.b64encode(hashlib.sha512(archive).digest()).decode("ascii") + "\n", encoding="utf-8")
        restore(folder, members)
    if packs_root is not None:
        packed = packs_root / "packs" / pack_id / version / template_path
        packed.parent.mkdir(parents=True)
        packed.write_bytes(template)
    (payload / launcher_name).write_bytes(launcher)
    (payload / "FileCat.dll").write_bytes(app)

    def change_index(value):
        value["Files"].append({"Path": "runtime/LICENSE.TXT", "Bytes": len(APPHOST_LICENSE), "SHA256": hashlib.sha256(APPHOST_LICENSE).hexdigest()})
        value["AppHostPacks"] = [{
            "ID": pack_id, "Version": version, "RID": rid,
            "PackageArchiveSHA256": hashlib.sha256(archive).hexdigest(),
            "RawArchiveSHA512": base64.b64encode(hashlib.sha512(archive).digest()).decode("ascii"),
            "TemplatePath": template_path, "TemplateBytes": len(template), "TemplateSHA256": hashlib.sha256(template).hexdigest(),
            "RetainedNoticePaths": ["runtime/LICENSE.TXT"],
        }]
        if index_change:
            index_change(value)
    edit_json(index, change_index)


def declare_unlisted(value, sha256=None):
    value["Packages"][0]["UnlistedPayloadFiles"] = [{
        "Path": "Fixture.Package.xml", "PackagePath": UNLISTED_PATH, "Bytes": len(UNLISTED_BYTES),
        "SHA256": sha256 or hashlib.sha256(UNLISTED_BYTES).hexdigest(), "CopiedBy": "the fixture package's build targets"}]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence-dir", required=True)
    parser.add_argument("--source-packet-sha256", help="Exact frozen source packet used by a disposable validation checkout without Git metadata")
    options = parser.parse_args()
    if options.source_packet_sha256 is not None and (len(options.source_packet_sha256) != 64 or
            any(c not in "0123456789abcdefABCDEF" for c in options.source_packet_sha256)):
        raise SystemExit("source packet SHA-256 must be exactly 64 hexadecimal digits")
    evidence = pathlib.Path(options.evidence_dir).resolve()
    if evidence.exists():
        raise SystemExit("evidence directory must be new")
    evidence.mkdir(parents=True)
    artifacts = evidence / "artifacts"
    build = subprocess.run([
        "dotnet", "build", str(ROOT / "eng/ArtifactSbom/ArtifactSbom.csproj"), "-c", "Release",
        "--artifacts-path", str(artifacts), "-p:RestoreLockedMode=true", "--disable-build-servers", "--nologo"
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
            mutate_restored=None, expected_package_comment=None, rid="win-x64", setup=None, extra_environment=None,
            expected_apphost=None, expected_unlisted=()):
        folder = evidence / "cases" / name
        payload, index, packages_root = prepare(folder, include_runtime=include_runtime, package_hash=package_hash,
                                                runtime_hash=runtime_hash, package_id=package_id,
                                                reference_id=reference_id, reference_hash=reference_hash,
                                                extra_package=extra_package, projects=projects,
                                                project_runtime=project_runtime, project_files=project_files)
        if setup:
            setup(folder, payload, index, packages_root)
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
        command = ["dotnet", str(tool), str(payload), str(index), "1.0.0-rc.1", rid, kind,
                   "a" * 40, "2026-10-28T12:00:00Z", str(output)] + list(extras)
        environment = dict(os.environ)
        environment["NUGET_PACKAGES"] = str(packages_root)
        environment.update({key: value(folder) for key, value in (extra_environment or {}).items()})
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
            # The apphost package, what is generated from it and how; the files expanded from a package's archive.
            names_by_id = {value["SPDXID"]: value["fileName"] for value in files}
            comments_by_name = {value["fileName"]: value["comment"] for value in files}
            host = next((value for value in document["packages"] if value["SPDXID"].startswith("SPDXRef-AppHost-")), None)
            generated = sorted(names_by_id[value["spdxElementId"]] for value in document["relationships"]
                               if value["relationshipType"] == "GENERATED_FROM" and host and value["relatedSpdxElement"] == host["SPDXID"])
            details["AppHost"] = None if host is None else {
                "Name": host["name"], "Version": host["versionInfo"], "License": host["licenseDeclared"],
                "Purl": host["externalRefs"][0]["referenceLocator"],
                "DependedOn": any(value["spdxElementId"] == "SPDXRef-Package-FileCat" and value["relationshipType"] == "DEPENDS_ON"
                                  and value["relatedSpdxElement"] == host["SPDXID"] for value in document["relationships"]),
                "Generated": generated, "Relations": [comments_by_name[name] for name in generated],
            }
            details["Unlisted"] = sorted(names_by_id[value["spdxElementId"]] for value in document["relationships"]
                                         if value["relationshipType"] == "EXPANDED_FROM_ARCHIVE")
            if expected_apphost is None:
                passed = passed and details["AppHost"] is None
            else:
                (host_name, host_version, launcher, relation) = expected_apphost
                actual = details["AppHost"]
                passed = (passed and actual is not None and actual["Name"] == host_name and actual["Version"] == host_version
                          and actual["License"] == "MIT" and actual["Purl"] == f"pkg:nuget/{host_name}@{host_version}"
                          and actual["DependedOn"] and actual["Generated"] == [launcher] and relation in actual["Relations"][0])
            passed = passed and details["Unlisted"] == sorted(expected_unlisted)
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
    # Archive projects retain BSD parts; the separate XADMaster port and SharpCompress keep their component terms.
    run("project-licenses", True, projects=("FileCat.Core", "FileCat.Archives", "FileCat.Archives.Writing", "FileCat.Archives.Rar15", "FileCat.SharpCompress"),
        expected_project_licenses={
            "FileCat.Archives.Rar15": "LGPL-2.1-or-later",
            "FileCat.Core": "MIT",
            "FileCat.Archives": "MIT AND BSD-2-Clause",
            "FileCat.Archives.Writing": "MIT AND BSD-2-Clause",
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

    # The .NET apphost. A launcher carrying the apphost's binding check must be a reviewed template of the payload's RID,
    # bound to the DLL beside it and changed only as each platform changes it: Windows adds the DLL's Win32 resources, the
    # GUI subsystem and an Authenticode signature; macOS replaces the code signature; Linux changes nothing else.
    def apphost(fmt, rid, **options):
        return lambda _folder, payload, index, packages_root: add_apphost(payload, index, packages_root, fmt=fmt, rid=rid, **options)

    pe_relation = "a .rsrc section with exactly FileCat.dll's 2 Win32 resources; an Authenticode signature"
    run("apphost-pe", True, setup=apphost("pe", "win-x64"),
        expected_apphost=("Microsoft.NETCore.App.Host.win-x64", "10.0.14", "./FileCat.exe", pe_relation))
    run("apphost-pe-unsigned-fdd", True, kind="fdd", include_runtime=False, setup=apphost("pe", "win-x64", signed=False),
        expected_apphost=("Microsoft.NETCore.App.Host.win-x64", "10.0.14", "./FileCat.exe", "FileCat.dll's 2 Win32 resources"))
    run("apphost-elf", True, kind="fdd", include_runtime=False, rid="linux-x64", setup=apphost("elf", "linux-x64"),
        expected_apphost=("Microsoft.NETCore.App.Host.linux-x64", "10.0.14", "./FileCat", "byte for byte the template with the name written in"))
    run("apphost-macho", True, kind="fdd", include_runtime=False, rid="osx-arm64", setup=apphost("macho", "osx-arm64"),
        expected_apphost=("Microsoft.NETCore.App.Host.osx-arm64", "10.0.14", "./FileCat", "an ad-hoc signature replaces the template's"))
    # The SDK prefers a pack in its own packs folder, and restore then leaves none: the template there must still be the
    # reviewed one.
    run("apphost-from-sdk-packs", True, kind="fdd", include_runtime=False, rid="linux-x64",
        setup=lambda folder, payload, index, root: add_apphost(payload, index, root, fmt="elf", rid="linux-x64", restored=False,
                                                               packs_root=folder / "dotnet"),
        extra_environment={"DOTNET_ROOT": lambda folder: str(folder / "dotnet")},
        expected_apphost=("Microsoft.NETCore.App.Host.linux-x64", "10.0.14", "./FileCat", "byte for byte"))
    run("apphost-pe-code-changed", False, setup=apphost("pe", "win-x64", change=lambda data: data.replace(b"pe-code-fixture;", b"pe-code-fixturE;", 1)))
    run("apphost-pe-resources-not-the-dll", False,
        setup=lambda _folder, payload, index, root: (add_apphost(payload, index, root, fmt="pe", rid="win-x64"),
                                                     (payload / "FileCat.dll").write_bytes(pe([(b".text", b"other")], resources=RESOURCES[:1]))))
    run("apphost-pe-bytes-after-signature", False, setup=apphost("pe", "win-x64", change=lambda data: data + b"appended"))
    run("apphost-elf-changed", False, kind="fdd", include_runtime=False, rid="linux-x64",
        setup=apphost("elf", "linux-x64", change=lambda data: data.replace(b"elf-code;", b"elf-codE;", 1)))
    run("apphost-macho-changed-before-signature", False, kind="fdd", include_runtime=False, rid="osx-arm64",
        setup=apphost("macho", "osx-arm64", change=lambda data: data.replace(b"macho-code;", b"macho-codE;", 1)))
    ad_hoc_signature = len(code_signature(0x2, b""))
    run("apphost-macho-signature-not-a-superblob", False, kind="fdd", include_runtime=False, rid="osx-arm64",
        setup=apphost("macho", "osx-arm64", change=lambda data: data[:-ad_hoc_signature] + bytes(ad_hoc_signature)))
    run("apphost-bound-to-another-app", False, kind="fdd", include_runtime=False, rid="linux-x64",
        setup=apphost("elf", "linux-x64", bound_to="Other.dll"))
    run("apphost-unreviewed", False, kind="fdd", include_runtime=False, rid="linux-x64",
        setup=apphost("elf", "linux-x64", index_change=lambda value: value.pop("AppHostPacks")))
    run("apphost-template-pin-differs", False, kind="fdd", include_runtime=False, rid="linux-x64",
        setup=apphost("elf", "linux-x64", index_change=lambda value: value["AppHostPacks"][0].update(TemplateSHA256="0" * 64)))
    run("apphost-retained-text-not-its-own", False, kind="fdd", include_runtime=False, rid="linux-x64",
        setup=apphost("elf", "linux-x64", index_change=lambda value: value["Files"][-1].update(SHA256="0" * 64)))
    run("apphost-other-release-than-runtime", False, setup=apphost("pe", "win-x64", version="10.0.13"))

    # Files a package's build targets copy without the dependency manifest naming them ship only as the reviewed index
    # declares them, byte for byte; any other payload file that is a reviewed archive's member is refused. A license text
    # the index retains, and an empty file, are no components.
    def payload_file(path, data):
        def write(_folder, payload, _index, _root):
            (payload / path).parent.mkdir(parents=True, exist_ok=True)
            (payload / path).write_bytes(data)
        return write

    def both(*setups):
        return lambda *arguments: [step(*arguments) for step in setups]

    run("unlisted-declared", True, setup=payload_file("Fixture.Package.xml", UNLISTED_BYTES),
        mutate=lambda _payload, index: edit_json(index, declare_unlisted), expected_unlisted=["./Fixture.Package.xml"],
        expected_package_comment="./Fixture.Package.xml (lib/net10.0/Fixture.Package.xml)")
    run("unlisted-declared-absent", True, mutate=lambda _payload, index: edit_json(index, declare_unlisted))
    run("unlisted-undeclared", False, setup=payload_file("Fixture.Package.xml", UNLISTED_BYTES))
    run("unlisted-undeclared-elsewhere", False, setup=payload_file("docs/copy.xml", UNLISTED_BYTES))
    run("unlisted-declared-differs", False, setup=payload_file("Fixture.Package.xml", b"changed documentation"),
        mutate=lambda _payload, index: edit_json(index, declare_unlisted))
    run("unlisted-pin-differs", False, setup=payload_file("Fixture.Package.xml", UNLISTED_BYTES),
        mutate=lambda _payload, index: edit_json(index, lambda value: declare_unlisted(value, "0" * 64)))
    run("runtime-member-copy-undeclared", False,
        setup=payload_file("extra/coreclr.fixture.dll", RUNTIME_MEMBERS["runtimes/win-x64/native/coreclr.fixture.dll"]))
    run("retained-license-text-anywhere", True,
        setup=both(payload_file("licenses/dependencies/fixture/LICENSE", LICENSE_BYTES), payload_file("licenses/other/COPYING", LICENSE_BYTES)))
    run("license-text-not-retained", False, setup=payload_file("licenses/other/COPYING", LICENSE_BYTES),
        mutate=lambda _payload, index: edit_json(index, lambda value: value["Files"].clear()))
    run("empty-file-not-attributed", True, setup=payload_file("FileCat.portable", b""))
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
        "SourceCommit": None if options.source_packet_sha256 else subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "SourceTreeDirty": None if options.source_packet_sha256 else bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip()),
        "SourcePacketSHA256": options.source_packet_sha256.lower() if options.source_packet_sha256 else None,
        "SourceIdentityMethod": "frozen source packet; caller verifies extraction/PDB inputs" if options.source_packet_sha256 else "Git checkout",
        "SourceRoot": str(ROOT), "ArtifactsRoot": str(artifacts),
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
