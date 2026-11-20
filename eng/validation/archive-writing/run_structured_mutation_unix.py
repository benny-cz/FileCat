#!/usr/bin/env python3
"""Run bounded, source-bound multi-target mutation on a disposable Unix test machine."""

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import signal
import subprocess
import tarfile


LANES = (
    ("sevenzip-strict", "seeds-sevenzip-strict-v14", 12011),
    ("rar5-strict", "seeds-rar5-strict-v15", 12013),
    ("sevenzip-password", "seeds-sevenzip-password-v16", 12017),
    ("rar5-password", "seeds-rar5-password-v16", 12019),
    ("journal-state", "seeds-journal-state", 12023),
)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def unpack(archive, destination):
    total = 0
    with tarfile.open(archive, "r:gz") as reader:
        members = reader.getmembers()
        if len(members) > 256:
            raise ValueError("Packet member count exceeded")
        for member in members:
            name = PurePosixPath(member.name)
            if name.is_absolute() or ".." in name.parts or not member.isfile() and not member.isdir():
                raise ValueError("Packet has an unsafe member")
            target = destination.joinpath(*name.parts)
            if member.isdir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            total += member.size
            if member.size > 1048576 or total > 16 * 1048576:
                raise ValueError("Packet exceeded bounded input size")
            target.parent.mkdir(parents=True, exist_ok=True)
            with reader.extractfile(member) as source, target.open("xb") as output:
                shutil.copyfileobj(source, output)


def run_logged(command, logfile, env, timeout=600):
    with logfile.open("xb") as output:
        result = subprocess.run(command, stdout=output, stderr=subprocess.STDOUT,
                                env=env, timeout=timeout, check=False)
    if result.returncode:
        raise RuntimeError(f"Command exited {result.returncode}: {command[0]} {command[1]}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--seconds", type=int, default=3600)
    parser.add_argument("--cpu-floor", type=int, default=14400)
    args = parser.parse_args()
    if os.geteuid() == 0 or not 1 <= args.seconds <= 86400 or args.cpu_floor < 0:
        parser.error("Use an ordinary user and bounded run/floor")
    task = args.task.resolve(strict=True)
    source = args.source.resolve(strict=True)
    dotnet = args.dotnet.resolve(strict=True)
    root = task / "structured-mutation-unix-v1"
    root.mkdir(mode=0o700, exist_ok=False)
    processes = []
    try:
        packet = task / "structured-mutation-harness-v1.tar.gz"
        seeds = task / "structured-mutation-seeds-v1.tar.gz"
        if sha(packet) != "ca7a04047cf09c5a32dab64cf42bcc64fd15f88aba97b47d9512387cc2288297" or \
           sha(seeds) != "c36d2e822b2505c04f77e96d23844e742da475803c7c2ce0224d344ae8f1297b":
            raise ValueError("Frozen harness or corpus packet changed")
        unpack(packet, root)
        unpack(seeds, root)
        core = source / "src/FileCat.Core/bin/Release/net10.0"
        archives = source / "src/FileCat.Archives/bin/Release/net10.0"
        for path in (core / "FileCat.Core.dll", archives / "FileCat.Archives.dll"):
            if not path.is_file():
                raise FileNotFoundError(path)
        environment = os.environ.copy()
        environment.update(DOTNET_ROOT=str(dotnet.parent),
                           PATH=str(dotnet.parent) + os.pathsep + environment.get("PATH", ""),
                           DOTNET_CLI_TELEMETRY_OPTOUT="1", MSBUILDDISABLENODEREUSE="1")
        props = [f"-p:FileCatFuzzProductDirectory={core}",
                 f"-p:FileCatFuzzArchiveDirectory={archives}"]
        project = root / "ArchiveFuzzHarness.csproj"
        run_logged([str(dotnet), "restore", str(project), "--locked-mode", "-p:NuGetAudit=false", *props],
                   root / "restore.log", environment)
        run_logged([str(dotnet), "build", str(project), "-c", "Release", "--no-restore",
                    "--disable-build-servers", "-m:4", "-p:NuGetAudit=false", *props],
                   root / "build.log", environment)
        image = root / "image"
        shutil.copytree(root / "bin/Release/net10.0", image)
        harness = image / "FileCat.ArchiveFuzzHarness.dll"
        identity_files = [packet, seeds, root / "Program.cs", root / "StructuredMutation.cs",
                          harness, image / "FileCat.Core.dll", image / "FileCat.Archives.dll"]
        (root / "source-binary-hashes.json").write_text(json.dumps(
            [{"path": str(path), "sha256": sha(path)} for path in identity_files], indent=2))
        (root / "account.json").write_text(json.dumps(
            {"uid": os.getuid(), "euid": os.geteuid(), "logical_cpus": os.cpu_count()}, indent=2))
        for target, folder, random_seed in LANES:
            corpus = root / folder
            manifest = json.loads((corpus / "manifest.json").read_text())
            if manifest["target"] != target:
                raise ValueError("Seed target changed: " + target)
            for row in manifest["seeds"]:
                path = corpus / row["name"]
                if path.stat().st_size != row["bytes"] or sha(path) != row["sha256"]:
                    raise ValueError("Synthetic seed changed: " + str(path))
            report = root / f"{target}-smoke.json"
            run_logged([str(dotnet), str(harness), "structured-mutation", target, str(corpus),
                        "2", str(random_seed), str(report)], root / f"{target}-smoke.log",
                       environment, timeout=120)
            facts = json.loads(report.read_text())
            if not facts["complete"] or facts["calls"] < len(manifest["seeds"]) or facts["accepted"] == 0:
                raise ValueError("Smoke result incomplete: " + target)
        (root / "status.txt").write_text("RUNNING\n")
        for target, folder, random_seed in LANES:
            output = (root / f"{target}-long.log").open("xb")
            command = [str(dotnet), str(harness), "structured-mutation", target,
                       str(root / folder), str(args.seconds), str(random_seed),
                       str(root / f"{target}-long.json")]
            process = subprocess.Popen(command, cwd=root, env=environment, stdout=output,
                                       stderr=subprocess.STDOUT, start_new_session=True)
            processes.append((target, process, output))
        (root / "workers.json").write_text(json.dumps(
            [{"target": target, "pid": process.pid} for target, process, _ in processes], indent=2))
        for target, process, _ in processes:
            if process.wait(timeout=args.seconds + 300) != 0:
                raise RuntimeError("Mutation worker failed: " + target)
        results = [json.loads((root / f"{target}-long.json").read_text()) for target, _, _ in processes]
        if any(not row["complete"] or row["failure"] or row["calls"] <= 1000 or row["accepted"] == 0
               for row in results):
            raise ValueError("A structured mutation report is incomplete")
        if sum(row["process_cpu_seconds"] for row in results) < args.cpu_floor:
            raise ValueError("Structured mutation did not reach the platform CPU floor")
        (root / "summary.json").write_text(json.dumps(results, indent=2))
        (root / "status.txt").write_text("PASS\n")
    except Exception as error:
        (root / "error.txt").write_text(repr(error) + "\n")
        (root / "status.txt").write_text("FAIL\n")
        raise
    finally:
        for _, process, output in processes:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()
            output.close()
        members = ["Program.cs", "StructuredMutation.cs", "ArchiveFuzzHarness.csproj",
                   "packages.lock.json", "restore.log", "build.log", "account.json",
                   "source-binary-hashes.json", "status.txt"]
        members += [path.name for path in root.glob("*-smoke.*")]
        members += [path.name for path in root.glob("*-long.*")]
        members += [path.name for path in root.glob("*.failure.bin")]
        members += [path.name for path in root.glob("*.json") if path.name not in members]
        members += [path.name for path in root.glob("*.txt") if path.name not in members]
        members += ["image/FileCat.ArchiveFuzzHarness.dll", "image/FileCat.Core.dll", "image/FileCat.Archives.dll"]
        with tarfile.open(task / "structured-mutation-unix-v1-evidence.tar.gz", "w:gz") as archive:
            for name in dict.fromkeys(members):
                path = root / name
                if path.is_file():
                    archive.add(path, arcname=name, recursive=False)


if __name__ == "__main__":
    main()
