"""Check the installed CLI against the real offline diagnostic and file outputs."""

import argparse
import json
import subprocess
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def verify(report_path):
    folder = ROOT / "artifacts/local-ci/contract with spaces" / uuid.uuid4().hex
    folder.mkdir(parents=True, exist_ok=True)
    launcher = ROOT / "project.cmd"
    checks = []

    def run(name, args, expected=0):
        result = subprocess.run(
            [str(launcher), *args], cwd=ROOT, capture_output=True,
            text=True, encoding="utf-8", errors="replace", timeout=60,
        )
        checks.append({
            "name": name, "args": args, "expected_exit": expected,
            "exit": result.returncode, "passed": result.returncode == expected,
            "output": (result.stdout + result.stderr).strip(),
        })
        return result

    def require(name, passed):
        checks.append({"name": name, "passed": bool(passed)})

    for args in (["--help"], ["doctor"], ["check-project", "--help"]):
        run("CLI " + " ".join(args), args)
    run("Unknown command is rejected", ["unknown-command"], 2)
    run("Invalid doctor arguments are rejected", ["doctor", "unexpected"], 2)

    normal_report = folder / "normal report.json"
    run("Report path forwarding", ["check-project", "-ReportPath", str(normal_report)])
    normal = json.loads(normal_report.read_text(encoding="utf-8"))
    require("Report confirms offline scope", normal["passed"] and not any(
        normal[key] for key in ("live_revit_checked", "host_execution_checked", "timberfold_generation_run")
    ))
    sources = {row["tool_id"]: row["source_root"] for row in normal["sources"]}

    config = folder / "explicit config.json"
    config.write_text(json.dumps({"schema_version": 1, "tool_roots": sources}), encoding="utf-8")
    explicit_report = folder / "explicit report.json"
    run("Configuration and report paths with spaces", [
        "check-project", "-LocalConfigPath", str(config), "-ReportPath", str(explicit_report),
    ])
    explicit = json.loads(explicit_report.read_text(encoding="utf-8"))
    require("Explicit configuration was used", explicit["passed"] and explicit["configuration_mode"] == "explicit")
    require("External source resolution is preserved", sources == {
        row["tool_id"]: row["source_root"] for row in explicit["sources"]
    })

    config.write_text(json.dumps({
        "schema_version": 1, "tool_roots": {"timberfold": str(folder / "missing source")},
    }), encoding="utf-8")
    failed_report = folder / "failed report.json"
    run("Missing source exits 1 through the launcher", [
        "check-project", "-LocalConfigPath", str(config), "-ReportPath", str(failed_report),
    ], 1)
    failed = json.loads(failed_report.read_text(encoding="utf-8"))
    require("Failed report identifies the missing source", not failed["passed"] and any(
        row["name"] == "Source directory: timberfold" and not row["passed"] for row in failed["checks"]
    ))
    config.write_text("{ invalid JSON", encoding="utf-8")
    run("Malformed configuration exits 1", ["check-project", "-LocalConfigPath", str(config)], 1)

    report = {"scope": "offline_cli_boundary", "passed": all(row["passed"] for row in checks), "checks": checks}
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"CLI contract: {len(checks)} checks; passed={report['passed']}; report={report_path}")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, default=ROOT / "artifacts/local-ci/cli-contract.json")
    args = parser.parse_args()
    raise SystemExit(verify(args.report.resolve()))
