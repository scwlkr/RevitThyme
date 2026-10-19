const redirects = {
  '/docs/getting-started/': '/docs/quick-start/',
  '/docs/tools/': '/docs/native-utilities/',
  '/docs/routes/': '/docs/migration/',
  '/docs/execution/': '/docs/dynamic-execution/',
  '/docs/agent-workflow/': '/docs/agent-integration/',
};
module.exports = function legacyRoutes() {
  return {
    name: 'revitthyme-legacy-routes',
    async postBuild({ outDir }) {
      const fs = require('node:fs');
      const path = require('node:path');
      for (const [from, to] of Object.entries(redirects)) {
        const dir = path.join(outDir, from);
        fs.mkdirSync(dir, { recursive: true });
        fs.writeFileSync(
          path.join(dir, 'index.html'),
          `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>RevitThyme — page moved</title><link rel="canonical" href="https://revitthyme.wlkrlabs.com${to}"><meta http-equiv="refresh" content="0;url=${to}"></head><body><a href="${to}">Continue to the current documentation</a></body></html>`,
        );
      }
    },
  };
};
