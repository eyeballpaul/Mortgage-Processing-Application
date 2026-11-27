#!/usr/bin/env python3
"""Determine whether repository changes affect a deployable project."""

import argparse
import fnmatch
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path


def project_inputs(root: Path, project: Path) -> tuple[set[str], set[str]]:
    directories: set[str] = set()
    linked_files: set[str] = set()
    visited: set[Path] = set()

    def relative(path: Path) -> str:
        return path.resolve().relative_to(root).as_posix()

    def visit(path: Path) -> None:
        path = path.resolve()
        if path in visited:
            return
        visited.add(path)
        directories.add(relative(path.parent))
        tree = ET.parse(path)
        for item in tree.iter():
            name = item.tag.rsplit("}", 1)[-1]
            include = item.get("Include")
            if not include:
                continue
            include = include.replace("\\", "/")
            if name == "ProjectReference":
                if "$(" in include or "*" in include:
                    raise ValueError(f"Unsupported project reference in {path}: {include}")
                visit(path.parent / include)
            elif name in {"Compile", "Content", "EmbeddedResource", "None"} and (
                include.startswith("../") or include.startswith("..\\")
            ):
                if "$(" in include:
                    raise ValueError(f"Unsupported linked input in {path}: {include}")
                linked_files.add(relative(path.parent / include))

    visit(root / project)
    return directories, linked_files


def affects(root: Path, project: Path, changed: list[str]) -> bool:
    directories, linked_files = project_inputs(root, project)
    shared_build_files = {
        ".dockerignore", "global.json", "NuGet.config", "nuget.config",
        "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
    }
    for path in changed:
        name = Path(path).name
        if name in shared_build_files or name.endswith((".sln", ".slnx")):
            return True
        if any(path == directory or path.startswith(directory + "/") for directory in directories):
            if path.endswith(".md"):
                continue
            return True
        if any(fnmatch.fnmatchcase(path, pattern) for pattern in linked_files):
            return True
        if path.endswith(".md") or path.startswith((".github/", "docs/", "infra/")):
            continue
        if "/" not in path:
            return True
        top = path.split("/", 1)[0]
        if not (root / top).is_dir():
            return True
    return False


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    args = parser.parse_args()
    root = Path(subprocess.check_output(
        ["git", "rev-parse", "--show-toplevel"], text=True
    ).strip()).resolve()
    result = subprocess.check_output(
        ["git", "diff", "--no-renames", "--name-only", "-z", args.base, args.head],
        cwd=root,
    )
    changed = [path.decode("utf-8") for path in result.split(b"\0") if path]
    print("true" if affects(root, args.project, changed) else "false")


if __name__ == "__main__":
    main()
