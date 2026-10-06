#!/usr/bin/env python3
"""Fail the build if a NuGet package (direct OR transitive) has a known High or Critical vulnerability.

Policy
  * High and Critical vulnerabilities FAIL the check.
  * Moderate and Low vulnerabilities are reported as warnings and do NOT fail it.
  * A package merely being out of date never fails anything.
  * If the vulnerability data cannot be read (the NuGet source is unreachable, or the report has errors), the
    check FAILS rather than passing without having checked anything.

It uses the .NET SDK's own `dotnet list package --vulnerable --include-transitive`, which reads the
GitHub Advisory Database through NuGet's vulnerability feed. No third-party scanner is involved.

Usage
  check-vulnerabilities.py [SOLUTION]            run the SDK command (default: VehicleRentalManagementSystem.slnx)
  check-vulnerabilities.py --input REPORT.json   check a saved report (used to test this script)
"""
import json
import os
import subprocess
import sys

FAIL_SEVERITIES = {"high", "critical"}


def load_report(args: list[str]) -> dict:
    if len(args) >= 2 and args[0] == "--input":
        with open(args[1], encoding="utf-8") as handle:
            return json.load(handle)

    solution = args[0] if args else "VehicleRentalManagementSystem.slnx"
    command = [
        "dotnet", "list", solution, "package",
        "--vulnerable", "--include-transitive",
        "--format", "json", "--output-version", "1",
    ]
    if os.environ.get("CI_NO_RESTORE", "1") == "1":
        command.append("--no-restore")

    completed = subprocess.run(command, capture_output=True, text=True)
    output = completed.stdout

    start = output.find("{")
    if completed.returncode != 0 or start < 0:
        print(completed.stdout)
        print(completed.stderr, file=sys.stderr)
        raise SystemExit("ERROR: 'dotnet list package' failed, so vulnerabilities could not be checked.")

    return json.loads(output[start:])


def main() -> int:
    report = load_report(sys.argv[1:])
    in_actions = bool(os.environ.get("GITHUB_STEP_SUMMARY"))

    problems = [p for p in report.get("problems", []) if p.get("level") == "error"]
    if problems:
        for problem in problems:
            print(f"ERROR: {problem.get('text')}")
        print("ERROR: the vulnerability data could not be read, so nothing can be said about the packages.")
        return 2

    findings = []  # (severity, project, package, version, advisory)
    for project in report.get("projects", []):
        project_name = os.path.basename(project.get("path", "?"))
        for framework in project.get("frameworks", []):
            for kind, key in (("direct", "topLevelPackages"), ("transitive", "transitivePackages")):
                for package in framework.get(key, []):
                    for vulnerability in package.get("vulnerabilities", []):
                        findings.append((
                            vulnerability.get("severity", "Unknown"),
                            project_name,
                            f"{package.get('id')} ({kind})",
                            package.get("resolvedVersion") or package.get("requestedVersion", "?"),
                            vulnerability.get("advisoryurl", ""),
                        ))

    projects_checked = len(report.get("projects", []))
    blocking = [f for f in findings if f[0].lower() in FAIL_SEVERITIES]
    warnings = [f for f in findings if f[0].lower() not in FAIL_SEVERITIES]

    print(f"Checked {projects_checked} project(s), direct and transitive packages: "
          f"{len(blocking)} blocking (High/Critical), {len(warnings)} non-blocking (Moderate/Low).")

    for severity, project, package, version, advisory in warnings:
        message = f"{severity} vulnerability in {package} {version} ({project}): {advisory}"
        print(f"::warning::{message}" if in_actions else f"WARNING: {message}")

    for severity, project, package, version, advisory in blocking:
        message = f"{severity} vulnerability in {package} {version} ({project}): {advisory}"
        print(f"::error::{message}" if in_actions else f"ERROR: {message}")

    summary_file = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_file:
        with open(summary_file, "a", encoding="utf-8") as handle:
            handle.write(
                f"### NuGet vulnerabilities\n\n{projects_checked} projects checked (direct and transitive): "
                f"**{len(blocking)} blocking**, {len(warnings)} non-blocking.\n\n")
            for severity, project, package, version, advisory in blocking + warnings:
                handle.write(f"- {severity}: {package} {version} in {project} {advisory}\n")

    if blocking:
        print("\nFAILED: update or replace the vulnerable package(s) above.")
        return 1

    print("\nNo High or Critical vulnerabilities.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
