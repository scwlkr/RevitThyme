"""Reversibly select native Revit MCP; preserve all unrelated TOML settings."""
import copy
import hashlib
import json
from pathlib import Path
import re
import sys
import tomllib
import uuid


def revise(text, action, folder):
    before = tomllib.loads(text)
    expected = copy.deepcopy(before)
    names = ["revit", "jev-revit"] if action == "disable" else ["revitthyme_test"]
    for name in names:
        server = expected.get("mcp_servers", {}).get(name)
        if server is None:
            raise ValueError("Expected configured server: " + name)
        values = {"enabled": False} if action == "disable" else {
            "command": str(folder / "bundle/revitthyme-test.exe"),
            "args": ["--lab-mcp", str(folder / "binding.json")]}
        if action == "bind":
            values["enabled"] = True
        pattern = r"(?ms)^\[mcp_servers\." + re.escape(name) + r"\]\r?\n.*?(?=^\[|\Z)"
        matches = list(re.finditer(pattern, text))
        if len(matches) != 1:
            raise ValueError("Require one exact MCP section: " + name)
        section = matches[0].group()
        for key, value in values.items():
            line = key + " = " + json.dumps(value)
            field = r"(?m)^" + key + r"\s*=.*$"
            if re.search(field, section):
                section = re.sub(field, lambda _: line, section)
            else:
                section = section.rstrip() + "\n" + line + "\n\n"
            server[key] = value
        text = text[:matches[0].start()] + section + text[matches[0].end():]
    if tomllib.loads(text) != expected:
        raise ValueError("Refusing any unrelated TOML change")
    return text


def rotate(text, previous, folder):
    server = tomllib.loads(text).get("mcp_servers", {}).get("revitthyme_test", {})
    if server.get("command") != str(previous / "bundle/revitthyme-test.exe") or server.get("args") != ["--lab-mcp", str(previous / "binding.json")]:
        raise ValueError("Existing native MCP target does not match the exact previous run")
    return revise(text, "rotate", folder)


def prepare_rotation(previous, folder):
    target = Path.home() / ".codex/config.toml"
    original = target.read_bytes()
    revised = rotate(original.decode("utf-8-sig"), previous, folder).encode("utf-8")
    candidate = folder / "config.toml.prepared-rotation"
    if candidate.exists() and candidate.read_bytes() != revised:
        raise ValueError("Prepared rotation changed; preserve and review the existing candidate")
    candidate.write_bytes(revised)
    print(json.dumps({"candidate": str(candidate), "source_sha256": hashlib.sha256(original).hexdigest()}))


def check():
    fixture = '[mcp_servers.revit]\ncommand="old"\n[mcp_servers.jev-revit]\ncommand="uv"\n[mcp_servers.revitthyme_test]\ncommand="native"\nargs=[]\n[mcp_servers.other.env]\nPRIVATE="preserve exactly"\n'
    disabled = revise(fixture, "disable", Path("unused"))
    assert revise(disabled, "disable", Path("unused")) == disabled
    bound = revise(disabled, "bind", Path("C:/native test run"))
    result = tomllib.loads(bound)
    assert not result["mcp_servers"]["revit"]["enabled"]
    assert not result["mcp_servers"]["jev-revit"]["enabled"]
    assert result["mcp_servers"]["revitthyme_test"]["enabled"]
    assert '[mcp_servers.other.env]\nPRIVATE="preserve exactly"\n' in bound
    previous = Path("C:/another checkout/artifacts/revit-lab/old")
    configured = revise(disabled, "bind", previous)
    rotated = rotate(configured, previous, Path("C:/new checkout/artifacts/revit-lab/new"))
    result = tomllib.loads(rotated)
    assert result["mcp_servers"]["revitthyme_test"]["enabled"]
    assert result["mcp_servers"]["other"]["env"]["PRIVATE"] == "preserve exactly"
    assert result["mcp_servers"]["revit"]["enabled"] is False
    disabled_native = configured.replace("enabled = true", "enabled = false")
    assert not tomllib.loads(rotate(disabled_native, previous, Path("C:/new")))["mcp_servers"]["revitthyme_test"]["enabled"]
    try:
        rotate(configured, Path("C:/wrong previous"), Path("C:/new"))
    except ValueError:
        pass
    else:
        raise AssertionError("Rotation must reject an unrelated previous target")
    try:
        revise('[mcp_servers.other]\ncommand="keep"\n', "disable", Path("unused"))
    except ValueError:
        pass
    else:
        raise AssertionError("Missing target sections must be refused")
    print("MCP cutover preserves unrelated settings, disables legacy routes, pins native paths and is repeatable.")


if __name__ == "__main__":
    if sys.argv[1:] == ["check"]:
        check()
    elif len(sys.argv) == 4 and sys.argv[1] == "prepare-rotation":
        prepare_rotation(Path(sys.argv[2]).resolve(), Path(sys.argv[3]).resolve())
    else:
        previous = None
        if len(sys.argv) == 4 and sys.argv[1] == "upgrade":
            previous = Path(sys.argv[2]).resolve()
            action, folder_text = "bind", sys.argv[3]
        else:
            action, folder_text = sys.argv[1:]
        if action not in ("disable", "bind"):
            raise ValueError("Expected disable or bind")
        folder = Path(folder_text).resolve()
        target = Path.home() / ".codex/config.toml"
        original = target.read_bytes()
        revised = (rotate(original.decode("utf-8-sig"), previous, folder) if previous else
                   revise(original.decode("utf-8-sig"), action, folder)).encode("utf-8")
        backup = folder / ("config.toml.before-" + action)
        if backup.exists():
            raise ValueError("Backup exists; inspect prior operation instead of overwriting")
        backup.write_bytes(original)
        if target.read_bytes() != original:
            raise ValueError("Configuration changed during preparation")
        temporary = target.with_name("config.toml.revitthyme-" + uuid.uuid4().hex + ".tmp")
        temporary.write_bytes(revised)
        if target.read_bytes() != original:
            temporary.unlink()
            raise ValueError("Configuration changed before atomic replacement")
        temporary.replace(target)
        if target.read_bytes() != revised:
            raise ValueError("Configuration readback mismatch")
        print(json.dumps({"action": action, "backup": str(backup), "sha256": hashlib.sha256(revised).hexdigest()}))
