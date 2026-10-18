#!/usr/bin/env python3
"""Run already-built FileCat tests as a real ordinary Linux user in a private copy.

Root-only container helper. It never changes the checkout, SDK, system accounts, or
their permissions. See docs/validation/LINUX_UNPRIVILEGED_TESTS.md.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import pwd
import shutil
import signal
import stat
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET


CAPABILITY_FIELDS = ("CapInh", "CapPrm", "CapEff", "CapBnd", "CapAmb")
MAX_EVIDENCE_BYTES = 128 * 1024 * 1024


def parse_status(text):
    """Require every identity/capability field, including filesystem IDs."""
    fields = {}
    for line in text.splitlines():
        key, separator, value = line.partition(":")
        if separator:
            fields[key] = value.strip()
    result = {key: int(fields[key], 16) for key in CAPABILITY_FIELDS}
    result["NoNewPrivs"] = int(fields["NoNewPrivs"])
    result["Uid"] = [int(value) for value in fields["Uid"].split()]
    result["Gid"] = [int(value) for value in fields["Gid"].split()]
    result["Groups"] = [int(value) for value in fields["Groups"].split()]
    return result


def require_ordinary_identity(snapshot, uid, gid):
    if uid <= 0 or gid <= 0:
        raise ValueError("The test identity must have a nonzero user and group ID.")
    if snapshot["Uid"] != [uid] * 4 or snapshot["Gid"] != [gid] * 4:
        raise ValueError("Real, effective, saved and filesystem IDs were not all dropped.")
    if snapshot["Groups"] or any(snapshot[key] != 0 for key in CAPABILITY_FIELDS):
        raise ValueError("Supplementary groups or Linux capabilities remain.")
    if snapshot["NoNewPrivs"] != 1:
        raise ValueError("The child does not have no_new_privs set.")


def require_permission_denied(action, label):
    try:
        action()
    except PermissionError:
        return label
    raise RuntimeError("The unprivileged child unexpectedly succeeded: " + label)


def open_and_close(path, flags):
    descriptor = os.open(path, flags)
    os.close(descriptor)


def child(config_path, evidence_fd):
    """Verify the kernel state before any test or .NET code can execute."""
    config = json.loads(Path(config_path).read_text(encoding="utf-8"))
    with open("/proc/self/status", encoding="ascii") as status:
        snapshot = parse_status(status.read(65536))
    require_ordinary_identity(snapshot, config["uid"], config["gid"])
    probes = [
        require_permission_denied(lambda: os.setresuid(0, 0, 0), "regain root user IDs"),
        require_permission_denied(lambda: os.setresgid(0, 0, 0), "regain root group IDs"),
        require_permission_denied(lambda: os.setgroups([0]), "add the root supplementary group"),
        require_permission_denied(lambda: open_and_close(config["root_canary"], os.O_RDONLY),
                                  "read a root-only file"),
        require_permission_denied(lambda: open_and_close(config["readonly_canary"], os.O_WRONLY),
                                  "write an owner-readable file without write permission"),
    ]
    # This descriptor was opened by the root launcher. It is closed before exec,
    # so tests cannot rewrite the retained identity evidence through a pathname.
    with os.fdopen(int(evidence_fd), "w", encoding="utf-8") as evidence:
        json.dump({"verified": True, "pid": os.getpid(), "kernel": snapshot,
                   "denied_probes": probes, "command": config["command"]}, evidence, indent=2)
        evidence.write("\n")
    os.chdir(config["cwd"])
    os.execv(config["command"][0], config["command"])


def child_environment(stage):
    # Start from an allowlist: in particular no SUDO identity, injection hooks,
    # live-device test opt-ins, inherited user data locations, or credentials.
    return {
        "PATH": "/usr/bin:/bin",
        "LANG": "C.UTF-8",
        "LC_ALL": "C.UTF-8",
        "PYTHONDONTWRITEBYTECODE": "1",
        "DOTNET_ROOT": str(stage / "dotnet"),
        "DOTNET_CLI_HOME": str(stage / "state" / "dotnet"),
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        "DOTNET_MULTILEVEL_LOOKUP": "0",
        "DOTNET_GCHeapHardLimit": "0x60000000",
        "TMPDIR": str(stage / "state" / "tmp"),
        "XDG_CONFIG_HOME": str(stage / "state" / "config"),
        "XDG_CACHE_HOME": str(stage / "state" / "cache"),
        "XDG_DATA_HOME": str(stage / "state" / "data"),
        "XDG_RUNTIME_DIR": str(stage / "state" / "runtime"),
    }


def copy_tree(source, destination, uid, gid, writable):
    # copy2 preserves executable apphosts. Hard links are deliberately avoided:
    # test fixtures may rename/write their private copied apphost and test data.
    shutil.copytree(source, destination, symlinks=True)
    for parent, directories, files in os.walk(destination, followlinks=False):
        for name in ["."] + directories + files:
            path = Path(parent) / name
            mode = path.lstat().st_mode
            if stat.S_ISLNK(mode):
                # A staged link must not escape to inaccessible or mutable
                # originals; ordinary compiler outputs and this SDK have none.
                resolved = path.resolve(strict=True)
                if not resolved.is_relative_to(destination):
                    raise ValueError("A copied symbolic link escapes its staged tree: " + str(path))
                os.chown(path, uid if writable else 0, gid, follow_symlinks=False)
                continue
            if not (stat.S_ISDIR(mode) or stat.S_ISREG(mode)):
                raise ValueError("Only regular files/directories may be staged: " + str(path))
            os.chown(path, uid if writable else 0, gid)
            executable = stat.S_ISDIR(mode) or bool(mode & 0o111)
            os.chmod(path, (0o700 if executable else 0o600) if writable
                     else (0o750 if executable else 0o640))


def copy_evidence(source, destination):
    """Collect one bounded regular result; never follow child-controlled links."""
    descriptor = os.open(source, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
    with os.fdopen(descriptor, "rb") as incoming:
        info = os.fstat(incoming.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_size > MAX_EVIDENCE_BYTES:
            raise ValueError("The result must be a bounded regular file.")
        with open(destination, "xb") as outgoing:
            remaining = MAX_EVIDENCE_BYTES + 1
            while remaining:
                block = incoming.read(min(65536, remaining))
                if not block:
                    return
                outgoing.write(block)
                remaining -= len(block)
            raise ValueError("The result grew beyond its evidence budget.")


def result_counters(path):
    root = ET.parse(path).getroot()
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    summary = root.find("t:ResultSummary", ns)
    counters = None if summary is None else summary.find("t:Counters", ns)
    if counters is None:
        raise ValueError("The test runner did not retain a TRX result summary.")
    reported = {key: int(value) for key, value in counters.attrib.items()}
    required = {"total", "executed", "passed", "failed", "notExecuted"}
    if not required.issubset(reported) or any(value < 0 for value in reported.values()):
        raise ValueError("The TRX result counters are missing or invalid.")
    records = root.findall("t:Results/t:UnitTestResult", ns)
    if reported["total"] <= 0 or len(records) != reported["total"]:
        raise ValueError("The test filter did not execute a complete, nonempty test selection.")
    execution_ids = set()
    observed = {"Passed": 0, "Failed": 0, "NotExecuted": 0}
    for record in records:
        execution_id = record.get("executionId")
        outcome = record.get("outcome")
        if not execution_id or execution_id in execution_ids or outcome not in observed:
            raise ValueError("The TRX contains duplicate, missing, or unfinished test results.")
        execution_ids.add(execution_id)
        observed[outcome] += 1
    # xUnit/VSTest can leave the aggregate notExecuted counter at zero while
    # retaining one NotExecuted result per native/opt-in skip. Require every
    # distinct result and reconcile the executed outcomes before counting skips.
    if (reported["passed"] != observed["Passed"] or reported["failed"] != observed["Failed"]
            or reported["executed"] != observed["Passed"] + observed["Failed"]
            or reported["notExecuted"] not in (0, observed["NotExecuted"])):
        raise ValueError("The TRX counters do not match its complete test results.")
    counts = reported | {"notExecuted": observed["NotExecuted"]}
    return {"outcome": summary.attrib["outcome"], "counters": counts, "reported_counters": reported}


def terminate_group(process):
    # Own process group only; do not target other agents' builds/test processes.
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    process.wait()


def run(args):
    if sys.platform != "linux" or os.getuid() != 0 or os.geteuid() != 0:
        raise ValueError("Use this helper only in a Linux root container; ordinary users run dotnet test directly.")
    account = pwd.getpwnam(args.user)
    uid, gid = account.pw_uid, account.pw_gid
    if uid == 0 or gid == 0:
        raise ValueError("Select an existing account with a nonzero user and primary group ID.")
    dotnet = args.dotnet.resolve(strict=True)
    assembly = args.assembly.resolve(strict=True)
    settings = args.settings.resolve(strict=True)
    if not dotnet.is_file() or not os.access(dotnet, os.X_OK):
        raise ValueError("--dotnet must name the pinned SDK's executable.")
    if not assembly.is_file() or assembly.suffix != ".dll" or not assembly.name.startswith("FileCat."):
        raise ValueError("--assembly must name an already-built FileCat test assembly.")
    repo = Path(__file__).resolve().parents[2]
    sdk_version = json.loads((repo / "global.json").read_text(encoding="utf-8"))["sdk"]["version"]
    vstest = Path("sdk") / sdk_version / "vstest.console.dll"
    if not (dotnet.parent / vstest).is_file():
        raise ValueError("The supplied SDK does not contain the global.json-pinned VSTest runner.")
    setpriv = shutil.which("setpriv")
    python = Path("/usr/bin/python3")
    if not setpriv or not python.is_file():
        raise ValueError("This helper requires util-linux setpriv and /usr/bin/python3.")
    if not shutil.rmtree.avoids_symlink_attacks:
        raise ValueError("This helper requires descriptor-relative temporary-tree cleanup.")
    if args.timeout <= 0:
        raise ValueError("--timeout must be positive.")
    results = args.results.absolute()
    results.mkdir(mode=0o700, parents=True, exist_ok=False)
    with open(assembly, "rb") as compiled:
        digest = hashlib.file_digest(compiled, "sha256").hexdigest()
    receipt = {"assembly": str(assembly), "assembly_sha256": digest,
               "sdk_version": sdk_version, "user": account.pw_name, "uid": uid, "gid": gid,
               "filter": args.filter, "timeout_seconds": args.timeout, "settings": str(settings)}
    stage = Path(tempfile.mkdtemp(prefix="filecat-unprivileged-tests-", dir="/tmp"))
    started = time.monotonic()
    process = None
    code = 1
    try:
        os.chown(stage, 0, gid)
        # Keep the stage root-only until all recursive copies, ownership changes
        # and configuration writes have finished. An existing process running as
        # the selected account cannot race staging through its writable copies.
        print("Staging a private SDK and test output copy at " + str(stage), flush=True)
        copy_tree(dotnet.parent, stage / "dotnet", uid, gid, writable=False)
        copy_tree(assembly.parent, stage / "tests", uid, gid, writable=True)
        for relative in ("state", "state/dotnet", "state/tmp", "state/config", "state/cache",
                         "state/data", "state/runtime", "state/work", "results"):
            directory = stage / relative
            directory.mkdir(mode=0o700)
            os.chown(directory, uid, gid)
        # The directory containing copied code/config/canaries remains owned by
        # root. The test identity cannot replace these entries before verification.
        for name, source in (("runner.py", Path(__file__)), ("tests.runsettings", settings)):
            shutil.copyfile(source, stage / name)
            os.chown(stage / name, 0, gid)
            os.chmod(stage / name, 0o640)
        # Retain the exact root-owned settings copy the child will use, including
        # its collection concurrency; later source-file edits cannot rewrite it.
        copy_evidence(stage / "tests.runsettings", results / "tests.runsettings")
        with open(results / "tests.runsettings", "rb") as retained_settings:
            receipt["settings_sha256"] = hashlib.file_digest(retained_settings, "sha256").hexdigest()
        for name, owner in (("root-canary", 0), ("readonly-canary", uid)):
            (stage / name).write_text("permission probe\n", encoding="ascii")
            os.chown(stage / name, owner, gid)
            os.chmod(stage / name, 0o400)
        command = [str(stage / "dotnet" / dotnet.name), "exec", str(stage / "dotnet" / vstest),
                   str(stage / "tests" / assembly.name), "/Settings:" + str(stage / "tests.runsettings"),
                   "/Logger:trx;LogFileName=results.trx", "/ResultsDirectory:" + str(stage / "results")]
        if args.filter:
            command.append("/TestCaseFilter:" + args.filter)
        config = {"uid": uid, "gid": gid, "root_canary": str(stage / "root-canary"),
                  "readonly_canary": str(stage / "readonly-canary"),
                  "cwd": str(stage / "state" / "work"), "command": command}
        (stage / "config.json").write_text(json.dumps(config), encoding="utf-8")
        os.chown(stage / "config.json", 0, gid)
        os.chmod(stage / "config.json", 0o640)
        os.chmod(stage, 0o750)
        print("Dropping all user/group IDs, supplementary groups and capabilities before tests.", flush=True)
        with open(results / "identity.json", "x", encoding="utf-8") as identity, \
                open(results / "tests.log", "xb") as log:
            drop = [setpriv, "--reuid=" + str(uid), "--regid=" + str(gid), "--clear-groups",
                    "--bounding-set=-all", "--inh-caps=-all", "--ambient-caps=-all", "--no-new-privs",
                    str(python), str(stage / "runner.py"), "--child", str(stage / "config.json"), str(identity.fileno())]
            process = subprocess.Popen(drop, cwd=stage, env=child_environment(stage),
                                       stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT,
                                       pass_fds=(identity.fileno(),), start_new_session=True)
            try:
                receipt["test_exit_code"] = process.wait(timeout=args.timeout)
            except subprocess.TimeoutExpired:
                receipt["timed_out"] = True
                terminate_group(process)
                code = 124
                return code
        identity_record = json.loads((results / "identity.json").read_text(encoding="utf-8"))
        require_ordinary_identity(identity_record["kernel"], uid, gid)
        copy_evidence(stage / "results" / "results.trx", results / "results.trx")
        receipt["result"] = result_counters(results / "results.trx")
        code = receipt["test_exit_code"]
        if receipt["result"]["outcome"] not in ("Completed", "Passed") or receipt["result"]["counters"]["failed"]:
            code = code or 1
        print("Unprivileged tests exited " + str(code) + "; evidence: " + str(results), flush=True)
        return code
    except BaseException as error:
        receipt["error"] = type(error).__name__ + ": " + str(error)
        raise
    finally:
        if process is not None and process.poll() is None:
            terminate_group(process)
        receipt["exit_code"] = code
        receipt["elapsed_seconds"] = round(time.monotonic() - started, 3)
        (results / "run.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        # Linux shutil.rmtree uses descriptor-relative traversal, including when
        # a test has left symlinks or made its own fixture read-only.
        shutil.rmtree(stage)


def main():
    if len(sys.argv) == 4 and sys.argv[1] == "--child":
        child(sys.argv[2], sys.argv[3])
        return 1
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", required=True, type=Path)
    parser.add_argument("--assembly", required=True, type=Path)
    parser.add_argument("--results", required=True, type=Path, help="New directory for TRX, log and identity evidence")
    parser.add_argument("--settings", type=Path, default=Path(__file__).resolve().parents[2] / "docs/validation/ux-s1.runsettings")
    parser.add_argument("--filter", help="Optional VSTest test-case filter")
    parser.add_argument("--user", default="nobody", help="Existing ordinary account; no account is created")
    parser.add_argument("--timeout", type=int, default=1800, help="Test process-tree timeout in seconds")
    return run(parser.parse_args())


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError, RuntimeError) as error:
        print("Unprivileged test runner: " + str(error), file=sys.stderr)
        sys.exit(1)
