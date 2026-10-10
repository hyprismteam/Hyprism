#!/usr/bin/env python3

# Copyright (C) 2026 Hyprism Launcher
# SPDX-License-Identifier: GPL-3.0-only

"""Add or verify SPDX headers on comment-capable project files."""

from __future__ import annotations

import argparse
from pathlib import Path
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
COPYRIGHT = "Copyright (C) 2026 Hyprism Launcher"
LICENSE = "SPDX-License-" "Identifier: GPL-3.0-only"

LINE_SUFFIXES = {".cs", ".js", ".mjs", ".ts", ".tsx"}
HASH_SUFFIXES = {".desktop", ".nix", ".py", ".sh", ".toml", ".yaml", ".yml"}
XML_SUFFIXES = {".axaml", ".csproj", ".html", ".manifest", ".plist", ".props", ".targets", ".xml"}
BLOCK_SUFFIXES = {".css"}
MARKDOWN_SUFFIXES = {".md"}
HASH_NAMES = {".gitignore"}

THIRD_PARTY_HEADERS = {
    "Sources/Hyprism.Desktop/Assets/Icons/MaterialSymbols.axaml": (
        "Copyright (C) 2026 Google LLC",
        "SPDX-License-" "Identifier: Apache-2.0",
    ),
    "Sources/Hyprism.Desktop/Assets/Brands/CurseForge.axaml": (
        "CurseForge and the CurseForge logo are trademarks of Overwolf Ltd.",
        "SPDX-License-" "Identifier: LicenseRef-CurseForge",
    ),
    "Sources/Hyprism.Desktop/Assets/Brands/Modifold.axaml": (
        "Modifold branding is owned by Modifold or its licensors.",
        "SPDX-License-" "Identifier: LicenseRef-Modifold",
    ),
    "Sources/Hyprism.Desktop/Assets/Brands/Avalonia.axaml": (
        "Avalonia and the Avalonia logo are registered trademarks of AvaloniaUI OÜ",
        "SPDX-License-" "Identifier: LicenseRef-Avalonia",
    ),
    "Sources/Hyprism.Desktop/Assets/Brands/DotNet.axaml": (
        "Copyright (C) .NET authors",
        "SPDX-License-" "Identifier: CC0-1.0",
    ),
    "Sources/Hyprism.Desktop/Assets/Brands/Hyprism.axaml": (
        "Copyright (C) 2026 Hyprism Launcher",
        "SPDX-License-" "Identifier: CC-BY-SA-4.0",
    ),
    "Sources/Hyprism.Desktop/Assets/Brands/YouTube.axaml": (
        "YouTube and the YouTube logo are trademarks of Google LLC",
        "SPDX-License-" "Identifier: LicenseRef-YouTube",
    ),
}


def project_files() -> list[Path]:
    result = subprocess.run(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
        cwd=ROOT,
        check=True,
        capture_output=True,
    )
    return [ROOT / path.decode() for path in result.stdout.split(b"\0") if path]


def header_style(path: Path) -> str | None:
    relative = path.relative_to(ROOT).as_posix()
    if (
        relative == "LICENSE"
        or relative == "Docs/next-env.d.ts"
        or relative == "Docs/package-lock.json"
        or relative.startswith("Licenses/")
    ):
        return None
    if path.name in HASH_NAMES:
        return "hash"
    if path.suffix in LINE_SUFFIXES:
        return "line"
    if path.suffix in HASH_SUFFIXES:
        return "hash"
    if path.suffix in XML_SUFFIXES:
        return "xml"
    if path.suffix in BLOCK_SUFFIXES:
        return "block"
    if path.suffix in MARKDOWN_SUFFIXES:
        return "xml"
    return None


def header_for(style: str, copyright_text: str, license_text: str, newline: str) -> str:
    if style == "line":
        return f"// {copyright_text}{newline}// {license_text}{newline}{newline}"
    if style == "hash":
        return f"# {copyright_text}{newline}# {license_text}{newline}{newline}"
    return (
        f"/*{newline}{copyright_text}{newline}{license_text}{newline}*/{newline}{newline}"
        if style == "block"
        else f"<!--{newline}{copyright_text}{newline}{license_text}{newline}-->{newline}{newline}"
    )


def insert_header(text: str, style: str, header: str, newline: str) -> str:
    if style == "hash" and text.startswith("#!"):
        line_end = text.find("\n")
        if line_end < 0:
            return text + newline + newline + header
        return text[: line_end + 1] + newline + header + text[line_end + 1 :]

    if style == "xml" and text.startswith("<?xml"):
        line_end = text.find("\n")
        if line_end < 0:
            return text + newline + header
        return text[: line_end + 1] + header + text[line_end + 1 :]

    return header + text


def process(path: Path, write: bool) -> bool:
    if not path.is_file():
        return True
    style = header_style(path)
    if style is None:
        return True

    raw = path.read_bytes()
    has_bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw.decode("utf-8-sig")
    relative = path.relative_to(ROOT).as_posix()
    expected = THIRD_PARTY_HEADERS.get(relative, (COPYRIGHT, LICENSE))
    if expected[0] in text[:768] and expected[1] in text[:768]:
        return True
    if not write:
        return False

    newline = "\r\n" if "\r\n" in text[:2048] else "\n"
    header = header_for(style, *expected, newline)
    updated = insert_header(text, style, header, newline)
    encoded = updated.encode("utf-8")
    path.write_bytes((b"\xef\xbb\xbf" if has_bom else b"") + encoded)
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument(
        "--write",
        action="store_true",
        help="insert missing headers instead of only checking them",
    )
    mode.add_argument(
        "--check",
        action="store_true",
        help="verify headers without modifying files (the default)",
    )
    args = parser.parse_args()

    missing = [path for path in project_files() if not process(path, args.write)]
    if missing:
        print("Files without the required Hyprism SPDX header:", file=sys.stderr)
        for path in missing:
            print(f"  {path.relative_to(ROOT)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
