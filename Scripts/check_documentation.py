#!/usr/bin/env python3
# Copyright (C) 2026 Hyprism Launcher
# SPDX-License-Identifier: GPL-3.0-only

"""Validate documentation prose, bilingual routes, and Core service contracts."""

from __future__ import annotations

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / "Docs" / "content"
CORE = ROOT / "Sources" / "Hyprism.Core"
PROSE_FILES = [ROOT / "README.md", *sorted(CONTENT.rglob("*.mdx"))]
METHOD_PATTERN = re.compile(r"(?:public\s+)?(.+?)\s+(\w+)\((.*)\);$")
XML_END_TAGS = "summary|param|returns|exception|remarks"
FENCE_PATTERN = re.compile(r"^\s*(?P<marker>`{3,}|~{3,})(?P<info>.*)$")
IMAGE_PATTERN = re.compile(r"!\[(?P<alt>[^\]]*)\]\((?P<target>[^)]+)\)")
LINK_PATTERN = re.compile(r"(?<!!)\[[^\]]+\]\((?P<target>[^)]+)\)")
HEADING_ID_PATTERN = re.compile(r"^#{1,6} .+\{/\* #([\w-]+) \*/\}\s*$")
DASH_PATTERN = re.compile(r"[—–]")


def prose_lines(path: Path):
    """Yield lines outside examples so sample links are not treated as real links."""
    marker = ""
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        fence = FENCE_PATTERN.match(line)
        if fence:
            candidate = fence.group("marker")
            if not marker:
                marker = candidate
            elif candidate[0] == marker[0] and len(candidate) >= len(marker) and not fence.group("info").strip():
                marker = ""
            continue
        if not marker:
            yield number, line


def check_prose() -> list[str]:
    errors: list[str] = []
    for path in PROSE_FILES:
        marker = ""
        opening_line = 0
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            stripped = line.rstrip()
            fence = FENCE_PATTERN.match(stripped)
            if fence:
                candidate = fence.group("marker")
                if not marker:
                    marker = candidate
                    opening_line = number
                    if not fence.group("info").strip():
                        errors.append(
                            f"{path.relative_to(ROOT)}:{number}: add a language to the code fence"
                        )
                elif candidate[0] == marker[0] and len(candidate) >= len(marker) and not fence.group("info").strip():
                    marker = ""
                continue
            if marker:
                continue
            if DASH_PATTERN.search(line):
                errors.append(f"{path.relative_to(ROOT)}:{number}: replace the en dash or em dash")
        if marker:
            errors.append(f"{path.relative_to(ROOT)}:{opening_line}: unclosed code fence")
    return errors


def check_images() -> list[str]:
    errors: list[str] = []

    for path in sorted(CONTENT.rglob("*.mdx")):
        for number, line in prose_lines(path):
            for match in IMAGE_PATTERN.finditer(line):
                if not match.group("alt").strip():
                    errors.append(
                        f"{path.relative_to(ROOT)}:{number}: image alternative text is empty"
                    )
                target = match.group("target").split("#", 1)[0]
                if "://" in target or target.startswith("/"):
                    continue
                resolved = (path.parent / target).resolve()
                if not resolved.is_file():
                    errors.append(
                        f"{path.relative_to(ROOT)}:{number}: image does not exist: {target}"
                    )

    return errors


def check_links() -> list[str]:
    errors: list[str] = []
    for locale in ("en", "ru"):
        pages = {}
        anchors = {}
        for path in sorted((CONTENT / locale).rglob("*.mdx")):
            route = path.relative_to(CONTENT / locale).with_suffix("").as_posix()
            route = route.removesuffix("/index") if route != "index" else ""
            pages[route] = path
            anchors[route] = {
                match.group(1)
                for _, line in prose_lines(path)
                if (match := HEADING_ID_PATTERN.match(line))
            }
        for route, path in pages.items():
            for number, line in prose_lines(path):
                for match in LINK_PATTERN.finditer(line):
                    target = match.group("target")
                    location = f"{path.relative_to(ROOT)}:{number}"
                    if target.startswith("/") or target.startswith("#"):
                        destination, _, anchor = target.partition("#")
                        if not destination:
                            destination_route = route
                        elif destination == "/docs":
                            destination_route = ""
                        else:
                            destination_route = destination.removeprefix("/docs").strip("/")
                        if destination_route not in pages:
                            errors.append(f"{location}: missing documentation route: {target}")
                        elif anchor and anchor not in anchors[destination_route]:
                            errors.append(f"{location}: missing explicit bilingual heading ID: {target}")
                    elif target.startswith("../"):
                        resolved = (path.parent / target.split("#", 1)[0]).resolve()
                        if not resolved.is_relative_to(ROOT) or not resolved.exists():
                            errors.append(f"{location}: missing repository target: {target}")
    return errors


def check_language_parity() -> list[str]:
    errors: list[str] = []
    english = {
        path.relative_to(CONTENT / "en")
        for path in (CONTENT / "en").rglob("*.mdx")
    }
    russian = {
        path.relative_to(CONTENT / "ru")
        for path in (CONTENT / "ru").rglob("*.mdx")
    }
    for missing in sorted(english - russian):
        errors.append(f"Docs/content/ru/{missing}: Russian page is missing")
    for missing in sorted(russian - english):
        errors.append(f"Docs/content/en/{missing}: English page is missing")

    return errors


def split_parameters(raw: str) -> list[str]:
    parameters: list[str] = []
    depth = 0
    current: list[str] = []
    for character in raw:
        if character in "<[(":
            depth += 1
        elif character in ">])":
            depth -= 1
        if character == "," and depth == 0:
            parameters.append("".join(current))
            current = []
        else:
            current.append(character)

    if current:
        parameters.append("".join(current))
    return parameters


def preceding_xml(lines: list[str], index: int) -> str:
    comments: list[str] = []
    cursor = index - 1
    attribute_scan: list[str] = []
    probe = cursor
    while probe >= 0 and probe >= index - 20:
        candidate = lines[probe].strip()
        if candidate.startswith("///") or not candidate:
            break
        attribute_scan.append(candidate)
        if candidate.startswith(
            ("public ", "private ", "protected ", "internal ", "return ", "if ", "}")
        ):
            break
        probe -= 1
    in_attribute = any(candidate.startswith("[") for candidate in attribute_scan)
    while cursor >= 0:
        stripped = lines[cursor].lstrip()
        if stripped.startswith("///"):
            comments.append(stripped)
            in_attribute = False
        elif in_attribute or not stripped or stripped.startswith("["):
            pass
        else:
            break
        cursor -= 1

    return "\n".join(reversed(comments))


def public_method_signature(lines: list[str], index: int) -> tuple[str, str] | None:
    """Return the return type and name for a public method declaration."""
    line = lines[index]
    declaration = line
    cursor = index
    while "(" not in declaration and cursor + 1 < len(lines) and cursor - index < 20:
        cursor += 1
        declaration += f" {lines[cursor].strip()}"

    if "(" not in declaration:
        return None
    signature = declaration.split("(", 1)[0]
    if any(
        marker in signature
        for marker in (
            "{ get",
            " class ",
            " record ",
            " struct ",
            " interface ",
            " enum ",
            " delegate ",
            " event ",
            "=",
        )
    ):
        return None
    signature = re.sub(r"^\s*public\s+", "", signature)
    signature = re.sub(
        r"^(?:static|async|sealed|virtual|override|new|unsafe|partial|extern)\s+",
        "",
        signature,
    )
    signature = re.sub(r"^(?:ref|readonly)\s+", "", signature)
    match = re.match(r"(?P<return_type>.+?)\s+(?P<name>[A-Za-z_]\w*)\s*$", signature)
    if not match:
        return None
    return_type = " ".join(match.group("return_type").split())
    if return_type in {"void", "public"} or return_type.startswith("public"):
        return None
    return return_type, match.group("name")


def check_core_public_api() -> list[str]:
    """Check summaries for public types and returns for public non-void methods."""
    errors: list[str] = []
    public_type_pattern = re.compile(
        r"^\s*public\s+(?:(?:abstract|sealed|static|partial|readonly|unsafe)\s+)*"
        r"(?:class|record(?:\s+(?:class|struct))?|struct|interface|enum|delegate)\b"
    )

    for path in sorted(CORE.rglob("*.cs")):
        lines = path.read_text(encoding="utf-8").splitlines()
        for index, line in enumerate(lines):
            if public_type_pattern.match(line):
                location = f"{path.relative_to(ROOT)}:{index + 1}"
                if "<summary>" not in preceding_xml(lines, index):
                    errors.append(f"{location}: public type has no summary")
        enum_body_depth: int | None = None
        enum_pending = False
        brace_depth = 0
        for index, line in enumerate(lines):
            if re.match(r"^\s*public\s+enum\b", line):
                enum_pending = True
            if enum_pending and "{" in line:
                enum_body_depth = brace_depth + 1
                enum_pending = False
            elif enum_body_depth is not None and brace_depth == enum_body_depth:
                candidate = line.split("//", 1)[0].strip().rstrip(",").strip()
                if re.match(r"^[A-Za-z_]\w*(?:\s*=\s*.+)?$", candidate):
                    location = f"{path.relative_to(ROOT)}:{index + 1}"
                    if "<summary>" not in preceding_xml(lines, index):
                        errors.append(f"{location}: public enum member has no summary")
            brace_depth += line.count("{") - line.count("}")
            if enum_body_depth is not None and brace_depth < enum_body_depth:
                enum_body_depth = None

        for index, line in enumerate(lines):
            if not line.lstrip().startswith("public"):
                continue
            signature = public_method_signature(lines, index)
            if signature is None:
                continue
            return_type, method_name = signature
            documentation = preceding_xml(lines, index)
            if "<inheritdoc" in documentation:
                continue
            if "<returns>" not in documentation:
                location = f"{path.relative_to(ROOT)}:{index + 1}"
                errors.append(f"{location}: {method_name} has no returns entry")

    return errors


def check_core_inheritdoc_blocks() -> list[str]:
    """Ensure inherited implementation docs contain no additional XML tags."""
    errors: list[str] = []

    for path in sorted(CORE.rglob("*.cs")):
        lines = path.read_text(encoding="utf-8").splitlines()
        for index, line in enumerate(lines):
            if "<inheritdoc" not in line:
                continue
            cursor = index + 1
            while cursor < len(lines) and (not lines[cursor].strip() or lines[cursor].lstrip().startswith("///")):
                if lines[cursor].lstrip().startswith("///") and "<inheritdoc" not in lines[cursor]:
                    errors.append(
                        f"{path.relative_to(ROOT)}:{cursor + 1}: remove XML documentation after inheritdoc and document the interface instead"
                    )
                cursor += 1

    return errors


def check_core_contracts() -> list[str]:
    errors: list[str] = []

    for path in sorted(CORE.rglob("I*.cs")):
        lines = path.read_text(encoding="utf-8").splitlines()
        if not any("public interface " in line for line in lines):
            continue
        for index, line in enumerate(lines):
            if not line.lstrip().startswith("///"):
                continue
            location = f"{path.relative_to(ROOT)}:{index + 1}"
            if DASH_PATTERN.search(line):
                errors.append(f"{location}: replace the en dash or em dash")
        inside_interface = False
        for index, line in enumerate(lines):
            if "public interface " in line:
                inside_interface = True
            signature = line.strip()
            if not inside_interface or not signature.endswith(";") or "(" not in signature:
                continue
            if signature.startswith(("///", "event ")):
                continue

            match = METHOD_PATTERN.match(signature)
            if not match:
                continue
            return_type, method_name, raw_parameters = match.groups()
            documentation = preceding_xml(lines, index)
            location = f"{path.relative_to(ROOT)}:{index + 1}"

            if "<summary>" not in documentation:
                errors.append(f"{location}: {method_name} has no summary")
            for parameter in split_parameters(raw_parameters):
                declaration = parameter.split("=", 1)[0].strip()
                name_match = re.search(r"([A-Za-z_]\w*)\s*$", declaration)
                if name_match and f'<param name="{name_match.group(1)}"' not in documentation:
                    errors.append(
                        f"{location}: {method_name} has no param entry for {name_match.group(1)}"
                    )
            if return_type.strip() != "void" and "<returns>" not in documentation:
                errors.append(f"{location}: {method_name} has no returns entry")

    return errors


def main() -> int:
    errors = [
        *check_prose(),
        *check_images(),
        *check_links(),
        *check_language_parity(),
        *check_core_public_api(),
        *check_core_inheritdoc_blocks(),
        *check_core_contracts(),
    ]
    if not errors:
        print("Documentation checks passed")
        return 0

    print("Documentation checks failed", file=sys.stderr)
    for error in errors:
        print(f"  {error}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
