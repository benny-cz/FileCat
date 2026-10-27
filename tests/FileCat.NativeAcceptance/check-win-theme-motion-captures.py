#!/usr/bin/env python3
"""Verify six Win11 VMware frames from win-ax-theme-motion.ps1.

Usage: check-win-theme-motion-captures.py RECEIPT.json IMAGE_PREFIX OUTPUT.json
IMAGE_PREFIX precedes moving-1.png, reduced-1.png, and resumed-1.png.
The bounded 1925×1411/100% route places FileCat at (220, 115).
"""

import json
from pathlib import Path
import sys

from PIL import Image, ImageChops


REGIONS = {
    "menu": (220, 138, 1320, 205),
    "leftList": (235, 315, 715, 740),
    "rightList": (785, 315, 1240, 740),
    "footer": (220, 785, 1320, 870),
}


def changed(first, second, rectangle):
    raw = ImageChops.difference(first.crop(rectangle), second.crop(rectangle)).tobytes()
    return sum(max(raw[index:index + 3]) > 10 for index in range(0, len(raw), 3))


def run(receipt_path, prefix, output):
    receipt = json.loads(receipt_path.read_text(encoding="utf-8-sig"))
    phases = receipt.get("Phases", [])
    if (not receipt.get("Passed") or not receipt.get("SourceUnchanged") or
            not receipt.get("TargetEmpty") or receipt.get("Head") != "4c042d37" or
            receipt.get("OriginalAnimation") != receipt.get("RestoredAnimation") or
            [(item["Name"], item["Animation"]) for item in phases] !=
            [("moving", 1), ("reduced", 0), ("resumed", 1)] or
            any(item["ForegroundPid"] != item["AppPid"] for item in phases)):
        raise AssertionError("The exact-build Windows native route did not finish safely")
    measurements = {}
    for phase in ("moving", "reduced", "resumed"):
        images = [Image.open(f"{prefix}{phase}-{index}.png").convert("RGB")
                  for index in (1, 2)]
        if any(image.size != (1925, 1411) for image in images):
            raise AssertionError(f"Unexpected {phase} VM display size")
        counts = {name: changed(*images, bounds) for name, bounds in REGIONS.items()}
        measurements[phase] = counts
        if counts["leftList"] or counts["rightList"]:
            raise AssertionError(f"A functional file list moved in {phase}: {counts}")
        if phase == "reduced" and (counts["menu"] or counts["footer"]):
            raise AssertionError(f"Decoration moved with reduced motion: {counts}")
        if phase != "reduced" and min(counts["menu"], counts["footer"]) < 500:
            raise AssertionError(f"Decorative motion did not {phase}: {counts}")
    result = {"passed": True, "guestReceipt": str(receipt_path),
              "appDllSha256": receipt["AppDllSha256"], "regions": REGIONS,
              "changedPixelsOver10PerChannel": measurements}
    if output.exists():
        raise AssertionError("Use a fresh capture measurement receipt")
    output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    print(json.dumps(run(Path(sys.argv[1]), sys.argv[2], Path(sys.argv[3]))))
