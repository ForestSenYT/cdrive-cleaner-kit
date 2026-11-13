#!/usr/bin/env python3
"""Reference implementation of SPEC.md (rule-driven disk cleaner). Windows only, standard library only.

Usage:
  python cleaner_ref.py scan  --rules FILE [--json] [--list]
  python cleaner_ref.py clean --rules FILE --select ID[,ID...] --yes [--json]

SPEC.md is authoritative; this file exists to answer edge-case questions by running it.
"""
import argparse
import ctypes
import json
import os
import re
import stat
import sys
import time
from ctypes import wintypes

SEP = chr(92)
ID_PATTERN = re.compile("^[a-z0-9][a-z0-9-]*$")
VARIABLE = re.compile("%([^%]+)%")
DRIVE = re.compile("^[A-Za-z]:$")
TOP_KEYS = {"$schema", "format", "rules"}
RULE_KEYS = {"id", "name", "group", "description", "note", "risk", "default_selected", "kind", "min_age_hours", "targets"}
TARGET_KEYS = {"root", "include", "exclude"}
PROTECTED_VARIABLES = ["SystemRoot", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData",
                       "USERPROFILE", "APPDATA", "LOCALAPPDATA"]
PROTECTED_SYSTEM_SUBDIRECTORIES = ["System32", "SysWOW64", "WinSxS"]
DIRECTORY = stat.FILE_ATTRIBUTE_DIRECTORY
REPARSE = stat.FILE_ATTRIBUTE_REPARSE_POINT


class RuleFileError(Exception):
    """The rule file is rejected as a whole (exit code 2)."""


# ---------------------------------------------------------------- rule file (SPEC 2)

def segments_of(text):
    return text.replace("/", SEP).rstrip(SEP).split(SEP)


def check_relative_pattern(text, where):
    if not isinstance(text, str) or not text.strip():
        raise RuleFileError(f"{where}: pattern must be a non-empty string")
    for segment in segments_of(text):
        if segment in ("", ".", ".."):
            raise RuleFileError(f"{where}: pattern {text!r} must be relative, without empty, dot or dot-dot segments")
        if ":" in segment or "%" in segment:
            raise RuleFileError(f"{where}: pattern {text!r} must not contain a colon or a percent sign")


def check_root(text, where):
    if not isinstance(text, str) or not text.strip():
        raise RuleFileError(f"{where}: root must be a non-empty string")
    segments = segments_of(text)
    if any(segment in ("", ".", "..") for segment in segments):
        raise RuleFileError(f"{where}: root {text!r} must not contain empty, dot or dot-dot segments")
    if "*" in segments[0] or not (segments[0].startswith("%") or DRIVE.match(segments[0])):
        raise RuleFileError(f"{where}: root {text!r} must start with a drive letter or a %VARIABLE%")


def load_rules(path):
    try:
        with open(path, encoding="utf-8-sig") as handle:
            document = json.load(handle)
    except (OSError, ValueError) as error:
        raise RuleFileError(f"cannot read rule file: {error}") from error
    if not isinstance(document, dict) or set(document) - TOP_KEYS:
        raise RuleFileError("top level must be an object holding only format and rules")
    if document.get("format") != 1 or isinstance(document.get("format"), bool):
        raise RuleFileError("unsupported format (expected 1)")
    rules = document.get("rules")
    if not isinstance(rules, list):
        raise RuleFileError("rules must be an array")
    seen = set()
    for index, rule in enumerate(rules):
        where = f"rules[{index}]"
        if not isinstance(rule, dict):
            raise RuleFileError(f"{where}: must be an object")
        unknown = set(rule) - RULE_KEYS
        if unknown:
            raise RuleFileError(f"{where}: unknown field(s) {sorted(unknown)}")
        for key in ("id", "name", "group", "kind"):
            if not isinstance(rule.get(key), str) or not rule[key]:
                raise RuleFileError(f"{where}: field {key} is required")
        identifier = rule["id"]
        if not ID_PATTERN.match(identifier) or identifier in seen:
            raise RuleFileError(f"{where}: id {identifier!r} is malformed or duplicated")
        seen.add(identifier)
        if rule.get("risk", "low") not in ("low", "medium"):
            raise RuleFileError(f"{where}: risk must be low or medium")
        if not isinstance(rule.get("default_selected", False), bool):
            raise RuleFileError(f"{where}: default_selected must be a boolean")
        for key in ("description", "note"):
            if key in rule and not isinstance(rule[key], str):
                raise RuleFileError(f"{where}: field {key} must be a string")
        kind = rule["kind"]
        if kind == "recycle_bin":
            if "targets" in rule or "min_age_hours" in rule:
                raise RuleFileError(f"{where}: recycle_bin rules take no targets or min_age_hours")
            continue
        if kind != "files":
            raise RuleFileError(f"{where}: unknown kind {kind!r}")
        age = rule.get("min_age_hours", 0)
        if isinstance(age, bool) or not isinstance(age, (int, float)) or age < 0:
            raise RuleFileError(f"{where}: min_age_hours must be a number >= 0")
        targets = rule.get("targets")
        if not isinstance(targets, list) or not targets:
            raise RuleFileError(f"{where}: files rules need at least one target")
        for number, target in enumerate(targets):
            place = f"{where}.targets[{number}]"
            if not isinstance(target, dict) or set(target) - TARGET_KEYS:
                raise RuleFileError(f"{place}: must be an object holding only root, include and exclude")
            check_root(target.get("root"), place)
            for key in ("include", "exclude"):
                patterns = target.get(key, [])
                if not isinstance(patterns, list):
                    raise RuleFileError(f"{place}: field {key} must be an array")
                for pattern in patterns:
                    check_relative_pattern(pattern, f"{place}.{key}")
    return rules


# ---------------------------------------------------------------- file system helpers

def system_path(path):
    """Extended-length form for long paths; plain paths stay untouched."""
    if len(path) >= 248 and not path.startswith(SEP + SEP):
        return SEP + SEP + "?" + SEP + path
    return path


def join(directory, name):
    return directory + name if directory.endswith(SEP) else directory + SEP + name


def attributes_of(path):
    try:
        return os.lstat(system_path(path))
    except OSError:
        return None


def list_directory(directory):
    """Sorted (name, stat) pairs without following links; unreadable directories read as empty."""
    entries = []
    try:
        with os.scandir(system_path(directory)) as iterator:
            for entry in iterator:
                try:
                    entries.append((entry.name, entry.stat(follow_symlinks=False)))
                except OSError:
                    continue
    except OSError:
        return []
    entries.sort(key=lambda item: item[0].lower())
    return entries


def segment_matcher(segment):
    """A star matches any run of characters inside one path segment; comparison ignores case."""
    if "*" not in segment:
        wanted = segment.lower()
        return lambda name: name.lower() == wanted
    expression = re.compile(".*".join(re.escape(part) for part in segment.split("*")), re.IGNORECASE | re.DOTALL)
    return lambda name: expression.fullmatch(name) is not None


def protected_directories():
    found = set()
    for variable in PROTECTED_VARIABLES:
        value = os.environ.get(variable)
        if value:
            found.add(value.replace("/", SEP).rstrip(SEP).lower())
    system_root = os.environ.get("SystemRoot")
    if system_root:
        for name in PROTECTED_SYSTEM_SUBDIRECTORIES:
            found.add(join(system_root.rstrip(SEP), name).lower())
    system_drive = os.environ.get("SystemDrive")
    if system_drive:
        found.add(join(system_drive.rstrip(SEP) + SEP, "Users").lower())
    return found


def is_protected(path, protected):
    normalized = path.rstrip(SEP).lower()
    return DRIVE.match(normalized) is not None or normalized in protected


kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
kernel32.CreateFileW.restype = wintypes.HANDLE
kernel32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
                                 wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
INVALID_HANDLE_VALUE = wintypes.HANDLE(-1).value


def can_open_shared_read(path):
    """In-use probe (SPEC 7): GENERIC_READ with FILE_SHARE_READ only, OPEN_EXISTING."""
    handle = kernel32.CreateFileW(system_path(path), 0x80000000, 0x00000001, None, 3, 0x00000080, None)
    if handle is None or handle == INVALID_HANDLE_VALUE:
        return False
    kernel32.CloseHandle(handle)
    return True


# ---------------------------------------------------------------- expansion and matching (SPEC 3, 4)

def expand_variables(text):
    missing = []

    def replace(match):
        value = os.environ.get(match.group(1))
        if value is None:
            missing.append(match.group(1))
            return match.group(0)
        return value

    return VARIABLE.sub(replace, text), missing


def expand_root(expanded, warn):
    """Concrete existing root directories for an environment-expanded root string."""
    segments = segments_of(expanded)
    if not DRIVE.match(segments[0]) or any(segment in ("", ".", "..") for segment in segments[1:]):
        warn("invalid_root", expanded)
        return []
    current = [segments[0].upper() + SEP]
    for segment in segments[1:]:
        following = []
        if "*" in segment:
            matches = segment_matcher(segment)
            for directory in current:
                for name, info in list_directory(directory):
                    flags = info.st_file_attributes
                    if flags & DIRECTORY and not flags & REPARSE and matches(name):
                        following.append(join(directory, name))
        else:
            for directory in current:
                candidate = join(directory, segment)
                info = attributes_of(candidate)
                if info is None or not info.st_file_attributes & DIRECTORY:
                    continue
                if info.st_file_attributes & REPARSE:
                    warn("reparse_point_in_root", candidate)
                    continue
                following.append(candidate)
        current = following
    return current


def walk_files(directory):
    """Every regular file below directory; reparse points are neither entered nor reported."""
    pending = [directory]
    while pending:
        current = pending.pop()
        for name, info in list_directory(current):
            flags = info.st_file_attributes
            if flags & REPARSE:
                continue
            child = join(current, name)
            if flags & DIRECTORY:
                pending.append(child)
            else:
                yield child, info.st_size, info.st_mtime


def resolve_target(root, include, protected, warn):
    """Files selected by one concrete root: the whole root when include is empty, else each pattern."""

    def walk_guarded(directory):
        if is_protected(directory, protected):
            warn("protected_root", directory)
            return
        yield from walk_files(directory)

    if not include:
        yield from walk_guarded(root)
        return
    for pattern in include:
        if is_protected(root, protected) and all(set(segment) == {"*"} for segment in pattern):
            warn("protected_root", root)
            continue
        parents = [root]
        for segment in pattern[:-1]:
            matches = segment_matcher(segment)
            following = []
            for directory in parents:
                for name, info in list_directory(directory):
                    flags = info.st_file_attributes
                    if flags & DIRECTORY and not flags & REPARSE and matches(name):
                        following.append(join(directory, name))
            parents = following
        matches = segment_matcher(pattern[-1])
        for directory in parents:
            for name, info in list_directory(directory):
                flags = info.st_file_attributes
                if flags & REPARSE or not matches(name):
                    continue
                child = join(directory, name)
                if flags & DIRECTORY:
                    yield from walk_guarded(child)
                else:
                    yield child, info.st_size, info.st_mtime


def is_excluded(relative, exclude):
    return any(len(pattern) <= len(relative) and all(match(part) for match, part in zip(pattern, relative))
               for pattern in exclude)


# ---------------------------------------------------------------- recycle bin (SPEC 6)

class SHQUERYRBINFO(ctypes.Structure):
    if ctypes.sizeof(ctypes.c_void_p) == 4:
        _pack_ = 1  # shellapi.h packs this structure only in 32-bit builds
    _fields_ = [("cbSize", wintypes.DWORD), ("i64Size", ctypes.c_longlong), ("i64NumItems", ctypes.c_longlong)]


def query_recycle_bin():
    drive = os.environ.get("SystemDrive", "C:").rstrip(SEP) + SEP
    info = SHQUERYRBINFO()
    info.cbSize = ctypes.sizeof(SHQUERYRBINFO)
    shell32 = ctypes.WinDLL("shell32")
    shell32.SHQueryRecycleBinW.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(SHQUERYRBINFO)]
    shell32.SHQueryRecycleBinW.restype = ctypes.c_long
    if shell32.SHQueryRecycleBinW(drive, ctypes.byref(info)) != 0:
        return None
    return int(info.i64NumItems), int(info.i64Size)


# ---------------------------------------------------------------- scan (SPEC 5)

def scan(rules, list_paths):
    protected = protected_directories()
    claimed = set()
    now = time.time()
    report = {"format": 1, "rules": [], "total_files": 0, "total_bytes": 0}
    for rule in rules:
        entry = {"id": rule["id"], "kind": rule["kind"], "files": 0, "bytes": 0,
                 "skipped": {"too_new": 0}, "warnings": []}
        paths = []
        if rule["kind"] == "recycle_bin":
            answer = query_recycle_bin()
            if answer is None:
                entry["warnings"].append({"code": "recycle_bin_query_failed", "target": None, "detail": ""})
            else:
                entry["files"], entry["bytes"] = answer
        else:
            matched = {}
            for index, target in enumerate(rule["targets"]):
                def warn(code, detail, index=index):
                    entry["warnings"].append({"code": code, "target": index, "detail": detail})

                expanded, missing = expand_variables(target["root"])
                if missing:
                    warn("unresolved_variable", ",".join(missing))
                    continue
                include = [segments_of(pattern) for pattern in target.get("include", [])]
                exclude = [[segment_matcher(part) for part in segments_of(pattern)]
                           for pattern in target.get("exclude", [])]
                for root in expand_root(expanded, warn):
                    prefix = root if root.endswith(SEP) else root + SEP
                    for path, size, modified in resolve_target(root, include, protected, warn):
                        if is_excluded(path[len(prefix):].split(SEP), exclude):
                            continue
                        matched.setdefault(path.lower(), (path, size, modified))
            minimum_age = rule.get("min_age_hours", 0) * 3600
            for key in sorted(matched):
                if key in claimed:
                    continue
                claimed.add(key)  # the first matching rule owns the file, whatever its filters decide
                path, size, modified = matched[key]
                if minimum_age and now - modified < minimum_age:
                    entry["skipped"]["too_new"] += 1
                else:  # no per-file open here: the in-use probe belongs to clean (SPEC 7, 10)
                    entry["files"] += 1
                    entry["bytes"] += size
                    paths.append(path)
        entry["_paths"] = paths
        if list_paths:
            entry["paths"] = paths
        report["rules"].append(entry)
        report["total_files"] += entry["files"]
        report["total_bytes"] += entry["bytes"]
    return report


# ---------------------------------------------------------------- clean (SPEC 7)

def delete_file(path):
    info = attributes_of(path)
    if info is None:
        return "gone"
    if info.st_file_attributes & (DIRECTORY | REPARSE):
        return "not_regular_file"
    if not can_open_shared_read(path):
        return "in_use_or_denied"
    try:
        os.remove(system_path(path))
    except OSError:
        return "delete_failed"
    return None


def clean(rules, selected):
    scanned = scan(rules, list_paths=False)
    report = {"format": 1, "rules": [], "total_deleted_files": 0, "total_deleted_bytes": 0}
    for entry in scanned["rules"]:
        if entry["id"] not in selected:
            continue
        result = {"id": entry["id"], "kind": entry["kind"], "deleted_files": 0, "deleted_bytes": 0, "failed": []}
        if entry["kind"] == "recycle_bin":
            # SPEC 6 defines SHEmptyRecycleBinW; the reference deliberately leaves the bin alone.
            result["failed"].append({"path": "", "error": "recycle_bin_clean_not_in_reference"})
        else:
            for path in entry["_paths"]:
                size = getattr(attributes_of(path), "st_size", 0)
                error = delete_file(path)
                if error is None:
                    result["deleted_files"] += 1
                    result["deleted_bytes"] += size
                else:
                    result["failed"].append({"path": path, "error": error})
        report["rules"].append(result)
        report["total_deleted_files"] += result["deleted_files"]
        report["total_deleted_bytes"] += result["deleted_bytes"]
    return report


# ---------------------------------------------------------------- command line (SPEC 8)

def emit(report, as_json):
    for entry in report["rules"]:
        entry.pop("_paths", None)
    if as_json:
        json.dump(report, sys.stdout, ensure_ascii=False, indent=2)
        sys.stdout.write("\n")
        return
    mebibyte = 1048576
    for entry in report["rules"]:
        name = entry["id"]
        if "files" in entry:
            codes = ", ".join(sorted({warning["code"] for warning in entry["warnings"]}))
            line = (f"{name:<28} {entry['files']:>8} files {entry['bytes'] / mebibyte:>10.1f} MiB"
                    f"  skipped too-new {entry['skipped']['too_new']}")
            print(line + (f"  [{codes}]" if codes else ""))
        else:
            print(f"{name:<28} deleted {entry['deleted_files']:>8} files "
                  f"{entry['deleted_bytes'] / mebibyte:>10.1f} MiB, failed {len(entry['failed'])}")
    for key in ("total_files", "total_bytes", "total_deleted_files", "total_deleted_bytes"):
        if key in report:
            print(f"{key}: {report[key]}")


def main():
    parser = argparse.ArgumentParser(description="Reference cleaner (see SPEC.md)")
    commands = parser.add_subparsers(dest="command", required=True)
    scan_parser = commands.add_parser("scan")
    scan_parser.add_argument("--rules", required=True)
    scan_parser.add_argument("--json", action="store_true")
    scan_parser.add_argument("--list", action="store_true")
    clean_parser = commands.add_parser("clean")
    clean_parser.add_argument("--rules", required=True)
    clean_parser.add_argument("--select", required=True)
    clean_parser.add_argument("--yes", action="store_true")
    clean_parser.add_argument("--json", action="store_true")
    arguments = parser.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    try:
        rules = load_rules(arguments.rules)
        if arguments.command == "scan":
            emit(scan(rules, arguments.list), arguments.json)
            return 0
        selected = {item for item in arguments.select.split(",") if item}
        unknown = selected - {rule["id"] for rule in rules}
        if unknown or not selected:
            raise RuleFileError(f"--select names unknown rule id(s): {sorted(unknown)}")
        if not arguments.yes:
            raise RuleFileError("clean deletes files permanently; pass --yes to confirm")
        emit(clean(rules, selected), arguments.json)
        return 0
    except RuleFileError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
