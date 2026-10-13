# Wstack 0.2 setup refresh - 2026-10-13

Work: [WLK-240](https://linear.app/wlkr-labs/issue/WLK-240/refresh-revitthyme-for-wstack-02-project-setup). Baseline: `b7197235d85095e5032c255cc02b18c6ce5424eb`. Local branch: `codex/WLK-240-wstack-setup`. Setup does not operate Revit, replace installed artifacts, publish or land the existing MCP branch.

The installed wstack 0.2 worker failed before edits at `signal.SIGALRM`/`signal.alarm`, unavailable on Windows. It also selects Unix `./project` before the existing Windows launcher. The retained log is `artifacts/wstack-setup/upstream-windows-first.log`; the pre-refresh instructions are preserved in `AGENTS.before.md` there. Global skills were not modified.

Used the skill's small local fallback: add `w.md`, feature/glossary vocabulary and the short AGENTS.md pointer. Preserve meaningful project standards, owner commands, local-only publication rules, standing RevitThyme load/install authorization and working-model safeguards. Current accepted Rust/.NET/UI architecture replaces historical setup exceptions; the older foundation record remains historical.

`project check-wstack` checks project notes/version/pointers and actual Windows CLI help/doctor without changing documents. A before-refresh run failed for the three missing documents and retained `before.json`. The after-refresh report and repeat-setup readback remain under `artifacts/wstack-setup`. Malformed/missing pointers must return exit 1; healthy setup returns 0.

Full CI replaces the retired `.agents/skills/wstack-setup` check/apply dependency with this owned check. Existing build, packaged UI, native transport, CLI, scope-routing and cache gates remain. Exact clean-SHA aggregate results are retained in `artifacts/wstack-setup/ci.json`; inspect the report's SHA, cleanliness and aggregate result before claiming success.

Current native host/client revisions and qualification stay in [native-mcp.md](native-mcp.md). No new native or physical qualification is claimed by this setup refresh. Unrelated logo and unpublished branches/evidence remain preserved.
