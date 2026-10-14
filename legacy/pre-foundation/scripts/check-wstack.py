"""Verify project-owned Wstack notes and the real CLI without changing setup."""
import argparse
import json
import os
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    checks, originals = [], {}

    def require(name, passed, detail):
        checks.append({"name": name, "passed": bool(passed), "detail": detail})

    for name in ("w.md", "AGENTS.md", "FEATURES.md", "GLOSSARY.md"):
        file = root / name
        if file.is_file() and not file.is_symlink():
            originals[name] = file.read_bytes()
            text = originals[name].decode("utf-8-sig")
            require(name + " contains project content", any(line.strip() and not line.lstrip().startswith(("#", "<!--"))
                    for line in text.splitlines()), name)
        else:
            require(name + " exists", False, str(file))
    if len(originals) == 4:
        home, agents = (originals[name].decode("utf-8-sig") for name in ("w.md", "AGENTS.md"))
        blocks = {}
        for name, text in (("w.md", home), ("AGENTS.md", agents)):
            begin, end = "<!-- wstack:begin setup -->", "<!-- wstack:end setup -->"
            valid = text.count(begin) == text.count(end) == 1 and text.index(begin) < text.index(end)
            require(name + " has one ordered setup block", valid, name)
            blocks[name] = text.split(begin)[1].split(end)[0] if valid else ""
        require("Current Wstack setup version", "Wstack: 0.2.0" in blocks["w.md"].splitlines(), "0.2.0")
        require("Feature and glossary pointers", "[FEATURES.md](FEATURES.md)" in blocks["w.md"]
                and "[GLOSSARY.md](GLOSSARY.md)" in blocks["w.md"], "Root vocabulary")
        require("Agent notes pointer", "Wstack project notes → [w.md](w.md)." in blocks["AGENTS.md"], "w.md")
        launcher = ".\\project.cmd" if os.name == "nt" else "./project"
        require("Platform CLI pointer", f"CLI: `{launcher} --help`" in blocks["w.md"].splitlines(), launcher)

    if all(check["passed"] for check in checks):
        cli = root / ("project.cmd" if os.name == "nt" else "project")
        for command in ("--help", "doctor"):
            try:
                result = subprocess.run([str(cli), command], cwd=root, capture_output=True, text=True,
                                        encoding="utf-8", errors="replace", timeout=45)
                valid = result.returncode == 0
                if command == "--help":
                    valid = valid and "check-wstack" in result.stdout and "doctor" in result.stdout
                require("Real CLI " + command, valid, (result.stdout + result.stderr).strip())
            except (OSError, subprocess.TimeoutExpired) as error:
                require("Real CLI " + command, False, str(error))
    changed = [name for name, before in originals.items() if not (root / name).exists()
               or (root / name).read_bytes() != before]
    require("Verification preserves project notes", not changed, changed)
    report = {"passed": all(check["passed"] for check in checks), "scope": "project_setup_offline",
              "root": str(root), "changed": changed, "checks": checks}
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
