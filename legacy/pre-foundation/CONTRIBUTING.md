# Contributing

Start with README.md, PROJECT.md and AGENTS.md. Changes should have a small observable outcome and preserve existing models and tools.

Use GitHub issues for public reports and contribution proposals. Maintainer implementation is tracked in WLKR LABS / RevitThyme on Linear; maintainers link public requests there. Contributors do not need Linear access.

Revit-facing code must work with IronPython 2.7. CPython scripts are separate. Reuse lib/revitthyme/operations.py for ribbon and external callers. Do not expose arbitrary code execution or save models implicitly.

Run project.cmd check-release. Full maintainer CI is project.cmd ci --base <base-sha> --require-clean --full and also needs external TimberFold and the wstack setup skill. Public CI checks portable packaging and the Rust CLI; live Revit behavior needs separate evidence.

For model changes, include a dedicated sample, deterministic target checks, rollback and read-back evidence. Never upload private models, credentials or settings. Redact private document names and paths from screenshots/logs.

Submit focused pull requests with the problem, resulting behavior, checks and limits. Contributions are provided under GPL-3.0-or-later.
