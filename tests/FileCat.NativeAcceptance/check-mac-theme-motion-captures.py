#!/usr/bin/env python3
"""Verify six direct 2880×1800 Mac captures from mac-ax-theme-motion.py.

Usage: check-mac-theme-motion-captures.py RECEIPT.json IMAGE_PREFIX OUTPUT.json
IMAGE_PREFIX precedes moving-1.png, reduced-1.png and resumed-1.png.
"""

import json
from pathlib import Path
import sys

from PIL import Image, ImageChops


# The window is 1000×660 logical at 2×. These bounded regions avoid the
# Mac's menu bar, iCloud notification, and unrelated VoiceOver caption.
REGIONS = {
    "menu": (750, 210, 2090, 265),
    "leftList": (260, 560, 1180, 1280),
    "rightList": (1260, 560, 2140, 1280),
    "footer": (1450, 1390, 2150, 1450),
}


def changed(first, second, rectangle):
    raw = ImageChops.difference(first.crop(rectangle), second.crop(rectangle)).tobytes()
    return sum(max(raw[index:index + 3]) > 10 for index in range(0, len(raw), 3))


def run(receipt_path, prefix, output):
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    phases = receipt.get("phases", [])
    if (not receipt.get("passed") or not receipt.get("sourceUnchanged") or
            not receipt.get("targetEmpty") or receipt.get("head") != "799e8918" or
            receipt.get("originalUiReducedMotion") !=
            receipt.get("restoredUiReducedMotion") or
            receipt.get("originalNativeReducedMotion") !=
            receipt.get("restoredNativeReducedMotion") or
            receipt.get("originalUiReducedMotion") !=
            receipt.get("originalNativeReducedMotion") or
            [(item["name"], item["nativeReducedMotion"]) for item in phases] !=
            [("moving", False), ("reduced", True), ("resumed", False)] or
            any(item["geometry"][2:] != [1000, 660] for item in phases) or
            len({item["appPid"] for item in phases}) != 1 or
            len(receipt.get("systemAlertsDismissed", [])) != 6 or
            any(item not in ("absent", "dismissed") for item in
                receipt.get("systemAlertsDismissed", []))):
        raise AssertionError("The exact-build Mac native motion route did not finish safely")
    measurements = {}
    for phase in ("moving", "reduced", "resumed"):
        images = [Image.open(f"{prefix}{phase}-{index}.png").convert("RGB")
                  for index in (1, 2)]
        if any(image.size != (2880, 1800) for image in images):
            raise AssertionError(f"Unexpected {phase} Mac display size")
        counts = {name: changed(*images, bounds) for name, bounds in REGIONS.items()}
        measurements[phase] = counts
        if counts["leftList"] or counts["rightList"]:
            raise AssertionError(f"A functional file list moved in {phase}: {counts}")
        if phase == "reduced" and (counts["menu"] or counts["footer"]):
            raise AssertionError(f"Decoration moved with reduced motion: {counts}")
        if phase != "reduced" and min(counts["menu"], counts["footer"]) < 500:
            raise AssertionError(f"Decorative motion did not {phase}: {counts}")
    result = {"passed": True, "guestReceipt": str(receipt_path),
              "appDllSha256": receipt["appDllSha256"], "regions": REGIONS,
              "changedPixelsOver10PerChannel": measurements}
    if output.exists():
        raise AssertionError("Use a fresh capture measurement receipt")
    output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    print(json.dumps(run(Path(sys.argv[1]), sys.argv[2], Path(sys.argv[3]))))
