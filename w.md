# RevitThyme development workflow

Near the start of coding, run `2brain`; on Windows encoding errors use `$env:PYTHONUTF8='1'`. Current user instructions and [AGENTS.md](AGENTS.md) take precedence over historical guidance.

For architecture or scope changes, read [PROJECT.md](PROJECT.md) and the complete [2026-10-14 plan](2026-10-14_NEW_PLAN.md). The active architecture is C#/.NET 10/WPF; `legacy/pre-foundation` is preserved historical source. Implement only the authorized stage. [Features](FEATURES.md) and [terms](GLOSSARY.md) record current behavior.

Use `project.cmd help` for supported development routes. Implement shared operations in Core and Autodesk access in Addin, under ExternalEvent/API callbacks. Each edit requires explicit captured identity and native before/after readback. Files should normally stay under 300 lines; split by responsibility when useful.

Before declaring an implementation complete, run `project.cmd check` and record source revision, actual checks and limitations. Hosted portability checks use `project.cmd check -Portable`; actual Revit changes use the separate disposable process/fixture procedure in [Foundation acceptance](docs/FOUNDATION.md) or [MCP qualification](docs/MCP.md). Source/build/package checks cannot substitute for native proof.

Keep focused local commits on `codex/<issue>-<purpose>` branches and preserve unrelated edits. Track maintainer implementation in WLKR LABS / RevitThyme. Issue Done requires verified acceptance and default-branch landing; pushing, public publication or model save/sync/close require applicable user authorization. Never change a working model as a fallback for a test failure.

Installation/package changes follow [installation](docs/INSTALLATION.md): verify native physical paths, exact ownership, file hashes and recoverable registrations. Preserve private artifacts, existing installations, unpublished work and external TimberFold. Cleanup addresses only owned processes and exact reviewed targets.

For repeated trusted C# iteration, follow [development setup](docs/DEVELOPMENT.md). Build a signed package with the persistent local certificate, then install only during an agreed Revit-close window. `project.cmd dev-prepare` stages readiness without changing live registration; `dev-start` copies a known disposable nonworkshared fixture and sets opt-in only for its child process. Keep each run pinned to its exact process/session/document; no working-model fallback or registration swapping. Compiled host changes still require a restart. Pre-install package checks are separate from post-install native acceptance.

Update commands and daily-check behavior are in [UPDATES.md](docs/UPDATES.md). Reuse the packaged bootstrap with the checked release version; installation requires Revit closed. GitHub publication uses the existing Git Credential Manager entry for `https://github.com`; keep credential values out of files/output. Wstack's global setup worker lacks Windows SIGALRM support; preserve these project-owned notes and use the existing Windows project launcher.

For ribbon organization, controls, labels, tooltips or SVG icons, follow [ribbon standards](docs/RIBBON.md) and the reusable `revitthyme-ribbon` skill. Regenerate assets with `scripts/export-ribbon-icons.ps1`; `project.cmd check` checks freshness. `project.cmd ribbon-prepare/start/check/cleanup` uses a separate disposable Revit process.

Website: retain the original wordmark and compact, factual presentation. [Website maintenance](docs/WEBSITE.md) owns the word budget and content checks. Keep detail in the docs; local owner review still precedes deployment.
