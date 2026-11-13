#!/usr/bin/env python3
"""Conformance harness for SPEC.md. Windows only, standard library only.

Builds a throwaway fixture tree under the temp directory, runs the cleaner under test through the
command-line contract of SPEC section 8, and compares what it reports and deletes with the expectations below.

Usage:
  python run_conformance.py --tool "<command prefix>" [--only load|scan|clean] [--keep]
Example:
  python run_conformance.py --tool "python ../reference/cleaner_ref.py"
"""
import _winapi
import argparse
import ctypes
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
from ctypes import wintypes

KERNEL32 = ctypes.WinDLL("kernel32", use_last_error=True)
KERNEL32.CreateFileW.restype = wintypes.HANDLE
KERNEL32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
                                 wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
KERNEL32.CloseHandle.argtypes = [wintypes.HANDLE]
INVALID_HANDLE_VALUE = wintypes.HANDLE(-1).value

HERE = os.path.dirname(os.path.abspath(__file__))
FIXTURE_RULES = os.path.join(HERE, "fixture_rules.json")
OLD = 72  # hours
NEW = 1

# (relative path, size in bytes, age in hours, id of the rule that must report it or None)
FILES = [
    ("temp/a.tmp", 100, OLD, "whole-root"),
    ("temp/sub/deep/b.bin", 200, OLD, "whole-root"),
    ("temp/new.tmp", 50, NEW, None),                 # younger than min_age_hours
    ("temp/locked.tmp", 70, OLD, "whole-root"),      # reported by scan, but held open: clean must leave it
    ("outside/secret.txt", 33, OLD, None),           # reachable only through junction temp/linkdir
    ("outside/cache/c3", 77, OLD, None),             # reachable only through junction users/dave
    ("logs/x.log", 10, OLD, "pattern"),
    ("logs/y.txt", 20, OLD, None),
    ("logs/sub/z.log", 30, OLD, None),               # a star never crosses a path separator
    ("logs/app_1/trace.log", 40, OLD, "pattern"),
    ("logs/app_2/trace.log", 41, OLD, "pattern"),
    ("logs/apple/trace.txt", 5, OLD, None),
    ("users/alice/cache/c1", 300, OLD, "wild-root"),
    ("users/bob/cache/deep/c2", 400, OLD, "wild-root"),
    ("users/bob/keep/k", 9, OLD, None),
    ("users/carol/nocache/n", 1, OLD, None),
    ("cachex/keep/important.dat", 500, OLD, None),   # excluded directory
    ("cachex/drop/d1", 600, OLD, "exclude"),
    ("cachex/drop/keepme.cfg", 7, OLD, None),        # excluded by pattern
    ("cachex/top.dat", 8, OLD, "exclude"),
    ("case/UPPER.LOG", 11, OLD, "case"),
    ("dirmatch/Minidump/m1.dmp", 12, OLD, "dirmatch"),
    ("dirmatch/MEMORY.DMP", 13, OLD, "dirmatch"),
    ("dirmatch/other.dmp", 14, OLD, None),
]
JUNCTIONS = [("temp/linkdir", "outside"), ("users/dave", "outside")]  # (link, existing target)
LOCKED = "temp/locked.tmp"
RULE_ORDER = ["whole-root", "overlap", "pattern", "wild-root", "exclude", "case", "dirmatch",
              "unresolved", "missing", "protected"]
EXPECTED_SKIPPED = {"whole-root": {"too_new": 1}}
UNDELETABLE = {LOCKED: "in_use_or_denied"}  # relative path -> error code clean must report
EXPECTED_WARNINGS = {"unresolved": ["unresolved_variable"], "protected": ["protected_root", "protected_root"]}
CLEAN_SELECTION = ["whole-root", "exclude"]


def base_rule(**changes):
    rule = {"id": "r1", "name": "n", "group": "g", "kind": "files", "targets": [{"root": "%CLEANKIT_FIXTURE%/temp"}]}
    rule.update(changes)
    return rule


# Rule documents that must be rejected as a whole (non-zero exit, SPEC section 2).
INVALID_DOCUMENTS = {
    "not-json": "{",
    "wrong-format": {"format": 2, "rules": [base_rule()]},
    "duplicate-id": {"format": 1, "rules": [base_rule(), base_rule()]},
    "malformed-id": {"format": 1, "rules": [base_rule(id="Bad Id")]},
    "unknown-kind": {"format": 1, "rules": [base_rule(kind="registry")]},
    "files-without-targets": {"format": 1, "rules": [base_rule(targets=[])]},
    "unknown-rule-field": {"format": 1, "rules": [base_rule(min_age_hour=24)]},
    "unknown-target-field": {"format": 1, "rules": [base_rule(targets=[{"root": "%CLEANKIT_FIXTURE%/temp", "exlude": ["a"]}])]},
    "negative-age": {"format": 1, "rules": [base_rule(min_age_hours=-1)]},
    "include-dot-dot": {"format": 1, "rules": [base_rule(targets=[{"root": "%CLEANKIT_FIXTURE%/temp", "include": ["../logs"]}])]},
    "include-absolute": {"format": 1, "rules": [base_rule(targets=[{"root": "%CLEANKIT_FIXTURE%/temp", "include": ["C:/x"]}])]},
    "exclude-leading-separator": {"format": 1, "rules": [base_rule(targets=[{"root": "%CLEANKIT_FIXTURE%/temp", "exclude": ["/a"]}])]},
    "root-relative": {"format": 1, "rules": [base_rule(targets=[{"root": "temp/x"}])]},
    "root-wildcard-first-segment": {"format": 1, "rules": [base_rule(targets=[{"root": "*/x"}])]},
    "root-dot-dot": {"format": 1, "rules": [base_rule(targets=[{"root": "%CLEANKIT_FIXTURE%/temp/../logs"}])]},
    "recycle-bin-with-targets": {"format": 1, "rules": [base_rule(kind="recycle_bin")]},
}


class Fixture:
    def __init__(self):
        self.base = os.path.realpath(tempfile.mkdtemp(prefix="cleankit-"))
        self.locked_handle = None
        now = time.time()
        for relative, size, age, _owner in FILES:
            path = self.path(relative)
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, "wb") as handle:
                handle.write(b"x" * size)
            stamp = now - age * 3600
            os.utime(path, (stamp, stamp))
        for link, target in JUNCTIONS:
            _winapi.CreateJunction(self.path(target), self.path(link))
        # Another writer that shares read, write and delete: a bare delete would succeed and pull the file
        # out from under it, so only the shared-read probe of SPEC section 7 keeps this file safe.
        self.locked_handle = KERNEL32.CreateFileW(self.path(LOCKED), 0x40000000, 0x00000007, None, 3, 0x00000080, None)
        if self.locked_handle in (None, INVALID_HANDLE_VALUE):
            raise OSError("cannot hold the locked fixture file open")
        stamp = now - OLD * 3600
        os.utime(self.path(LOCKED), (stamp, stamp))

    def path(self, relative):
        return os.path.join(self.base, *relative.split("/"))

    def environment(self):
        environment = dict(os.environ)
        environment["CLEANKIT_FIXTURE"] = self.base
        environment.pop("CLEANKIT_NO_SUCH_VAR", None)
        return environment

    def existing(self):
        return {relative for relative, _size, _age, _owner in FILES if os.path.isfile(self.path(relative))}

    def missing_directories(self):
        wanted = {os.path.dirname(relative) for relative, _size, _age, _owner in FILES}
        wanted |= {link for link, _target in JUNCTIONS}
        return sorted(relative for relative in wanted if not os.path.isdir(self.path(relative)))

    def close(self, keep):
        if self.locked_handle not in (None, INVALID_HANDLE_VALUE):
            KERNEL32.CloseHandle(self.locked_handle)
            self.locked_handle = None
        if keep:
            print(f"fixture kept at {self.base}")
            return
        for link, _target in JUNCTIONS:
            if os.path.lexists(self.path(link)):
                os.rmdir(self.path(link))  # removes the junction itself, never its target
        if "cleankit-" in os.path.basename(self.base):
            shutil.rmtree(self.base, ignore_errors=True)


class Results:
    def __init__(self):
        self.failures = 0
        self.passes = 0

    def check(self, name, condition, detail=""):
        if condition:
            self.passes += 1
            print(f"PASS {name}")
        else:
            self.failures += 1
            print(f"FAIL {name}{': ' + detail if detail else ''}")


def run_tool(tool, arguments, environment):
    completed = subprocess.run(f"{tool} {arguments}", shell=True, capture_output=True, env=environment, timeout=900)
    return completed.returncode, completed.stdout.decode("utf-8", errors="replace"), completed.stderr.decode("utf-8", errors="replace")


def parse_json(text):
    try:
        return json.loads(text)
    except ValueError:
        return None


def normalized(path):
    return os.path.normcase(os.path.realpath(path))


def group_load(tool, results, keep):
    fixture = Fixture()
    try:
        for name, document in INVALID_DOCUMENTS.items():
            path = os.path.join(fixture.base, f"invalid-{name}.json")
            with open(path, "w", encoding="utf-8") as handle:
                handle.write(document if isinstance(document, str) else json.dumps(document))
            code, _out, _err = run_tool(tool, f'scan --rules "{path}" --json', fixture.environment())
            results.check(f"load/{name} is rejected", code != 0, f"exit code {code}")
    finally:
        fixture.close(keep)


def group_scan(tool, results, keep):
    fixture = Fixture()
    try:
        code, out, err = run_tool(tool, f'scan --rules "{FIXTURE_RULES}" --json --list', fixture.environment())
        report = parse_json(out)
        results.check("scan/exit code 0 and JSON on stdout", code == 0 and isinstance(report, dict), f"exit {code}; stderr: {err[:300]}")
        if not isinstance(report, dict):
            return
        entries = report.get("rules", [])
        results.check("scan/rules reported in rule-file order", [entry.get("id") for entry in entries] == RULE_ORDER,
                      str([entry.get("id") for entry in entries]))
        by_id = {entry.get("id"): entry for entry in entries}
        total_files = total_bytes = 0
        for rule_id in RULE_ORDER:
            entry = by_id.get(rule_id, {})
            owned = [(relative, size) for relative, size, _age, owner in FILES if owner == rule_id]
            expected_paths = sorted(normalized(fixture.path(relative)) for relative, _size in owned)
            actual_paths = sorted(normalized(path) for path in entry.get("paths", []))
            results.check(f"scan/{rule_id} paths", actual_paths == expected_paths,
                          f"expected {len(expected_paths)} got {len(actual_paths)}: "
                          f"extra {sorted(set(actual_paths) - set(expected_paths))[:5]} missing {sorted(set(expected_paths) - set(actual_paths))[:5]}")
            expected_bytes = sum(size for _relative, size in owned)
            results.check(f"scan/{rule_id} files and bytes", entry.get("files") == len(owned) and entry.get("bytes") == expected_bytes,
                          f"expected {len(owned)} files {expected_bytes} bytes, got {entry.get('files')} / {entry.get('bytes')}")
            expected_skipped = EXPECTED_SKIPPED.get(rule_id, {"too_new": 0})
            actual_skipped = entry.get("skipped", {})
            results.check(f"scan/{rule_id} skipped counters",
                          all(actual_skipped.get(key) == value for key, value in expected_skipped.items()), str(actual_skipped))
            codes = sorted(warning.get("code") for warning in entry.get("warnings", []))
            results.check(f"scan/{rule_id} warnings", codes == EXPECTED_WARNINGS.get(rule_id, []), str(codes))
            total_files += len(owned)
            total_bytes += expected_bytes
        results.check("scan/totals", report.get("total_files") == total_files and report.get("total_bytes") == total_bytes,
                      f"expected {total_files} / {total_bytes}, got {report.get('total_files')} / {report.get('total_bytes')}")
        results.check("scan/nothing deleted", fixture.existing() == {relative for relative, _s, _a, _o in FILES})
    finally:
        fixture.close(keep)


def group_clean(tool, results, keep):
    fixture = Fixture()
    try:
        everything = {relative for relative, _size, _age, _owner in FILES}
        selection = ",".join(CLEAN_SELECTION)
        code, _out, _err = run_tool(tool, f'clean --rules "{FIXTURE_RULES}" --select {selection} --json', fixture.environment())
        results.check("clean/refuses to run without --yes", code != 0 and fixture.existing() == everything, f"exit {code}")
        code, _out, _err = run_tool(tool, f'clean --rules "{FIXTURE_RULES}" --select no-such-rule --yes --json', fixture.environment())
        results.check("clean/rejects an unknown rule id", code != 0 and fixture.existing() == everything, f"exit {code}")
        code, out, err = run_tool(tool, f'clean --rules "{FIXTURE_RULES}" --select {selection} --yes --json', fixture.environment())
        report = parse_json(out)
        results.check("clean/exit code 0 and JSON on stdout", code == 0 and isinstance(report, dict), f"exit {code}; stderr: {err[:300]}")
        doomed = {relative for relative, _size, _age, owner in FILES if owner in CLEAN_SELECTION and relative not in UNDELETABLE}
        survivors = fixture.existing()
        results.check("clean/selected files are gone", not (survivors & doomed), str(sorted(survivors & doomed)))
        results.check("clean/every other file survives", survivors >= everything - doomed, f"lost {sorted((everything - doomed) - survivors)}")
        results.check("clean/directories and junctions are left in place", not fixture.missing_directories(), str(fixture.missing_directories()))
        if isinstance(report, dict):
            by_id = {entry.get("id"): entry for entry in report.get("rules", [])}
            results.check("clean/report lists exactly the selected rules", sorted(by_id) == sorted(CLEAN_SELECTION), str(sorted(by_id)))
            deleted_files = deleted_bytes = 0
            for rule_id in CLEAN_SELECTION:
                listed = [(relative, size) for relative, size, _age, owner in FILES if owner == rule_id]
                owned = [size for relative, size in listed if relative not in UNDELETABLE]
                expected_failed = sorted((normalized(fixture.path(relative)), UNDELETABLE[relative])
                                         for relative, _size in listed if relative in UNDELETABLE)
                entry = by_id.get(rule_id, {})
                failed = entry.get("failed")
                actual_failed = sorted((normalized(item.get("path", "")), item.get("error")) for item in failed) if isinstance(failed, list) else None
                results.check(f"clean/{rule_id} report",
                              entry.get("deleted_files") == len(owned) and entry.get("deleted_bytes") == sum(owned) and actual_failed == expected_failed,
                              f"got {entry.get('deleted_files')} files {entry.get('deleted_bytes')} bytes failed {failed}; expected failed {expected_failed}")
                deleted_files += len(owned)
                deleted_bytes += sum(owned)
            results.check("clean/totals", report.get("total_deleted_files") == deleted_files and report.get("total_deleted_bytes") == deleted_bytes,
                          f"got {report.get('total_deleted_files')} / {report.get('total_deleted_bytes')}")
    finally:
        fixture.close(keep)


def main():
    parser = argparse.ArgumentParser(description="Conformance harness for SPEC.md")
    parser.add_argument("--tool", required=True, help="command prefix of the cleaner under test")
    parser.add_argument("--only", choices=["load", "scan", "clean"])
    parser.add_argument("--keep", action="store_true", help="keep the fixture directory for inspection")
    arguments = parser.parse_args()
    results = Results()
    groups = {"load": group_load, "scan": group_scan, "clean": group_clean}
    for name, function in groups.items():
        if arguments.only in (None, name):
            function(arguments.tool, results, arguments.keep)
    print(f"{results.passes} passed, {results.failures} failed")
    return 1 if results.failures else 0


if __name__ == "__main__":
    sys.exit(main())
