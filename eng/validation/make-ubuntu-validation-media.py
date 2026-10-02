#!/usr/bin/env python3
"""Remaster verified Ubuntu desktop media for one explicitly disposable VMware VM.

Requires xorriso and PyYAML. The output contains a test account password hash;
keep the media/configuration in private, ignored evidence storage. This only
creates installation media; attaching it and authorizing OS erasure are separate
operator steps. The installer refuses a different VM, disk identity or disk size.
"""
import argparse
import hashlib
import json
import pathlib
import re
import subprocess

import yaml


def digest(path):
    checksum = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            checksum.update(chunk)
    return checksum.hexdigest()


def run(*args):
    subprocess.run(args, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--iso", type=pathlib.Path, required=True)
    parser.add_argument("--sha256", required=True)
    parser.add_argument("--output-dir", type=pathlib.Path, required=True)
    parser.add_argument("--vm-uuid", required=True)
    parser.add_argument("--disk-serial", required=True)
    parser.add_argument("--disk-bytes", type=int, required=True)
    parser.add_argument("--hostname", required=True)
    parser.add_argument("--password-hash-file", type=pathlib.Path, required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-f]{64}", args.sha256):
        parser.error("invalid SHA256")
    if not re.fullmatch(r"[0-9a-f-]{36}", args.vm_uuid):
        parser.error("invalid VM UUID")
    if not re.fullmatch(r"[0-9a-f]{32}", args.disk_serial):
        parser.error("expected a VMware disk serial")
    if not re.fullmatch(r"[a-z][a-z0-9-]{0,62}", args.hostname):
        parser.error("invalid hostname")
    if args.disk_bytes <= 0:
        parser.error("invalid disk size")
    if args.output_dir.exists():
        parser.error("output directory already exists; preserve previous evidence")
    if digest(args.iso) != args.sha256:
        parser.error("original ISO hash mismatch")
    password_hash = args.password_hash_file.read_text().strip()
    if not password_hash.startswith("$6$"):
        parser.error("expected a SHA512 crypt password hash")
    args.output_dir.mkdir(mode=0o700)
    grub = args.output_dir / "grub.cfg"
    sources = args.output_dir / "install-sources.yaml"
    run("xorriso", "-osirrox", "on", "-indev", str(args.iso),
        "-extract", "/boot/grub/grub.cfg", str(grub),
        "-extract", "/casper/install-sources.yaml", str(sources))
    catalog = yaml.safe_load(sources.read_text())
    desktop = [entry for entry in catalog if entry["id"] == "ubuntu-desktop"]
    if len(desktop) != 1:
        raise RuntimeError("official media lacks the expected full desktop source")
    original_grub = grub.read_text()
    patched_grub, count = re.subn(
        r"(linux\s+/casper/vmlinuz[^\n]*?)\s+---",
        r"\1 autoinstall ---", original_grub)
    if count < 1:
        raise RuntimeError("no expected installer kernel entry; refuse unknown boot media")
    patched_grub = re.sub(r"(?m)^set timeout=\d+", "set timeout=3", patched_grub)
    grub.chmod(0o600)  # The official ISO's configuration is read-only.
    grub.write_text(patched_grub)
    # Evaluated in the installer before storage configuration. A floppy is not
    # an install disk; any second non-removable hard disk aborts the reinstall.
    guard = (
        "import json,pathlib,subprocess; "
        "u=pathlib.Path('/sys/class/dmi/id/product_uuid').read_text().strip().lower(); "
        "m=pathlib.Path('/sys/class/dmi/id/sys_vendor').read_text().strip(); "
        "d=json.loads(subprocess.check_output(['lsblk','-dnbo','NAME,SIZE,TYPE,SERIAL,RM','--json']))['blockdevices']; "
        "d=[x for x in d if x['type']=='disk' and not x['rm'] and not x['name'].startswith('fd')]; "
        f"assert u=={args.vm_uuid!r} and m=='VMware, Inc.', 'wrong VM'; "
        f"assert len(d)==1 and d[0]['serial']=={args.disk_serial!r} and int(d[0]['size'])=={args.disk_bytes}, 'wrong disk'"
    )
    config = {"autoinstall": {
        "version": 1,
        "refresh-installer": {"update": False},
        "source": {"id": desktop[0]["id"]},
        "locale": "en_US.UTF-8",
        "keyboard": {"layout": "us"},
        "timezone": "Europe/Prague",
        "identity": {"hostname": args.hostname, "realname": "FileCat Validation",
                     "username": "benny", "password": password_hash},
        "ssh": {"install-server": True, "allow-pw": True},
        "codecs": {"install": False},
        "drivers": {"install": False},
        "packages": ["open-vm-tools-desktop"],
        "storage": {"layout": {"name": "direct", "match": {"serial": args.disk_serial}}},
        "early-commands": [["python3", "-c", guard]],
        "late-commands": [
            "printf '[daemon]\\nAutomaticLoginEnable=true\\nAutomaticLogin=benny\\n' > /target/etc/gdm3/custom.conf"
        ],
        # Power off allows the operator to detach media before the first OS boot.
        "shutdown": "poweroff",
    }}
    config_path = args.output_dir / "autoinstall.yaml"
    config_path.write_text(json.dumps(config, indent=2) + "\n")  # JSON is valid YAML.
    config_path.chmod(0o600)
    output = args.output_dir / "validation-autoinstall.iso"
    run("xorriso", "-indev", str(args.iso), "-outdev", str(output),
        "-map", str(config_path), "/autoinstall.yaml",
        "-map", str(grub), "/boot/grub/grub.cfg",
        "-boot_image", "any", "replay", "-commit", "-end")
    output.chmod(0o600)
    manifest = {
        "original_iso": args.iso.name, "original_sha256": args.sha256,
        "remastered_sha256": digest(output), "autoinstall_sha256": digest(config_path),
        "grub_sha256": digest(grub), "generator_sha256": digest(pathlib.Path(__file__)),
        "source_id": desktop[0]["id"], "vm_uuid": args.vm_uuid,
        "disk_serial": args.disk_serial, "disk_bytes": args.disk_bytes,
        "hostname": args.hostname, "kernel_entries_patched": count,
        "test_user_autologin": True, "installer_shutdown": "poweroff",
    }
    (args.output_dir / "media-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
