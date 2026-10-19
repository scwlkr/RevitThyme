# RevitThyme website

Docusaurus source for the RevitThyme product website and docs. Requires Node.js 22+; CI uses Node 24. See [website maintenance](../docs/WEBSITE.md).

```powershell
npm ci
npm start
```

Local editing: `http://127.0.0.1:3100`. For full-text search and acceptance, stop the editing server and run:

```powershell
npm run build
npm run check
npm run serve
```

Edit canonical root/docs/verification Markdown. Generated files are ignored. Release metadata comes from `Directory.Build.props`; the MCP reference is checked against C# attributes. CI builds review artifacts only and never publishes.

`npm run check:live` checks production files and managed GitHub settings against the reviewed baselines in `.github/`. `npm run test:publication` runs offline regression tests. The daily live workflow reports drift and source changes awaiting deployment review; it cannot publish or update the baseline. See the maintenance guide before recording a separately approved deployment.
