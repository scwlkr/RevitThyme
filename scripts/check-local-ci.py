"""Run local checks and retain aggregate results at the current Git revision."""

import argparse
import json
import os
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "tools/project-cli/Cargo.toml"


def scope_for(paths):
    for name in paths:
        path = ROOT / name
        if not name.endswith(".md") or name in ("AGENTS.md", "README.md", "SETUP-TODO.md"):
            return "full"
        if path.is_file() and "```" in path.read_text(encoding="utf-8"):
            return "full"
    return "docs"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="HEAD^")
    parser.add_argument("--require-clean", action="store_true")
    parser.add_argument("--full", action="store_true")
    parser.add_argument("--explain", nargs="+")
    parser.add_argument("--report", type=Path, default=ROOT / "artifacts/local-ci/latest.json")
    args = parser.parse_args()
    if args.explain:
        print(json.dumps({"paths": args.explain, "scope": scope_for(args.explain)}))
        return 0

    env = {**os.environ, "PYTHONUTF8": "1", "RUSTUP_AUTO_INSTALL": "0"}
    checks = []

    def run(name, command, expected=0, cwd=ROOT, timeout=60):
        started = time.monotonic()
        try:
            result = subprocess.run(
                command, cwd=cwd, env=env, capture_output=True, text=True,
                encoding="utf-8", errors="replace", timeout=timeout,
            )
        except (OSError, subprocess.TimeoutExpired) as error:
            result = subprocess.CompletedProcess(command, 1, "", str(error))
        checks.append({
            "name": name, "command": [str(value) for value in command],
            "exit": result.returncode, "passed": result.returncode == expected,
            "seconds": round(time.monotonic() - started, 3),
            "output": (result.stdout + result.stderr).strip(),
        })
        print(f"{'PASS' if checks[-1]['passed'] else 'FAIL'}: {name}", flush=True)
        return result

    def require(name, passed, detail):
        checks.append({"name": name, "passed": bool(passed), "detail": detail})
        print(f"{'PASS' if passed else 'FAIL'}: {name}", flush=True)

    sha = run("Resolve checked SHA", ["git", "rev-parse", "HEAD"]).stdout.strip()
    base = run("Resolve base SHA", ["git", "rev-parse", "--verify", args.base]).stdout.strip()
    status = run("Inspect worktree", ["git", "status", "--porcelain"]).stdout.strip()
    changed = run("Changed paths", ["git", "diff", "--name-only", args.base, "HEAD"]).stdout.splitlines()
    if status:
        changed += run("Uncommitted paths", ["git", "diff", "--name-only", "HEAD"]).stdout.splitlines()
        changed += [line[3:] for line in status.splitlines() if line.startswith("?? ")]
    scope = "full" if args.full else scope_for(changed)
    if args.require_clean:
        require("Exact clean revision", not status, status or "Clean tracked and untracked files")
    run("Commit whitespace", ["git", "diff", args.base, "HEAD", "--check"])
    run("Working diff whitespace", ["git", "diff", "--check"])
    run("Foundation diagnostic", [str(ROOT / "project.cmd"), "check-project", "-ReportPath",
                                  str(ROOT / "artifacts/local-ci/foundation.json")])

    if scope == "full":
        if args.require_clean:
            run("Public CI boundary", [str(ROOT / "project.cmd"), "check-public-ci"])
        run("Release package and installer", [str(ROOT / "project.cmd"), "check-release"])
        run("M1 Rust and typed feature contracts", [str(ROOT / "project.cmd"), "check-m1"], timeout=180)
        run("M2 native Windows build/pipe/orchestration", [str(ROOT / "project.cmd"), "check-m2"], timeout=180)
        run("M1 Windows application package", [str(ROOT / "project.cmd"), "package-m1"], timeout=300)
        run("M1 actual packaged interaction", [str(ROOT / "project.cmd"), "check-m1-ui"], timeout=180)
        run("M2 packaged native orchestration", [str(ROOT / "project.cmd"), "m2", "ui"], timeout=180)
        run("Rust formatting", ["cargo", "fmt", "--manifest-path", str(MANIFEST), "--check"])
        run("Rust lint and build", ["cargo", "clippy", "--offline", "--locked", "--all-targets",
                                    "--manifest-path", str(MANIFEST), "--", "-D", "warnings"])
        run("Real CLI contract", [sys.executable, "-X", "utf8", str(ROOT / "scripts/check-cli-contract.py")])
        skill = Path.home() / ".agents/skills/wstack-setup"
        run("Setup readiness", [sys.executable, "-X", "utf8", str(skill / "scripts/setup.py"), "check", str(ROOT)], cwd=skill)
        result = run("Setup idempotence", [sys.executable, "-X", "utf8", str(skill / "scripts/setup.py"), "apply", str(ROOT)], cwd=skill)
        applied = json.loads(result.stdout) if result.returncode == 0 else {}
        require("Repeat apply changes no files", applied.get("changed") == [], applied)

        for paths, expected in ((["docs/ROADMAP.md"], "docs"), (["README.md"], "full"),
                                (["tools/project-cli/src/main.rs"], "full"),
                                (["rust-toolchain.toml"], "full"), (["scripts/check-local-ci.py"], "full")):
            result = run("Scope routing: " + paths[0], [str(ROOT / "project.cmd"), "ci", "--explain", *paths])
            routed = json.loads(result.stdout) if result.returncode == 0 else {}
            require("Expected scope: " + paths[0], routed.get("scope") == expected, routed)

        folder = ROOT / "artifacts/local-ci/cache-probe"
        (folder / "src").mkdir(parents=True, exist_ok=True)
        probe_manifest = folder / "Cargo.toml"
        source = folder / "src/main.rs"
        source.write_text("fn main() {}\n", encoding="utf-8")
        probe_manifest.write_text('[package]\nname="ci-cache-probe"\nversion="0.0.1"\nedition="2021"\n[workspace]\n', encoding="utf-8")
        command = ["cargo", "check", "--offline", "--manifest-path", str(probe_manifest), "--message-format=json"]
        run("Populate Cargo cache", command)
        warm = run("Reuse Cargo cache", command)
        probe_manifest.write_text(probe_manifest.read_text(encoding="utf-8").replace('"0.0.1"', '"0.0.2"'), encoding="utf-8")
        invalidated = run("Invalidate changed manifest/lockfile", command)

        def freshness(result):
            rows = [json.loads(line) for line in result.stdout.splitlines() if line.startswith("{")]
            return [row["fresh"] for row in rows if row.get("reason") == "compiler-artifact"]

        require("Warm cache reuse", freshness(warm) == [True], freshness(warm))
        require("Manifest change rebuilds artifact", freshness(invalidated) == [False], freshness(invalidated))

    final_status = run("Final worktree status", ["git", "status", "--porcelain"]).stdout.strip()
    require("Checks preserve worktree status", final_status == status, final_status)
    report = {
        "schema_version": 1, "checked_at_utc": datetime.now(timezone.utc).isoformat(),
        "sha": sha, "base_sha": base, "scope": scope, "changed_paths": sorted(set(changed)),
        "worktree_clean": not status, "passed": all(row["passed"] for row in checks),
        "checks": checks, "toolchain": run_version("rustc", env),
        "live_revit_checked": False, "native_cad_checked": False, "physical_checked": False,
    }
    report_path = args.report.resolve()
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Local CI: {scope}; passed={report['passed']}; SHA={sha}; report={report_path}")
    return 0 if report["passed"] else 1


def run_version(program, env):
    return subprocess.run([program, "--version"], cwd=ROOT, env=env,
                          capture_output=True, text=True, timeout=10).stdout.strip()


if __name__ == "__main__":
    raise SystemExit(main())
