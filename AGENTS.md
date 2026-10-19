# RevitThyme

> RevitThyme is an open-source, native Revit toolkit that makes Autodesk Revit easier to use, automate, and extend.
>
> It provides practical tools for everyday BIM work, a common operation layer accessible through its UI, CLI, and MCP, and an optional AI interface powered by existing agent runtimes.
>
> Its goal is to give users and AI agents powerful access to Revit while making complex workflows repeatable, inspectable, and reversible wherever practical.

For development and live Revit testing, follow the [isolated development workflow](docs/DEVELOPMENT.md) by default. Reuse a verified, pinned development session or launch one with `project.cmd dev-start`. Keep working Revit sessions and models separate from testing; never use them as a test fallback or close/restart them without explicit user authorization.

For ribbon organization, controls, tooltips, or SVG icons, use the [revitthyme-ribbon skill](https://github.com/scwlkr/skillsies/blob/main/personal/revitthyme-ribbon/SKILL.md).

For user-visible behavior, CLI/MCP contracts or releases, update the relevant canonical guides and changelog in the same change. Read [website maintenance](docs/WEBSITE.md) for content sources, drift checks and release review. Website/docs changes require `npm ci`, `npm run build` and `npm run check` in `website`; keep public deployment as a separate owner-approved action.
