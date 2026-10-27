#!/usr/bin/env python3
"""Check six VM frames from the native Psychedelic transparent-picture route.

Usage: check-theme-expressive-captures.py GUEST_RECEIPT.json IMAGE_PREFIX OUTPUT.json
IMAGE_PREFIX ends immediately before picture-0.png through picture-5.png.
The owned viewer is at x=183..1163 on a 1280x800 Ubuntu VMware display.
"""

import json
from pathlib import Path
import sys

from PIL import Image, ImageChops


REGIONS = {
    "picture": (432, 283, 913, 604),
    "viewerToolbar": (183, 85, 1163, 124),
    "viewerBody": (184, 124, 1163, 765),
    "exposedMain": (100, 112, 182, 772),
}


def changed(first, second, rectangle):
    data = ImageChops.difference(first.crop(rectangle), second.crop(rectangle)).tobytes()
    return sum(max(data[index:index + 3]) > 10 for index in range(0, len(data), 3))


def run(receipt_path, image_prefix, output):
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    expected = ["panel", "danger", "selection", "search"] + [
        f"picture-{index}" for index in range(6)]
    if not receipt.get("passed") or receipt.get("theme") != "Psychedelic" or \
            [phase["name"] for phase in receipt.get("phases", [])] != expected or \
            receipt.get("pictureCycleSeconds", 0) < 300 or \
            receipt.get("originalGnomeAnimations") != receipt.get("restoredGnomeAnimations") or \
            not receipt.get("sourceUnchanged") or not receipt.get("targetEmpty"):
        raise AssertionError("The complete owned native picture route did not pass and restore state")
    frames = [Image.open(f"{image_prefix}picture-{index}.png").convert("RGB")
              for index in range(6)]
    if any(frame.size != (1280, 800) for frame in frames):
        raise AssertionError("The VMware display size changed during the bounded cycle")
    # Opaque and half-alpha fixture colors remain distinguishable on the
    # checkerboard. A missing or black image must not satisfy a zero-diff test.
    if frames[0].getpixel((500, 350)) != (255, 45, 130) or \
            frames[0].getpixel((800, 350)) == frames[0].getpixel((500, 350)) or \
            frames[0].getpixel((450, 300)) == frames[0].getpixel((455, 300)):
        raise AssertionError("Transparent/opaque picture content was not rendered as expected")
    pairs = []
    for index in range(5):
        counts = {name: changed(frames[index], frames[index + 1], rectangle)
                  for name, rectangle in REGIONS.items()}
        if any(counts[name] for name in ("picture", "viewerToolbar", "viewerBody")):
            raise AssertionError(f"Work content or controls moved in pair {index}: {counts}")
        if counts["exposedMain"] < 200:
            raise AssertionError(f"Decorative motion did not continue behind viewer in pair {index}: {counts}")
        pairs.append(counts)
    result = {"passed": True, "guestReceipt": str(receipt_path),
              "appDllSha256": receipt["appDllSha256"],
              "pictureCycleSeconds": receipt["pictureCycleSeconds"],
              "regions": REGIONS, "changedPixelsOver10PerChannel": pairs}
    if output.exists():
        raise AssertionError("Use a fresh measurement receipt")
    output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    print(json.dumps(run(Path(sys.argv[1]), sys.argv[2], Path(sys.argv[3]))))
