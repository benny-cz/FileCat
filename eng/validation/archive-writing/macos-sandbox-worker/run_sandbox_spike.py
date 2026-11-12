#!/usr/bin/env python3
"""macOS App Sandbox worker spike (route decision of 12 November 2026).

Builds the self-contained probe into a throwaway bundle, signs a copy of its apphost with only the App Sandbox
entitlement, and records what that sandboxed .NET runtime can do: denials, process creation under a zero process
limit, inherited descriptors, parent-exit notification, and the parent's footprint monitor. Run as the ordinary user:

    python3 -I run_sandbox_spike.py <fresh owned work folder>

Writes <work>/results.json. Leaves no process running. Validation tooling only; nothing here is shipped.
"""
import ctypes, hashlib, json, os, plistlib, resource, shutil, signal, socket, subprocess, sys, threading, time

HERE = os.path.dirname(os.path.abspath(__file__))
WORK = os.path.abspath(sys.argv[1])
DOTNET = os.path.expanduser("~/.dotnet/dotnet")
BUNDLE_ID = "io.filecat.sandbox-spike"
WORKER_ID = BUNDLE_ID + ".worker"
LAUNCHERS = [("SandboxLauncher", WORKER_ID, None), ("SandboxLauncherVariant", WORKER_ID, "FILECAT_VARIANT_TAG"),
             ("SandboxLauncherNoBootstrap", WORKER_ID + ".nobootstrap", "FILECAT_NULL_BOOTSTRAP")]
results = {"started": time.strftime("%Y-%m-%dT%H:%M:%S%z")}
children = []


def log(message):
    results.setdefault("phases", []).append(time.strftime("%H:%M:%S ") + message)
    if os.path.isdir(WORK):  # keep what is known so far, should a later phase stop
        with open(os.path.join(WORK, "results.json"), "w") as f:
            json.dump(results, f, indent=2)
    print(time.strftime("%H:%M:%S"), message, flush=True)


def run(command, **kwargs):
    return subprocess.run(command, capture_output=True, text=True, **kwargs)


libproc = ctypes.CDLL("/usr/lib/libproc.dylib", use_errno=True)


def footprint(pid):
    """Physical footprint (rusage_info_v4.ri_phys_footprint) of a process, or None once it is gone."""
    buffer = (ctypes.c_uint64 * 64)()
    if libproc.proc_pid_rusage(pid, 4, ctypes.byref(buffer)) != 0:
        return None
    return int(buffer[9])


def build():
    if os.path.exists(WORK) and os.listdir(WORK):
        sys.exit("work folder must be new or empty")
    os.makedirs(WORK, exist_ok=True)
    source = os.path.join(WORK, "source")
    shutil.copytree(os.path.join(HERE, "SandboxProbe"), source)
    app = os.path.join(WORK, "Spike.app")
    macos = os.path.join(app, "Contents", "MacOS")
    publish = run([DOTNET, "publish", source, "-c", "Release", "-r", "osx-arm64", "--self-contained", "true", "-o", macos,
                   "-p:UseAppHost=true", "-nologo"], env={**os.environ, "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1"})
    results["publish"] = {"exit": publish.returncode, "tail": publish.stdout[-600:] + publish.stderr[-600:]}
    if publish.returncode != 0:
        raise SystemExit("publish failed")
    shutil.copy2(os.path.join(macos, "SandboxProbe"), os.path.join(macos, "SandboxProbeWorker"))
    # The launchers: C programs with an embedded Info.plist, so the sandbox can name their container. The variant has
    # different bytes under the same identifier (a rebuilt launcher meeting an existing container); the last one also
    # gives up its bootstrap port before exec, under an identifier of its own.
    clang, sdk = "/Library/Developer/CommandLineTools/usr/bin/clang", "/Library/Developer/CommandLineTools/SDKs/MacOSX.sdk"
    results["launcherCompile"] = {"clang": run([clang, "--version"]).stdout.splitlines()[0], "sdk": os.path.realpath(sdk)}
    for name, identifier, define in LAUNCHERS:
        plist = os.path.join(WORK, identifier + "-Info.plist")
        with open(plist, "wb") as f:
            plistlib.dump({"CFBundleIdentifier": identifier, "CFBundleName": "FileCat Sandbox Spike Worker", "CFBundleVersion": "1",
                           "CFBundleShortVersionString": "1", "CFBundleInfoDictionaryVersion": "6.0"}, f)
        compiled = run([clang, "-isysroot", sdk, "-std=c11", "-O2", "-Wall", "-Wextra", "-Werror", *(["-D" + define] if define else []),
                        "-sectcreate", "__TEXT", "__info_plist", plist, "-o", os.path.join(macos, name), os.path.join(HERE, "trampoline.c")])
        results["launcherCompile"][name] = {"exit": compiled.returncode, "output": (compiled.stdout + compiled.stderr)[-400:]}
        if compiled.returncode != 0:
            raise SystemExit("launcher compile failed")
    with open(os.path.join(app, "Contents", "Info.plist"), "wb") as f:
        plistlib.dump({"CFBundleIdentifier": BUNDLE_ID, "CFBundleName": "FileCat Sandbox Spike", "CFBundleExecutable": "SandboxProbe",
                       "CFBundlePackageType": "APPL", "CFBundleVersion": "1", "CFBundleShortVersionString": "1",
                       "LSMinimumSystemVersion": "26.0"}, f)
    entitlements = os.path.join(WORK, "sandbox.plist")
    with open(entitlements, "wb") as f:
        plistlib.dump({"com.apple.security.app-sandbox": True}, f)
    steps = [
        ["codesign", "--force", "--deep", "--sign", "-", "--timestamp=none", app],
        ["codesign", "--force", "--sign", "-", "--timestamp=none", "--identifier", WORKER_ID, "--entitlements", entitlements,
         os.path.join(macos, "SandboxProbeWorker")],
        *[["codesign", "--force", "--sign", "-", "--timestamp=none", "--identifier", identifier, "--entitlements", entitlements,
           os.path.join(macos, name)] for name, identifier, _ in LAUNCHERS],
        ["codesign", "--force", "--sign", "-", "--timestamp=none", app],
        ["codesign", "--verify", "--deep", "--strict", app],
    ]
    results["signing"] = []
    for step in steps:
        done = run(step)
        results["signing"].append({"command": " ".join(os.path.basename(a) if a.startswith(WORK) else a for a in step),
                                   "exit": done.returncode, "output": (done.stdout + done.stderr)[-400:]})
        if done.returncode != 0:
            raise SystemExit("signing failed: " + done.stderr)
    shown = run(["codesign", "-d", "--entitlements", "-", "--xml", os.path.join(macos, "SandboxProbeWorker")])
    results["workerEntitlements"] = shown.stdout[-400:]
    shown = run(["codesign", "-d", "--entitlements", "-", "--xml", os.path.join(macos, "SandboxProbe")])
    results["controlEntitlements"] = shown.stdout[-400:]
    results["launcherSignature"] = {}
    for name, _, _ in LAUNCHERS:
        shown = run(["codesign", "-d", "-v", os.path.join(macos, name)])
        results["launcherSignature"][name] = [l for l in (shown.stdout + shown.stderr).splitlines() if l.startswith(("Identifier", "Info.plist", "CDHash"))]
    results["bareCopy"] = probe(os.path.join(macos, "SandboxProbeWorker"), ["exit0"])
    return os.path.join(macos, "SandboxProbe"), {name: os.path.join(macos, name) for name, _, _ in LAUNCHERS}


def probe(executable, args, limits=None, stdin=None, pass_fds=(), timeout=60):
    def prepare():
        for resource_id, value in (limits or {}).items():
            resource.setrlimit(resource_id, (value, value))
    started = time.monotonic()
    try:
        done = subprocess.run([executable, *args], input=stdin, capture_output=True, timeout=timeout,
                              preexec_fn=prepare if limits else None, pass_fds=pass_fds)
    except subprocess.TimeoutExpired:
        return {"exit": "timeout", "seconds": timeout}
    elapsed = round(time.monotonic() - started, 3)
    out = done.stdout.decode("utf-8", "replace")
    try:
        parsed = json.loads(out)
    except ValueError:
        parsed = out[-1500:]
    return {"exit": done.returncode, "seconds": elapsed, "output": parsed, "stderr": done.stderr.decode("utf-8", "replace")[-800:]}


def listener():
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.bind(("127.0.0.1", 0)); server.listen(4)
    accepted = []

    def serve():
        while True:
            try:
                connection, _ = server.accept()
            except OSError:
                return
            accepted.append(1); connection.close()
    threading.Thread(target=serve, daemon=True).start()
    return server, accepted


def parent_watch(worker, kill_parent):
    """A middle process starts the sandboxed worker, which waits for the middle's exit through kqueue."""
    middle = subprocess.Popen([sys.executable, "-I", "-c",
                               "import subprocess,sys,time\n"
                               "p=subprocess.Popen([sys.argv[1],'parent-watch'])\n"
                               "print('child',p.pid,flush=True)\n"
                               "time.sleep(120)\n", worker], stdout=subprocess.PIPE, text=True)
    children.append(middle)
    first = middle.stdout.readline().split()
    worker_pid = int(first[1])
    report = middle.stdout.readline().strip()
    facts = {"workerPid": worker_pid, "workerReport": report}
    if kill_parent:
        os.kill(middle.pid, signal.SIGKILL); middle.wait()
        killed = time.monotonic()
        while time.monotonic() - killed < 10:
            try:
                os.kill(worker_pid, 0)
            except ProcessLookupError:
                facts["workerGoneAfterSeconds"] = round(time.monotonic() - killed, 3)
                break
            time.sleep(0.01)
        else:
            facts["workerGoneAfterSeconds"] = None
            try:
                os.kill(worker_pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
    else:
        time.sleep(1.5)
        try:
            os.kill(worker_pid, 0); facts["aliveWhileParentLives"] = True
        except ProcessLookupError:
            facts["aliveWhileParentLives"] = False
        try:
            os.kill(worker_pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        middle.kill(); middle.wait()
    return facts


def memory_monitor(executable, mib, budget, interval):
    """Start the probe touching MIB of native memory; sample its footprint every INTERVAL seconds; kill above BUDGET."""
    process = subprocess.Popen([executable, "memory", str(mib)], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    children.append(process)
    samples, killed_at, peak, killed_footprint = 0, None, 0, 0
    started = time.monotonic()
    while process.poll() is None and time.monotonic() - started < 40:
        value = footprint(process.pid)
        if value is None:
            break
        samples += 1; peak = max(peak, value)
        if value > budget and killed_at is None:
            os.kill(process.pid, signal.SIGKILL); killed_at = round(time.monotonic() - started, 3)
            killed_footprint = value
        time.sleep(interval)
    process.wait()
    lines = process.stdout.read().decode().split()
    touched = [int(x) for x in lines if x.isdigit()]
    return {"budgetMiB": budget >> 20, "intervalMs": interval * 1000, "samples": samples, "peakSampledMiB": round(peak / 1048576, 1),
            "killedAtSeconds": killed_at, "killedFootprintMiB": round(killed_footprint / 1048576, 1) if killed_at is not None else None,
            "lastTouchedMiB": touched[-1] if touched else 0, "exit": process.returncode,
            "reachedEnd": "allocated" in lines}


def main():
    control, launchers = build()
    worker = launchers["SandboxLauncher"]
    log("built and signed")
    fixture = os.path.join(WORK, "fixture"); os.makedirs(fixture)
    secret = os.path.join(fixture, "secret.txt")
    with open(secret, "w") as f:
        f.write("fixture secret")
    home = os.path.expanduser("~")
    server, accepted = listener()
    port = server.getsockname()[1]
    results["control"] = {"info": probe(control, ["info"]), "deny": probe(control, ["deny", secret, fixture, str(port), home]),
                          "mach": probe(control, ["mach"])}
    results["control"]["fixtureWritten"] = os.path.exists(os.path.join(fixture, "sandbox-write.txt"))
    results["control"]["loopbackAccepted"] = len(accepted)
    if os.path.exists(os.path.join(fixture, "sandbox-write.txt")):
        os.remove(os.path.join(fixture, "sandbox-write.txt"))
    accepted.clear()
    log("control done")
    results["sandbox"] = {"info": probe(worker, ["info"]), "deny": probe(worker, ["deny", secret, fixture, str(port), home])}
    results["sandbox"]["fixtureWritten"] = os.path.exists(os.path.join(fixture, "sandbox-write.txt"))
    results["sandbox"]["loopbackAccepted"] = len(accepted)
    results["sandbox"]["mach"] = probe(worker, ["mach"])
    log("sandbox info and denials done")
    variant = launchers["SandboxLauncherVariant"]
    results["variantSameContainer"] = {"info": probe(variant, ["info"]), "mach": probe(variant, ["mach"])}
    none = launchers["SandboxLauncherNoBootstrap"]
    data = os.urandom(1 << 20)
    echoed = probe(none, ["echo"], stdin=data)
    echoed["matches"] = isinstance(echoed["output"], str) and echoed["output"].strip() == hashlib.sha256(data).hexdigest().upper()
    results["noBootstrap"] = {"info": probe(none, ["info"]), "deny": probe(none, ["deny", secret, fixture, str(port), home]),
                              "mach": probe(none, ["mach"]), "stdio": echoed,
                              "spawnZeroProcesses": probe(none, ["spawn"], limits={resource.RLIMIT_NPROC: 0, resource.RLIMIT_CORE: 0})}
    results["noBootstrap"]["fixtureWritten"] = os.path.exists(os.path.join(fixture, "sandbox-write.txt"))
    results["noBootstrap"]["loopbackAccepted"] = len(accepted)
    log("variant and bootstrap-free launchers done")
    results["sandbox"]["spawnUnlimited"] = probe(worker, ["spawn"])
    results["sandbox"]["spawnZeroProcesses"] = probe(worker, ["spawn"], limits={resource.RLIMIT_NPROC: 0, resource.RLIMIT_CORE: 0})
    results["sandbox"]["infoUnderLimits"] = probe(worker, ["info"], limits={resource.RLIMIT_NPROC: 0, resource.RLIMIT_CORE: 0, resource.RLIMIT_CPU: 30})
    read_end, write_end = os.pipe()
    payload = os.urandom(1 << 20)

    def feed():
        try:
            with os.fdopen(write_end, "wb") as f:
                f.write(payload)
        except BrokenPipeError:
            pass  # the probe stopped reading; its report says why
    feeder = threading.Thread(target=feed, daemon=True); feeder.start()
    results["sandbox"]["inheritedDescriptor"] = probe(worker, ["fd", str(read_end)], pass_fds=(read_end,))
    os.close(read_end)  # before joining: a writer blocked on a full pipe gets EPIPE instead of waiting forever
    feeder.join(10)
    data = os.urandom(8 << 20)
    echoed = probe(worker, ["echo"], stdin=data)
    echoed["matches"] = isinstance(echoed["output"], str) and echoed["output"].strip() == hashlib.sha256(data).hexdigest().upper()
    results["sandbox"]["stdio"] = echoed
    log("spawn, descriptor and stdio done")
    results["sandbox"]["parentWatchAlive"] = parent_watch(worker, kill_parent=False)
    results["sandbox"]["parentWatchKilled"] = parent_watch(worker, kill_parent=True)
    log("parent exit done")
    results["sandbox"]["memory"] = [memory_monitor(worker, 512, 256 << 20, 0.01), memory_monitor(worker, 512, 256 << 20, 0.05),
                                    memory_monitor(worker, 128, 256 << 20, 0.01)]
    results["control"]["memory"] = memory_monitor(control, 512, 256 << 20, 0.01)
    log("memory monitor done")
    timings = {"sandboxed": [], "control": []}
    for _ in range(5):
        timings["sandboxed"].append(probe(worker, ["exit0"])["seconds"])
        timings["control"].append(probe(control, ["exit0"])["seconds"])
    results["startupSeconds"] = timings
    results["containers"] = {identifier: os.path.isdir(os.path.join(home, "Library", "Containers", identifier))
                             for identifier in sorted({i for _, i, _ in LAUNCHERS})}
    server.close()
    results["finished"] = time.strftime("%Y-%m-%dT%H:%M:%S%z")


try:
    main()
finally:
    for child in children:
        if child.poll() is None:
            child.kill(); child.wait()
    with open(os.path.join(WORK, "results.json"), "w") as f:
        json.dump(results, f, indent=2)
    log("results written")
