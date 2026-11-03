#!/usr/bin/env python3
"""Record and verify the bundled iOS framework's release metadata and tree hash."""

import argparse
import hashlib
from pathlib import Path
import plistlib
import re
import sys
from typing import Dict, Optional

VERSION = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?")
WIDGET = re.compile(r"[0-9a-f]{16}")
SHA256 = re.compile(r"[0-9a-f]{64}")


def one(pattern: str, text: str, description: str) -> str:
    matches = re.findall(pattern, text)
    if len(matches) != 1:
        raise ValueError(f"Expected exactly one {description}, found {len(matches)}")
    return matches[0]


def source_version(source: Path) -> str:
    text = (source / "Sources/Cluefolk/Cluefolk.swift").read_text()
    return one(r'\bsdkVersion\s*=\s*"([^"]+)"', text, "iOS SDK version")


def source_widget(source: Path) -> str:
    text = (source / "Sources/Cluefolk/WidgetAssets.swift").read_text()
    generated = one(r"widget build ([0-9a-f]+)", text, "generated widget digest")
    declared = one(r'\bstatic let version\s*=\s*"([0-9a-f]+)"', text,
                   "declared widget digest")
    if generated != declared:
        raise ValueError("The generated and declared widget digests do not match")
    if not WIDGET.fullmatch(declared):
        raise ValueError("The widget digest must be 16 lowercase hexadecimal characters")
    return declared


def tree_sha256(directory: Path) -> str:
    root = directory.resolve()
    if not root.is_dir():
        raise ValueError(f"Framework directory is missing: {directory}")
    aggregate = hashlib.sha256()
    files = sorted((path for path in root.rglob("*") if path.is_file()),
                   key=lambda path: path.relative_to(root).as_posix())
    if not files:
        raise ValueError(f"Framework directory is empty: {directory}")
    for path in files:
        relative = path.relative_to(root).as_posix()
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        aggregate.update(relative.encode("utf-8") + b"\0" +
                         digest.encode("ascii") + b"\n")
    return aggregate.hexdigest()


def validate_built(framework: Path, expected_version: str) -> None:
    if not VERSION.fullmatch(expected_version):
        raise ValueError(f"Invalid expected SDK version: {expected_version}")
    plists = sorted(framework.glob("*/Cluefolk.framework/Info.plist"))
    if len(plists) != 2:
        raise ValueError(f"Expected two Cluefolk.framework slices, found {len(plists)}")
    for path in plists:
        values = plistlib.loads(path.read_bytes())
        for key in ("CFBundleShortVersionString", "CFBundleVersion"):
            if values.get(key) != expected_version:
                raise ValueError(f"{path}: {key} is not {expected_version}")
        if values.get("CFBundleIdentifier") != "com.cluefolk.sdk":
            raise ValueError(f"{path}: unexpected CFBundleIdentifier")
        executable = path.parent / str(values.get("CFBundleExecutable", ""))
        if not executable.is_file():
            raise ValueError(f"{path}: framework executable is missing")


def parse_manifest(path: Path) -> Dict[str, str]:
    if not path.is_file():
        raise ValueError(f"Integrity manifest is missing: {path}")
    values: Dict[str, str] = {}
    for raw in path.read_text().splitlines():
        line = raw.strip()
        if not line:
            continue
        if "=" not in line:
            raise ValueError("Invalid integrity manifest line")
        key, value = line.split("=", 1)
        if not key or not value or key in values:
            raise ValueError("Invalid or duplicate integrity manifest key")
        values[key] = value
    if set(values) != {"version", "widget", "sha256"}:
        raise ValueError("Integrity manifest must contain version, widget and sha256")
    if not VERSION.fullmatch(values["version"]):
        raise ValueError("Invalid version in integrity manifest")
    if not WIDGET.fullmatch(values["widget"]):
        raise ValueError("Invalid widget digest in integrity manifest")
    if not SHA256.fullmatch(values["sha256"]):
        raise ValueError("Invalid SHA-256 in integrity manifest")
    return values


def write_manifest(framework: Path, manifest: Path, version: str,
                   widget: str) -> None:
    if not VERSION.fullmatch(version):
        raise ValueError(f"Invalid SDK version: {version}")
    if not WIDGET.fullmatch(widget):
        raise ValueError(f"Invalid widget digest: {widget}")
    validate_built(framework, version)
    contents = (f"version={version}\nwidget={widget}\n"
                f"sha256={tree_sha256(framework)}\n")
    temporary = manifest.with_name(manifest.name + ".tmp")
    temporary.write_text(contents)
    temporary.replace(manifest)


def verify(framework: Path, manifest: Path, expected_version: str,
           expected_widget: Optional[str]) -> None:
    values = parse_manifest(manifest)
    if values["version"] != expected_version:
        raise ValueError(
            f"Framework version {values['version']} does not match {expected_version}")
    if expected_widget is not None and values["widget"] != expected_widget:
        raise ValueError(
            f"Widget digest {values['widget']} does not match {expected_widget}")
    validate_built(framework, expected_version)
    actual = tree_sha256(framework)
    if values["sha256"] != actual:
        raise ValueError(
            f"Framework SHA-256 {actual} does not match {values['sha256']}")


def main() -> None:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)
    source = subparsers.add_parser("source")
    source.add_argument("ios_sdk", type=Path)
    tree = subparsers.add_parser("tree")
    tree.add_argument("framework", type=Path)
    write = subparsers.add_parser("write")
    write.add_argument("framework", type=Path)
    write.add_argument("manifest", type=Path)
    write.add_argument("version")
    write.add_argument("widget")
    check = subparsers.add_parser("verify")
    check.add_argument("framework", type=Path)
    check.add_argument("manifest", type=Path)
    check.add_argument("version")
    check.add_argument("widget", nargs="?")
    args = parser.parse_args()

    if args.command == "source":
        print(source_version(args.ios_sdk))
        print(source_widget(args.ios_sdk))
    elif args.command == "tree":
        print(tree_sha256(args.framework))
    elif args.command == "write":
        write_manifest(args.framework, args.manifest, args.version, args.widget)
    else:
        verify(args.framework, args.manifest, args.version, args.widget)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, plistlib.InvalidFileException) as error:
        print(f"ios-framework-integrity: {error}", file=sys.stderr)
        raise SystemExit(1)
