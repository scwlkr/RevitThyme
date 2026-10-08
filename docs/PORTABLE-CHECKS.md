# Portable local validation

On macOS or Linux, use Python 3 and installed Cargo/rustc with rustfmt and clippy. The Unix CLI chooses `python3`; Windows keeps `python`. No third-party Python dependencies, host installation, Revit project or external TimberFold checkout is required.

```sh
./project check-portable --base HEAD^ --require-clean
./project check-release --package-only --require-clean
```

Portable checks run offline Rust formatting, lint, unit tests and build, actual CLI help/failure behavior, standard-library Python test discovery (`tests/test*.py`), CPython syntax parsing, and reproducible package/hash/private-data/icon checks. Tests added for future features are discovered automatically. The report records exact SHA, base, platform, commands, outputs, cleanliness before/after and tool versions. Evidence goes to ignored `artifacts/local-ci/portable.json` and its package report.

The declared Rust pin stays in `rust-toolchain.toml`. An available compiler can validate portable behavior even when its version differs, but the report records `matches_pin: false` and leaves the pinned-toolchain qualification `not_run`. Tools are never installed automatically. CPython parsing does not establish IronPython 2.7 syntax/runtime compatibility.

To validate a separate feature checkout with this checker, use an explicit root and retain both revisions:

```sh
./project check-portable --root /path/to/feature-checkout --base master --require-clean
```

The checker executes the target checkout's Rust CLI and Python tests and builds the target's package with the target's release builder. It records `checker_sha` and `sha` separately and requires both checkouts clean when requested. The external checker supplies package-only verification; the target need not contain a `check-portable` route. Outputs stay under the target's ignored artifacts unless `--report` selects another path.

`passed: true` applies only to `scope: portable_offline`. Windows installer lifecycle, Windows foundation/CLI contracts, live Revit, IronPython runtime, second-computer behavior and physical fabrication remain explicit `not_run` entries. On Unix, full `check-release` fails with a report explaining its Windows requirement. On Windows, full `check-release` preserves the installer tests, and `check-public-ci` retains its Windows release gates while adding discovered Python and Rust tests. Full local `ci` remains the Windows/TimberFold path.

The existing GitHub workflow still runs automatically for pull requests and pushes to `master` on Windows. This change does not dispatch or edit hosted workflows. Portable local success does not replace required hosted results or Windows/Revit host qualification.
