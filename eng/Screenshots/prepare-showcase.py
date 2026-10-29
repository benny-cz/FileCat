"""Create synthetic, reproducible folders and a four-panel FileCat workspace.

The fixture root must be absent or empty. --workspace-root can name the path at
which a copied fixture will live in a guest, while files are made at --fixture-root.
"""

import argparse
import json
import os
from pathlib import Path
import struct
import zipfile
import zlib


def png(color: tuple[int, int, int]) -> bytes:
    width, height = 96, 64
    raw = b"".join(
        b"\0" + bytes(
            component
            for x in range(width)
            for component in (
                min(255, color[0] + x // 3),
                min(255, color[1] + y // 3),
                color[2],
            )
        )
        for y in range(height)
    )

    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fixture-root", type=Path, required=True)
    parser.add_argument("--workspace-root", required=True)
    parser.add_argument("--workspace-file", type=Path, required=True)
    args = parser.parse_args()
    root = args.fixture_root.resolve()
    if root.exists() and any(root.iterdir()):
        parser.error("Fixture root must be absent or empty; existing files are never replaced")
    root.mkdir(parents=True, exist_ok=True)

    text_files = {
        "Projects/README.md": "# Projects\nFileCat demonstration workspace.\n",
        "Projects/FileCat.slnx": "<Solution />\n",
        "Projects/CHANGELOG.md": "# Changes\nPanels, tabs, and clearer operations.\n",
        "Projects/Project notes.txt": "Review the source and test tabs before a release.\n",
        "Projects/Tools.json": '{"editor":"FileCat","profile":"showcase"}\n',
        "Projects/Architecture.md": "# Architecture\nOne workspace, explicit source and target panels.\n",
        "Projects/Build.ps1": "dotnet build FileCat.slnx\n",
        "Projects/Directory.Build.props": "<Project />\n",
        "Projects/Tasks.md": "# Tasks\nReview, build, and verify.\n",
        "Projects/Source/MainWindow.axaml": "<Window xmlns=\"https://github.com/avaloniaui\" />\n",
        "Projects/Source/WorkspaceViewModel.cs": "namespace Showcase; public sealed class WorkspaceViewModel {}\n",
        "Projects/Source/ThemePalette.cs": "namespace Showcase; public sealed class ThemePalette {}\n",
        "Projects/Tests/CopyTests.cs": "namespace Showcase; public sealed class CopyTests {}\n",
        "Projects/Tests/ArchiveTests.cs": "namespace Showcase; public sealed class ArchiveTests {}\n",
        "Projects/Tests/PanelTests.cs": "namespace Showcase; public sealed class PanelTests {}\n",
        "Documents/Getting started.md": "# Getting started\nChoose a source and a target panel.\n",
        "Documents/Keyboard shortcuts.md": "# Keyboard shortcuts\nF5 copies; F6 moves; F8 deletes.\n",
        "Documents/Release checklist.md": "# Release checklist\nCheck exact packages and hashes.\n",
        "Documents/Design notes.txt": "Every operation should show its state and outcome.\n",
        "Documents/Features.csv": "Feature,Status\nTabs,Ready\nPanels,Ready\n",
        "Documents/About.md": "# About\nFileCat explores files and system resources.\n",
        "Documents/FAQ.md": "# FAQ\nWhere do settings go? In the selected data root.\n",
        "Documents/Installation.md": "# Installation\nChoose the package for your platform.\n",
        "Documents/Troubleshooting.md": "# Troubleshooting\nOpen the operations report for exact outcomes.\n",
        "Documents/Guides/Navigation.md": "# Navigation\nUse tabs and panel histories.\n",
        "Documents/Guides/Copy and move.md": "# Copy and move\nReview source and target.\n",
        "Documents/Guides/Recovery.md": "# Recovery\nUse a separate destination disk.\n",
        "Documents/Notes/Ideas.md": "# Ideas\nMore ways to explore files.\n",
        "Documents/Notes/Meeting notes.txt": "Demo data only.\n",
        "Media/Logo.svg": '<svg xmlns="http://www.w3.org/2000/svg" width="96" height="64"><rect width="96" height="64" fill="#1d9bb7"/></svg>\n',
        "Media/Palette.csv": "Name,Hex\nOcean,#1d9bb7\nNight,#233047\n",
        "Media/Asset manifest.json": '{"assets":["Logo.svg","Thumbnail.png","Cover.png"]}\n',
        "Media/Color study.svg": '<svg xmlns="http://www.w3.org/2000/svg" width="96" height="64"><rect width="96" height="64" fill="#7956a5"/></svg>\n',
        "Media/Export notes.txt": "Owned artwork prepared for this screenshot.\n",
        "Media/Grid.svg": '<svg xmlns="http://www.w3.org/2000/svg" width="96" height="64"><path d="M0 0h96v64H0z" fill="#26384a"/></svg>\n',
        "Media/Sizes.csv": "Asset,Width,Height\nCover,96,64\n",
        "Media/Artwork/Banner.svg": '<svg xmlns="http://www.w3.org/2000/svg" width="320" height="80"><rect width="320" height="80" fill="#1d9bb7"/></svg>\n',
        "Media/Artwork/Icon.svg": '<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32"><circle cx="16" cy="16" r="12" fill="#233047"/></svg>\n',
        "Media/Exports/README.txt": "Sample image exports for the FileCat gallery.\n",
        "Releases/README.md": "# Demo packages\nThese are synthetic showcase files, not release artifacts.\n",
        "Releases/manifest.json": '{"kind":"showcase","published":false}\n',
        "Releases/release-notes.md": "# Preview notes\nA sample document for the gallery.\n",
        "Releases/Build-info.json": '{"kind":"synthetic","source":"showcase"}\n',
        "Releases/Checksums.sha256": "Synthetic fixture only.\n",
        "Releases/Install guide.md": "# Install guide\nUse an owned test package.\n",
        "Releases/Licenses.txt": "Synthetic sample; no shipped dependencies.\n",
        "Releases/Packages/README.md": "# Packages\nDemonstration archive only.\n",
        "Releases/Reports/summary.md": "# Checks\nSynthetic showcase data verified.\n",
        "Releases/Reports/qa.csv": "Check,Result\nListing,Pass\nTabs,Pass\n",
    }
    for relative, content in text_files.items():
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
    for relative, color in {
        "Media/Thumbnail.png": (29, 155, 183),
        "Media/Cover.png": (35, 48, 71),
        "Media/Exports/Screenshot.png": (52, 138, 105),
        "Media/Exports/Preview.png": (120, 76, 164),
    }.items():
        (root / relative).write_bytes(png(color))
    archive = root / "Releases" / "Packages" / "demo-package.zip"
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
        bundle.writestr("README.txt", "Synthetic showcase package; no product binaries.\n")
    (root / "Releases" / "demo-package.zip").write_bytes(archive.read_bytes())
    (root / "Releases" / "SHA256SUMS").write_text("Synthetic demo; see manifest.json for its scope.\n", encoding="utf-8")

    for path in root.rglob("*"):
        os.utime(path, (1793275200, 1793275200))

    names = [
        ("Projects", "Source", "Tests"),
        ("Documents", "Guides", "Notes"),
        ("Media", "Artwork", "Exports"),
        ("Releases", "Packages", "Reports"),
    ]
    ids = [f"showcase-panel-{n}" for n in range(1, 5)]

    def panel(index: int) -> dict:
        group = names[index]
        tabs = []
        separator = "\\" if "\\" in args.workspace_root else "/"
        for offset, name in enumerate(group):
            path = args.workspace_root.rstrip("/\\") + separator + group[0]
            if offset:
                path += separator + name
            tabs.append({"Location": {"s": "fs", "p": path}})
        return {"Id": ids[index], "Tabs": tabs, "ActiveTab": 0, "TargetPanelId": ids[(index + 1) % 4]}

    def leaf(index: int) -> dict:
        return {"PanelId": ids[index], "Size": 1}

    workspace = {
        "SchemaVersion": 1,
        "Name": "Showcase",
        "Panels": [panel(i) for i in range(4)],
        "ActivePanelId": ids[0],
        "Layout": "Rows",
        "Tree": {"Stacked": True, "Size": 1, "Children": [
            {"Stacked": False, "Size": 1, "Children": [leaf(0), leaf(1)]},
            {"Stacked": False, "Size": 1, "Children": [leaf(2), leaf(3)]},
        ]},
        "Window": {"X": 20, "Y": 20, "Width": 1440, "Height": 960, "Maximized": False},
    }
    args.workspace_file.parent.mkdir(parents=True, exist_ok=True)
    args.workspace_file.write_text(json.dumps(workspace, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{len(text_files) + 7} owned files, four panels, three tabs each: {args.workspace_file}")


if __name__ == "__main__":
    main()
