# Website house correction - 2026-10-06

Issue: WLK-105. Worktree: download-docs-site/RevitThyme. Branch: codex/WLK-105-download-docs-site. Starting SHA: c8aa562a5b76849a3776cd8349f1ed786e7847a6.

## Visual outcome

The corrected cottage is published at https://revitthyme.wlkrlabs.com. A fresh live screenshot confirms the coherent pitched roof, recognizable gable, aligned openings, thin foundation and modest planter within the existing hero card. The public SVG response matches the editable source exactly.

The before screenshot confirmed the user's reported inverted roof fold. One independent agent reviewed the screenshot and SVG, identifying the roof/wall intersections, door below the wall baseline, oversized plant and distracting labels. A separate agent combined that critique with the parent critique and generated one landscape architectural concept using the built-in image generation tool. Its exact prompt and selected reference remain in ignored task artifacts. The parent redrew the existing SVG using the reference's gable, dominant sage roof slope, timber faces and restrained planter.

The SVG documents one shared axonometric projection. Wall planes and openings use matching affine transforms; the roof perimeter extends the actual roof slope. Standing seams run from ridge to eave, and the door terminates on the wall baseline. The far roof face is hidden by this view. SVG title/description and the landing-page image alt text describe the new illustration.

## Exact implementation and publication

- Clean implementation SHA: 4f47aa54e7eda06eadbc0ae29eacfb408adf0f58.
- Sanitized Sites source SHA: d3de38a9ef718bdb473a40ae56f13649ea1077cd.
- Existing Sites project: appgprj_6ac4df23af808191aaa3703281ec0bff; public audience retained.
- Saved version 2: appgprj_6ac4df23af808191aaa3703281ec0bff~appgver_4f4cc0e9db8081918cc34e71c27e637c.
- Successful deployment: appgdep_6ac4e8d1c82c81919e840a05b6fc5d22.
- Archive hash: sha256:a82336882f290fc4a52d241668a9c3cf95fcab55a2e3c68febef70d271aaccc2.

Published build.json reports the same implementation SHA. Anonymous PowerShell requests return HTTP 200 for the SVG and build metadata. An initial Python urllib request returned 403; the subsequent normal PowerShell HTTP client and browser verified the live result. No browser cookies or credentials were copied for verification.

## Checks and retained evidence

Full exact-clean CI passed all 34 checks using `project.cmd ci --base c8aa562a5b76849a3776cd8349f1ed786e7847a6 --full --require-clean --report artifacts/site/house-ci.json`. Website checking passed 396 local links/assets/anchors across 14 HTML files.

Desktop and 390px mobile browser screenshots show a complete readable house without clipping. Mobile document scroll width was 375px within the 390px viewport; the SVG loaded successfully. Browser error logs were empty. The temporary viewport override was reset.

Independent Standards and Spec reviews both reported no material findings. Standards also confirmed the shared roof/gable/wall geometry, seams, overhangs and seated door. Spec confirmed that the drawing follows the generated reference and fixes the reported visual problem. Parent verification covers mobile and live publication beyond those reviewers' desktop/source scope.

Ignored evidence under `artifacts/site`: `house-before.jpg`, `house-concept.png`, `house-concept-prompt.txt`, `house-draft.jpg`, `house-mobile.jpg`, `house-after-live.jpg`, `house-ci.json`, `house-live-check.json` and `house-deployment.tar.gz`. The original generated image is preserved under the Codex generated_images directory. No credentials are retained in these artifacts.

Only the house asset, its alt text and changelog changed in the implementation commit. This evidence checkpoint is separate from the deployed implementation. Existing DNS, website layout, tool icons, Revit host, TimberFold, source models and unrelated primary-checkout files were preserved. The active worktree remains available for continued website work; repository default-branch landing remains outstanding under WLK-105.
