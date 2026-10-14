# Visual View Range 0.3.0-preview.1 — unpublished candidate

Visual View Range adds native plan capture, an interactive section preview and reviewed four-plane Apply with independent Revit readback and native Undo. Its Rust/desktop 0.1.1, native adapter 0.2.4 and protocol 2 bundle preserves the existing pyRevit suite and external TimberFold installation.

Bounded actual qualification on Revit 27.2.0.39 includes floor/ceiling/engineering ranges, main up/down direction, independent underlays, stale review rejection, cancellation, outcome recovery, geometry coordinates and responsiveness. A normal cold-start trial with pyRevit disabled verifies native ribbon launch, capture, Apply and Undo. Models were kept unsaved and original settings and disk contents restored.

Native edit readiness, open-document identity and idle-pipe handling were repaired. Unresolved Level Above/Below placeholders reject with named-level guidance; this restriction is accepted. Current, resolved named levels and permitted Unlimited remain supported. Capture limits and approximate geometry remain visible. See VISUAL-VIEW-RANGE.md for usage, restrictions and recovery, and the repository's numbered qualification assessment for exact historical test revisions.

This is a prepared local candidate. It is not published, signed, installed automatically or qualified on another computer. Multiple-instance and additional cross-user/network trials are deferred. It does not complete the broader suite migration, retire Python tools or claim aggregate R1–R12 qualification. Before distribution, complete the second-computer trial, bundled dependency notice clearance, signing and installer recovery.

RELEASE.json and the package manifest pin the exact source revision and component hashes. SHA256SUMS.txt covers the binary ZIP and corresponding tracked-source ZIP. Source/package CI and actual-Revit evidence are separate; the assessment must be consulted for the tested runtime revisions.
