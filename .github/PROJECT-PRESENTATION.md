# GitHub presentation

[`repository-presentation.json`](repository-presentation.json) defines the managed repository settings: description, homepage, exact topic set, Issues/Discussions, private vulnerability reporting and social-preview URL. [`labels.json`](labels.json) defines managed labels; other labels are allowed. Keep these files aligned with reviewed product changes.

The read-only **Live publication drift** workflow compares GitHub against these files daily and after `master` pushes. It reports differences without repairing settings or deploying. See [live publication checks](../docs/WEBSITE.md#live-publication-checks) for failure handling and the separate deployment approval process.

A maintainer applies intentional settings changes through GitHub or the authenticated GitHub CLI. Review the JSON change alongside that action. Upload the generated `website/static/img/social-card.png` under **Settings → General → Social preview**, then record its saved image URL in the JSON. Keep private vulnerability reporting enabled under **Settings → Advanced Security**. The security policy retains a profile-contact fallback.

Apply declared labels before using them on issues. Do not create empty starter issues just to populate a board. `good first issue` should describe a bounded, reproducible task with acceptance. Issues remains the maintained support channel.

The README, contributor guide, issue forms, PR template, governance and support files are source-controlled and reviewed alongside the code.
