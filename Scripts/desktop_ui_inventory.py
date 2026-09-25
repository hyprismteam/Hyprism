#!/usr/bin/env python3
# Copyright (C) 2026 Hyprism Launcher
# SPDX-License-Identifier: GPL-3.0-only

"""Generate a review report for Desktop UI classes, templates, colors, and dimensions."""

from __future__ import annotations

import argparse
import json
import re
from collections import defaultdict
from pathlib import Path
from typing import Iterable

ROOT = Path(__file__).resolve().parents[1]
DESKTOP = ROOT / "Sources" / "Hyprism.Desktop"
XAML_ROOTS = (DESKTOP,)
CODE_ROOTS = (DESKTOP / "Screens", DESKTOP / "UI", DESKTOP / "Shell")
TEST_ROOT = ROOT / "Tests" / "Hyprism.Desktop.Tests"

TAG_PATTERN = re.compile(
    r"<(Style|ControlTheme|DataTemplate|ControlTemplate)\b"
    r"(?:\"[^\"]*\"|'[^']*'|[^>'\"])*>",
    re.S,
)
SELECTOR_PATTERN = re.compile(r"\bSelector\s*=\s*\"([^\"]+)\"")
CLASS_SELECTOR_PATTERN = re.compile(r"\.([A-Za-z_][\w-]*)")
CLASS_ATTRIBUTE_PATTERN = re.compile(r"\bClasses\s*=\s*\"([^\"]*)\"")
CLASS_PROPERTY_PATTERN = re.compile(r"\bClasses\.([A-Za-z_][\w-]*)\s*=")
CLASS_CODE_PATTERN = re.compile(
    r"(?:\.Classes\.(?:Add|Remove|Set|Toggle|Contains)|\.Classes\[)\s*\(?\s*\"([A-Za-z_][\w-]*)\""
)
CLASS_ASSERT_PATTERN = re.compile(
    r"\bAssert\.(?:Contains|DoesNotContain)\(\s*\"([A-Za-z_][\w-]*)\"\s*,\s*[^,\n]*\.Classes\b"
)
COLOR_PATTERN = re.compile(r"#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?\b")
NUMERIC_PROPERTIES = {
    "Width", "Height", "MinWidth", "MaxWidth", "MinHeight", "MaxHeight",
    "Spacing", "FontSize", "LineHeight", "CornerRadius", "Opacity", "Duration",
    "Padding", "Margin", "BorderThickness", "StrokeThickness",
}
ATTRIBUTE_PATTERN = re.compile(r"\b([A-Za-z][A-Za-z0-9]*)\s*=\s*\"([^\"]*)\"")
LICENSE_PATTERN = re.compile(r"SPDX-" r"License-Identifier:\s*([^\s*]+)")


def relative(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()


def source_files(roots: Iterable[Path], suffixes: set[str]) -> list[Path]:
    return sorted(
        path
        for root in roots
        if root.exists()
        for path in root.rglob("*")
        if path.is_file() and path.suffix in suffixes and "obj" not in path.parts and "bin" not in path.parts
    )


def line_number(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def occurrence(path: Path, line: int, value: str) -> dict[str, object]:
    return {"file": relative(path), "line": line, "value": value}


def collect_definitions(xaml_files: list[Path]) -> tuple[list[dict[str, object]], dict[str, list[dict[str, object]]]]:
    definitions: list[dict[str, object]] = []
    selectors: dict[str, list[dict[str, object]]] = defaultdict(list)
    for path in xaml_files:
        text = path.read_text(encoding="utf-8")
        for match in TAG_PATTERN.finditer(text):
            tag = match.group(1)
            opening = match.group(0)
            selector_match = SELECTOR_PATTERN.search(opening)
            key_match = re.search(r"\bx:Key\s*=\s*\"([^\"]+)\"", opening)
            target_match = re.search(r"\bTargetType\s*=\s*\"([^\"]+)\"", opening)
            name = key_match.group(1) if key_match else None
            selector = selector_match.group(1) if selector_match else None
            item = {
                "kind": tag,
                "name": name,
                "target_type": target_match.group(1) if target_match else None,
                "selector": selector,
                "owner": relative(path),
                "line": line_number(text, match.start()),
            }
            definitions.append(item)
            if selector:
                for class_name in CLASS_SELECTOR_PATTERN.findall(selector):
                    selectors[class_name].append(occurrence(path, line_number(text, match.start()), selector))
    return definitions, selectors


def collect_class_usage(files: list[Path]) -> dict[str, list[dict[str, object]]]:
    usages: dict[str, list[dict[str, object]]] = defaultdict(list)
    for path in files:
        text = path.read_text(encoding="utf-8")
        is_xaml = path.suffix == ".axaml"
        if is_xaml:
            for match in CLASS_ATTRIBUTE_PATTERN.finditer(text):
                for name in match.group(1).split():
                    usages[name].append(occurrence(path, line_number(text, match.start()), "Classes=" + match.group(1)))
            for match in CLASS_PROPERTY_PATTERN.finditer(text):
                usages[match.group(1)].append(occurrence(path, line_number(text, match.start()), "Classes." + match.group(1)))
        else:
            for pattern in (CLASS_CODE_PATTERN, CLASS_ASSERT_PATTERN):
                for match in pattern.finditer(text):
                    usages[match.group(1)].append(occurrence(path, line_number(text, match.start()), match.group(0)))
    return usages


def collect_colors(xaml_files: list[Path]) -> list[dict[str, object]]:
    colors: dict[str, list[dict[str, object]]] = defaultdict(list)
    for path in xaml_files:
        text = path.read_text(encoding="utf-8")
        for match in COLOR_PATTERN.finditer(text):
            colors[match.group(0).upper()].append(occurrence(path, line_number(text, match.start()), match.group(0)))
    return [
        {"color": color, "count": len(refs), "occurrences": refs}
        for color, refs in sorted(colors.items())
    ]


def collect_dimensions(xaml_files: list[Path]) -> list[dict[str, object]]:
    values: dict[tuple[str, str], list[dict[str, object]]] = defaultdict(list)
    for path in xaml_files:
        text = path.read_text(encoding="utf-8")
        for tag_match in re.finditer(r"<[A-Za-z_][^>]*>", text, re.S):
            tag = tag_match.group(0)
            attributes = {name: value for name, value in ATTRIBUTE_PATTERN.findall(tag)}
            candidates = [
                (name, value)
                for name, value in attributes.items()
                if name in NUMERIC_PROPERTIES
            ]
            if tag.startswith("<Setter"):
                property_name = attributes.get("Property", "").split(".")[-1]
                if property_name in NUMERIC_PROPERTIES and "Value" in attributes:
                    candidates.append((property_name, attributes["Value"]))
            for name, value in candidates:
                if not re.search(r"\d", value) or "{" in value:
                    continue
                values[(name, value)].append(occurrence(path, line_number(text, tag_match.start()), value))
    return [
        {"property": name, "literal": literal, "count": len(refs), "occurrences": refs}
        for (name, literal), refs in sorted(values.items())
        if len(refs) >= 3
    ]


def collect_assets(xaml_files: list[Path], code_files: list[Path]) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    resources: list[dict[str, object]] = []
    source_texts = [(source, source.read_text(encoding="utf-8")) for source in xaml_files + code_files]
    for path in source_files([DESKTOP / "Assets"], {".axaml"}):
        text = path.read_text(encoding="utf-8")
        license_match = LICENSE_PATTERN.search(text[:500])
        for match in re.finditer(r"<([A-Za-z_][\w:.-]*)\b[^>]*\bx:Key=\"([^\"]+)\"[^>]*>", text, re.S):
            key = match.group(2)
            resource_type = match.group(1).split(":")[-1]
            resource_references = [
                {"file": relative(source), "line": line_number(source_text, ref.start())}
                for source, source_text in source_texts
                for ref in re.finditer(r"(?:StaticResource|DynamicResource)\s+" + re.escape(key) + r"\b", source_text)
            ]
            resources.append({
                "key": key,
                "type": resource_type,
                "source": relative(path),
                "line": line_number(text, match.start()),
                "license": license_match.group(1) if license_match else "Not recorded in this resource file.",
                "tint": "Set by the consuming control or selector." if "Geometry" in resource_type else "Defined by the resource.",
                "fallback": "Review the consuming screen; the resource has no fallback behavior.",
                "uses": resource_references,
            })

    assets: list[dict[str, object]] = []
    for path in sorted(item for item in (DESKTOP / "Assets").rglob("*") if item.is_file() and item.suffix != ".axaml"):
        relative_path = relative(path)
        data = path.read_bytes()
        dimensions: list[int] | None = None
        if path.suffix.lower() == ".png" and len(data) >= 24 and data[:8] == b"\x89PNG\r\n\x1a\n":
            dimensions = [int.from_bytes(data[16:20], "big"), int.from_bytes(data[20:24], "big")]
        elif path.suffix.lower() == ".ico" and len(data) >= 22:
            dimensions = [data[6] or 256, data[7] or 256]
        elif path.suffix.lower() == ".svg":
            svg_text = data[:2048].decode("utf-8", errors="replace")
            view_box = re.search(r"\bviewBox\s*=\s*['\"]\s*[-.\d]+[ ,]+[-.\d]+[ ,]+([\d.]+)[ ,]+([\d.]+)", svg_text)
            if view_box:
                dimensions = [int(float(view_box.group(1))), int(float(view_box.group(2)))]
        elif path.suffix.lower() == ".json":
            try:
                json_data = json.loads(data)
                if isinstance(json_data, dict) and isinstance(json_data.get("w"), (float, int)) and isinstance(json_data.get("h"), (float, int)):
                    dimensions = [int(json_data["w"]), int(json_data["h"])]
            except (UnicodeDecodeError, json.JSONDecodeError):
                pass
        text_header = data[:1024].decode("utf-8", errors="ignore")
        license_match = LICENSE_PATTERN.search(text_header)
        declared_image_sizes: list[dict[str, int]] = []
        for _, source_text in source_texts:
            for image_tag in re.finditer(r"<Image\b[^>]*>", source_text, re.S):
                if path.name not in image_tag.group(0):
                    continue
                attributes = {name: value for name, value in ATTRIBUTE_PATTERN.findall(image_tag.group(0))}
                width = attributes.get("Width")
                height = attributes.get("Height")
                if width and height and width.isdigit() and height.isdigit():
                    size = {"width": int(width), "height": int(height)}
                    if size not in declared_image_sizes:
                        declared_image_sizes.append(size)
        family = path.parent.name
        role = {
            "Brands": "Brand mark or product logo.",
            "Flags": "Country or language picker image.",
            "Fluent": "Screen and category illustration keyed by filename.",
            "Fonts": "Bundled application font face.",
            "Game": "Game artwork used in instance surfaces.",
            "Images": "Application or authentication imagery.",
            "Lotties": "Vector animation used by a screen or wizard.",
            "Music": "Bundled menu music track.",
            "Screenshots": "Packaged product screenshot or preview.",
        }.get(family, "Asset used by Desktop.")
        assets.append({
            "file": relative_path,
            "key": path.stem,
            "format": path.suffix.lstrip(".").lower(),
            "role": role,
            "native_dimensions_px": dimensions,
            "declared_image_sizes_px": declared_image_sizes,
            "tint": "Retain source colors; confirm any screen-specific treatment." if path.suffix.lower() in {".png", ".svg", ".ico"} else "Not applicable to this asset format.",
            "fallback": "Review the consuming control; no fallback is declared in the asset file.",
            "license": license_match.group(1) if license_match else "Not recorded in this asset file.",
            "references": [
                {"file": relative(source), "line": line_number(source_text, ref.start())}
                for source, source_text in source_texts
                for ref in re.finditer(re.escape(path.name), source_text)
            ],
            "size_bytes": len(data),
        })
    return resources, assets


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="Write JSON to this path instead of standard output.")
    args = parser.parse_args()

    xaml_files = source_files(XAML_ROOTS, {".axaml"})
    screen_xaml = [path for path in xaml_files if (DESKTOP / "Screens") in path.parents]
    code_files = source_files(CODE_ROOTS, {".cs"}) + source_files([TEST_ROOT], {".cs"})
    definitions, selectors = collect_definitions(xaml_files)
    usages = collect_class_usage(xaml_files + code_files)
    class_names = sorted(set(selectors) | set(usages))
    classes = [
        {
            "name": name,
            "selector_definitions": selectors.get(name, []),
            "usages": usages.get(name, []),
            "status": "no-selector" if name not in selectors else "no-static-use" if name not in usages else "matched",
        }
        for name in class_names
    ]
    asset_resources, assets = collect_assets(xaml_files, code_files)
    report = {
        "schema_version": 1,
        "scope": "Hyprism.Desktop AXAML resources and Desktop tests",
        "summary": {
            "xaml_files": len(xaml_files),
            "screen_xaml_files": len(screen_xaml),
            "style_and_template_definitions": len(definitions),
            "class_names": len(classes),
            "classes_without_selector": sum(item["status"] == "no-selector" for item in classes),
            "selectors_without_static_use": sum(item["status"] == "no-static-use" for item in classes),
            "screen_hex_literals": sum(item["count"] for item in collect_colors(screen_xaml)),
            "asset_files": len(assets),
            "asset_resource_keys": len(asset_resources),
        },
        "classes": classes,
        "definitions": definitions,
        "hex_colors": collect_colors(xaml_files),
        "screen_hex_colors": collect_colors(screen_xaml),
        "repeated_numeric_literals": collect_dimensions(xaml_files),
        "asset_resources": asset_resources,
        "assets": assets,
        "review_notes": [
            "Class matching is lexical. Bindings and runtime-generated classes may require source review.",
            "Repeated colors and numbers are candidates for review, not automatic token moves.",
            "Selectors inside templates may be intentionally used only by template parts.",
        ],
    }
    serialized = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        output = args.output if args.output.is_absolute() else ROOT / args.output
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(serialized, encoding="utf-8")
    else:
        print(serialized, end="")


if __name__ == "__main__":
    main()
