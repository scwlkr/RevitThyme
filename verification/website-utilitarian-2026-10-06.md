# Utilitarian website refinement - 2026-10-06

Issue: WLK-105. Worktree: download-docs-site/RevitThyme. Branch: codex/WLK-105-download-docs-site. Starting SHA: bf7d928634e183456276a34795dc2fddee5426d1.

## Published outcome

The refinement is public at https://revitthyme.wlkrlabs.com. The supplied https://docs.pyrevitlabs.io/ reference informed the plain typography, compact green navigation, guide sidebar and section anchors. Home, downloads and guides now share that layout and a visible global search control. The corrected editable house illustration is retained at a smaller size. No new dependency or framework was added.

Primary content words, measured from rendered HTML text nodes and excluding shared navigation and footer:

| Page | Before | After | Reduction |
| --- | ---: | ---: | ---: |
| Home | 356 | 69 | 81% |
| Download | 216 | 104 | 52% |
| Documentation index | 186 | 70 | 62% |

Site-authored quickstart and troubleshooting copy is shorter. Canonical technical guides, release facts, installation commands and implemented/planned distinctions remain available.

## Exact implementation and publication

- Clean implementation SHA: f9cf382a961630290e938ed349b57a64f30191bc.
- Sanitized Sites source SHA: 4c2f200e0b0437f8221891555afcf15f646ce74d.
- Existing public Sites project: appgprj_6ac4df23af808191aaa3703281ec0bff.
- Saved version 3: appgprj_6ac4df23af808191aaa3703281ec0bff~appgver_dc27c7e403848191b34b6af650b262ac.
- Successful deployment: appgdep_6ac4fb6ce15881918ace7033ecced121.
- Archive hash: sha256:fe66ccbd4dc1aa10024fde608f6c43cab1fbbdcf449a38ab5479c84ca48b47f8.

Anonymous HTTP requests returned 200 for the home page, quickstart and build metadata. Published build.json reports the implementation SHA above. A fresh screenshot confirms the new layout on the custom domain. Live Ctrl+K opened search, Routes returned seven matching guides, and one Escape closed the nonempty search dialog.

## Verification and reviews

Full exact-clean CI passed all 34 checks using `project.cmd ci --base bf7d928634e183456276a34795dc2fddee5426d1 --full --require-clean --report artifacts/site/utilitarian-final-ci.json`. Website checking passed 439 local links, assets and anchors across 14 HTML files. The earlier implementation checkpoint 7a7df25878962ebc8d63ab3cecb4c2fe88bcc859 also passed full CI; final CI covers both review fixes.

Browser verification covered desktop home, download, documentation index and quickstart; global search, no-match results, clipboard installation command, keyboard shortcut and mobile guide navigation. A native searchbox originally cleared text on the first Escape without closing the dialog; the explicit handler was verified to close it with one Escape and restore focus when launched from the search button.

At 390px, the document measured 375px without clipping. At 320px, home, download, documentation index, quickstart and tools measured 305px. Header links stayed on one row; the wider tools table scrolled within its container. Mobile Browse docs expanded, navigation reset the disclosure, and mobile search closed correctly. Browser error logs were empty and the temporary viewport override was reset.

Independent Standards and Spec reviews found two issues, both resolved at the final implementation SHA: focus outlines now exceed 6.6:1 contrast on tested surfaces, and planned fabrication preview is explicitly distinguished from the released v0.2.0 preview. Both reviewers confirmed no remaining material findings. Parent browser verification confirmed the clarified note and keyboard outline in the rendered site.

Ignored evidence under artifacts/site includes utilitarian-inspiration.jpg, utilitarian-home-draft.jpg, utilitarian-download.jpg, utilitarian-docs.jpg, utilitarian-guide.jpg, utilitarian-mobile-home.jpg, utilitarian-mobile-download.jpg, utilitarian-320.jpg, utilitarian-after-live.jpg, utilitarian-mobile-widths.json, utilitarian-word-counts.json, utilitarian-final-ci.json, utilitarian-live-proof.json and utilitarian-deployment.tar.gz. No credentials are retained in these files.

PROJECT.md records the accepted direction; CHANGELOG.md records the completed refinement. Existing audience and DNS remain configured. Revit host code, TimberFold, private source models and unrelated primary-checkout files were untouched. This verification-only checkpoint is separate from the deployed implementation. The worktree remains active for website work; repository default-branch landing remains outstanding under WLK-105.
