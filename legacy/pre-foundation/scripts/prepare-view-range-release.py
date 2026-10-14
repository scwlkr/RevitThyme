"""Prepare an unpublished Visual View Range preview from exact checked source."""

import argparse
import hashlib
import json
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = "0.3.0-preview.1"


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ci-report", type=Path, required=True)
    args = parser.parse_args()
    sha = git("rev-parse", "HEAD")
    if git("status", "--porcelain"):
        raise ValueError("Release preparation requires a clean source checkout")
    ci = json.loads(args.ci_report.read_text(encoding="utf-8-sig"))
    if not (ci["sha"] == sha and ci["passed"] and ci["worktree_clean"]
            and ci["scope"] == "full" and all(c["passed"] for c in ci["checks"])):
        raise ValueError("A passing full exact-clean CI report at HEAD is required")
    bundle = ROOT / "desktop/out/RevitThyme-win32-x64"
    manifest = json.loads((bundle / "resources/package-manifest.json").read_text())
    if manifest["source_sha"] != sha:
        raise ValueError("Packaged source revision differs from HEAD")
    # Reuse the owning packaged boundary check before creating any deliverable.
    subprocess.run(["node", "desktop/scripts/check-package.mjs"], cwd=ROOT, check=True)
    name = f"RevitThyme-VisualViewRange-{VERSION}-{sha[:12]}-win-x64"
    output = ROOT / "artifacts/releases" / name
    output.mkdir(parents=True, exist_ok=False)
    files = {}
    for source in sorted(bundle.rglob("*")):
        if source.is_symlink():
            raise ValueError("Bundle links are not allowed")
        if source.is_file():
            relative = source.relative_to(bundle).as_posix()
            if any(part.lower().endswith((".rvt", ".rfa", ".env"))
                   or "revitapi" in part.lower() or "qualification" in part.lower()
                   or "timberfold" in part.lower() for part in Path(relative).parts):
                raise ValueError(f"Excluded package content: {relative}")
            files[f"RevitThyme-win32-x64/{relative}"] = source.read_bytes()
    for relative in ("LICENSE", "THIRD-PARTY-NOTICES.md",
                     "docs/VISUAL-VIEW-RANGE.md", "docs/VIEW-RANGE-RELEASE-NOTES.md"):
        files[Path(relative).name] = (ROOT / relative).read_bytes()
    metadata = {
        "version": VERSION, "source_sha": sha, "published": False,
        "components": manifest["components"], "protocol": manifest["protocol"],
        "ci_base_sha": ci["base_sha"], "full_exact_clean_ci": True,
        "qualification": "Bounded current-host results: verification/rust-view-range-m3-remaining.md",
        "qualified_revit_builds": manifest["qualified_revit_builds"],
        "distribution_gates": ["second-computer trial", "dependency notice clearance",
                               "signing and installer recovery"],
        "files": {key: digest(value) for key, value in files.items()},
    }
    files["RELEASE.json"] = (json.dumps(metadata, indent=2) + "\n").encode()
    archive = output / (name + ".zip")
    with zipfile.ZipFile(archive, "x", zipfile.ZIP_DEFLATED) as target:
        for relative, data in files.items():
            target.writestr(relative, data)
    with zipfile.ZipFile(archive) as target:
        if set(target.namelist()) != set(files) or target.testzip() is not None:
            raise ValueError("Archive inventory/CRC check failed")
        for relative, data in files.items():
            if target.read(relative) != data:
                raise ValueError(f"Archive round-trip failed: {relative}")
    source_archive = output / (name + "-source.zip")
    git("archive", "--format=zip", "--output=" + str(source_archive), sha)
    checksums = "".join(f"{digest(p.read_bytes())}  {p.name}\n"
                        for p in (archive, source_archive))
    (output / "SHA256SUMS.txt").write_text(checksums, encoding="utf-8")
    (output / "release.json").write_text(json.dumps(metadata, indent=2) + "\n")
    print(json.dumps({"source_sha": sha, "output": str(output), "archive": str(archive),
                      "files_round_trip_verified": len(files), "published": False}))


if __name__ == "__main__":
    main()
