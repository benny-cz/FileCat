#!/usr/bin/env python3
"""Verify six full-screen VM captures from linux-atspi-theme-motion.py.

Usage: check-theme-motion-captures.py GUEST_RECEIPT.json IMAGE_PREFIX OUTPUT.json
IMAGE_PREFIX is the absolute path before moving-1.png, reduced-1.png, etc.
This bounded 1280×800/100% GNOME route places the app at x=100, y=75.
"""

import json
from pathlib import Path
import sys

from PIL import Image, ImageChops


REGIONS = {
    "menu": (100, 112, 1100, 182),
    "leftList": (105, 284, 596, 670),
    "rightList": (605, 284, 1095, 670),
    "footer": (100, 709, 1100, 773),
}
LABELS = (105, 116, 505, 141)


def changed(first, second, rectangle):
    raw = ImageChops.difference(first.crop(rectangle), second.crop(rectangle)).tobytes()
    return sum(max(raw[index:index + 3]) > 10 for index in range(0, len(raw), 3))


def label_pixels_fixed(first, second):
    left, top, right, bottom = LABELS
    glyphs = [(x, y) for y in range(top, bottom) for x in range(left, right)
              if min(first.getpixel((x, y))) > 210]
    if len(glyphs) < 100:
        raise AssertionError("The expected visible menu labels were absent")
    return sum(first.getpixel(pixel) == second.getpixel(pixel) for pixel in glyphs), len(glyphs)


def run(receipt_path, prefix, output):
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    if not receipt.get("passed") or receipt.get("desktop") != "ubuntu:GNOME" or \
            receipt.get("originalGnomeAnimations") != receipt.get("restoredGnomeAnimations") or \
            [item["name"] for item in receipt.get("phases", [])] != ["moving", "reduced", "resumed"]:
        raise AssertionError("The matching GNOME native route did not complete and restore its setting")
    measurements = {}
    for phase in ("moving", "reduced", "resumed"):
        images = [Image.open(f"{prefix}{phase}-{index}.png").convert("RGB") for index in (1, 2)]
        if any(image.size != (1280, 800) for image in images):
            raise AssertionError(f"Unexpected {phase} VM display size")
        counts = {name: changed(*images, bounds) for name, bounds in REGIONS.items()}
        fixed, glyphs = label_pixels_fixed(*images)
        counts["fixedBrightMenuGlyphs"] = fixed
        counts["brightMenuGlyphs"] = glyphs
        measurements[phase] = counts
        if counts["leftList"] or counts["rightList"] or fixed != glyphs:
            raise AssertionError(f"A functional list or menu glyph changed during {phase}: {counts}")
        if phase == "reduced" and (counts["menu"] or counts["footer"]):
            raise AssertionError(f"The decorative surfaces moved despite GNOME reduced motion: {counts}")
        if phase != "reduced" and min(counts["menu"], counts["footer"]) < 500:
            raise AssertionError(f"Expected decorative motion is missing in {phase}: {counts}")
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
