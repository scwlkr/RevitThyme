# GitHub presentation

The live repository was inspected on October 19, 2026. Its description still called the product a pyRevit extension, the homepage was empty and it had no topics. The public website advertised 0.8.1 while GitHub had a public 0.9.3 release.

The following settings are prepared for owner approval with the website. They have not been applied to production.

| Setting | Proposed value |
| --- | --- |
| Description | Open-source native Revit toolkit. Practical BIM tools, a shared C# operation layer, CLI, MCP and an optional built-in Agent. |
| Website | `https://revitthyme.wlkrlabs.com` |
| Topics | `revit`, `bim`, `autodesk-revit`, `csharp`, `dotnet`, `wpf`, `mcp`, `revit-api`, `open-source` |
| Social preview | `website/static/img/social-card.png` (1200 × 630) |

After approval and repository landing, a maintainer with GitHub CLI authentication can run:

```powershell
gh repo edit scwlkr/RevitThyme --description 'Open-source native Revit toolkit. Practical BIM tools, a shared C# operation layer, CLI, MCP and an optional built-in Agent.' --homepage 'https://revitthyme.wlkrlabs.com' --add-topic revit --add-topic bim --add-topic autodesk-revit --add-topic csharp --add-topic dotnet --add-topic wpf --add-topic mcp --add-topic revit-api --add-topic open-source
```

Upload the social preview through **Settings → General → Social preview**. Confirm private vulnerability reporting is enabled in **Settings → Code security** before treating its report link as available. The security policy retains a profile-contact fallback.

Issue labels are defined in `labels.json`; apply them before using those labels on issues. Do not create empty starter issues just to populate a board. `good first issue` should describe a bounded, reproducible task with acceptance. Discussions can remain disabled while Issues is the maintained support channel.

Keep the public description and homepage aligned when product direction changes. The README, contributor guide, issue forms, PR template, governance and support files are source-controlled and reviewed alongside the code.
