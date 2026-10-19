# Governance

RevitThyme is maintained by Shane Walker through WLKR LABS. The repository owner is responsible for scope, review, release approval and project administration. Contributors participate through public GitHub issues and pull requests; private maintainer tracking is not a prerequisite.

## Decisions and review

Proposals should describe a concrete workflow, expected behavior, model effects and how success can be verified. Maintainers decide scope after discussion, preferring shared operations, bounded changes and recoverable edits. Significant architecture decisions belong in repository documentation so future contributors can understand them.

Reviews consider correctness, compatibility, diagnostics, test evidence and documentation. Contributions do not guarantee merge or a release date. There is no promised support SLA; keep discussion constructive and reproducible.

## Releases and compatibility

Maintainers approve public releases. Passing CI verifies its recorded checks, not native compatibility. Native claims require pinned disposable Revit evidence. Public signing, additional host versions and another-machine qualification are separate acceptance items.

Before 1.0, contracts can evolve. Record user-visible changes in the changelog, coordinate add-in/CLI/MCP protocol changes, and document migration and recovery. A release must align source version, packaged assets, website links and public notes; see [website maintenance](docs/WEBSITE.md).

## Conduct and security

The maintainer enforces the [code of conduct](CODE_OF_CONDUCT.md), including removing abusive content, restricting participation or excluding repeat offenders. Reports are reviewed privately where practical. Follow [security reporting](SECURITY.md) for vulnerabilities and [support](SUPPORT.md) for ordinary help.

Contributions remain GPL-3.0-or-later. Project administration does not change contributor ownership of their work or the repository license.
