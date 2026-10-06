#!/usr/bin/env python3
"""Fail unless every test in every TRX result file actually ran and passed.

`dotnet test` exits 0 when tests are *skipped*, so a CI run that quietly lost its database (for example a
mis-typed connection variable) would otherwise look green with 100+ tests skipped. This reads the TRX
files (machine-readable XML), not the console text, and fails on:

  * no TRX files, or fewer TRX files than there are test projects (a project did not report),
  * any test that did not pass: failed, skipped / not executed, timed out, aborted, ...
  * a test run that did not complete, or one that contains no tests.

It deliberately does NOT hard-code how many tests there should be, so adding a test never breaks it.

Usage: check-test-results.py [RESULTS_DIR] [TESTS_DIR]     (defaults: TestResults, tests)
"""
import glob
import os
import sys
import xml.etree.ElementTree as ET
from collections import Counter

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def main() -> int:
    results_dir = sys.argv[1] if len(sys.argv) > 1 else "TestResults"
    tests_dir = sys.argv[2] if len(sys.argv) > 2 else "tests"

    trx_files = sorted(glob.glob(os.path.join(results_dir, "**", "*.trx"), recursive=True))
    test_projects = sorted(glob.glob(os.path.join(tests_dir, "*", "*.csproj")))

    problems: list[str] = []
    rows: list[tuple[str, Counter]] = []
    totals: Counter = Counter()

    if not trx_files:
        problems.append(f"No .trx files found under '{results_dir}'. The tests did not run or did not report.")

    if len(trx_files) < len(test_projects):
        problems.append(
            f"Found {len(trx_files)} result file(s) but there are {len(test_projects)} test projects: "
            "at least one project did not report results."
        )

    for path in trx_files:
        root = ET.parse(path).getroot()
        outcomes: Counter = Counter(
            result.get("outcome", "Unknown") for result in root.iterfind(".//t:Results/t:UnitTestResult", NS)
        )

        # Name the test assembly from its first test, falling back to the file name.
        method = root.find(".//t:TestDefinitions/t:UnitTest/t:TestMethod", NS)
        name = os.path.basename(path)
        if method is not None and method.get("codeBase"):
            name = os.path.basename(method.get("codeBase"))

        summary = root.find(".//t:ResultSummary", NS)
        if summary is None or summary.get("outcome") not in ("Completed", "Failed"):
            problems.append(f"{name}: the test run did not complete (summary outcome: "
                            f"{summary.get('outcome') if summary is not None else 'missing'}).")

        total = sum(outcomes.values())
        if total == 0:
            problems.append(f"{name}: the result file contains no tests.")

        not_passed = {k: v for k, v in outcomes.items() if k != "Passed"}
        if not_passed:
            detail = ", ".join(f"{v} {k}" for k, v in sorted(not_passed.items()))
            problems.append(f"{name}: {detail} (every test must pass; skipped tests are not allowed in CI).")

        rows.append((name, outcomes))
        totals.update(outcomes)

    # Report
    lines = ["| Test assembly | Passed | Failed | Skipped / not run | Total |", "|---|---:|---:|---:|---:|"]
    for name, outcomes in rows:
        failed = outcomes.get("Failed", 0)
        other = sum(v for k, v in outcomes.items() if k not in ("Passed", "Failed"))
        lines.append(f"| {name} | {outcomes.get('Passed', 0)} | {failed} | {other} | {sum(outcomes.values())} |")

    failed_total = totals.get("Failed", 0)
    other_total = sum(v for k, v in totals.items() if k not in ("Passed", "Failed"))
    lines.append(f"| **All** | **{totals.get('Passed', 0)}** | **{failed_total}** | **{other_total}** | **{sum(totals.values())}** |")
    report = "\n".join(lines)
    print(report)

    summary_file = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_file:
        with open(summary_file, "a", encoding="utf-8") as handle:
            handle.write("### Test results\n\n" + report + "\n\n")
            if problems:
                handle.write("**Problems:**\n\n" + "\n".join(f"- {p}" for p in problems) + "\n")

    if problems:
        print()
        for problem in problems:
            print(f"::error::{problem}" if summary_file else f"ERROR: {problem}")
        return 1

    print("\nAll tests ran and passed; none were skipped.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
