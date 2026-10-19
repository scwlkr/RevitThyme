import fs from 'node:fs';
import path from 'node:path';
import { Resvg } from '@resvg/resvg-js';
import { createHash } from 'node:crypto';
import {
  website,
  root,
  origin,
  repository,
  sources,
  slug,
  route,
  mkdirWrite,
  product,
} from './content.mjs';

// Canonical Markdown remains in the repository. Only generated files live here.
const files = sources();
const known = new Set(files);
const generated = path.join(website, '.generated/docs');
const index = [];
const metadata = product();
const logo = fs.readFileSync(path.join(root, 'assets/branding/revitthyme-logo.svg'), 'utf8');
const socialSvg = `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630"><rect width="1200" height="630" fill="#ffffff"/><g transform="translate(80 150) scale(10)">${logo}</g><text x="80" y="470" font-size="32" fill="#174445" font-family="Arial">Native tools for Revit 2027</text></svg>`;
const social = new Resvg(socialSvg, {
  font: { loadSystemFonts: true, defaultFontFamily: 'Arial' },
});
mkdirWrite(path.join(website, 'static/img/social-card.png'), social.render().asPng());
mkdirWrite(path.join(website, '.generated/product.json'), JSON.stringify(metadata, null, 2));
for (const file of files) {
  const raw = fs.readFileSync(path.join(root, file), 'utf8').replace(/^\uFEFF/, '');
  const title = raw.match(/^# (.+)$/m)?.[1] ?? path.basename(file, '.md');
  const description =
    raw
      .split('\n')
      .find((line) => line.trim() && !line.startsWith('#') && !line.startsWith('>'))
      ?.replace(/[`*_\[\]]/g, '')
      .slice(0, 155) ?? title;
  const content = raw.replace(/(!?\[[^\]\n]*\]\()([^\s)]+)(\))/g, (match, prefix, href, suffix) => {
    if (/^(?:[a-z]+:|#|\/)/i.test(href)) return match;
    const [target, anchor] = href.split('#');
    const resolved = path.posix.normalize(path.posix.join(path.posix.dirname(file), target));
    if (!fs.existsSync(path.join(root, resolved)))
      throw new Error(`Missing source link in ${file}: ${href}`);
    if (known.has(resolved)) return match;
    if (resolved.startsWith('docs/images/')) {
      const imagePath = `/generated/images/${path.posix.basename(resolved)}`;
      // File links must bypass Docusaurus route normalization. A trailing slash
      // on a PNG can appear valid on Windows but is a missing file on Linux.
      return `${prefix}${prefix.startsWith('!') ? '' : 'pathname://'}${imagePath}${suffix}`;
    }
    return `${prefix}${repository}/blob/master/${resolved}${anchor ? `#${anchor}` : ''}${suffix}`;
  });
  const frontmatter = `---\nslug: ${JSON.stringify(slug(file))}\ntitle: ${JSON.stringify(title)}\ndescription: ${JSON.stringify(description)}\n---\n\n`;
  mkdirWrite(path.join(generated, file), frontmatter + content);
  index.push({ file, title, route: route(file), raw });
}
// Remove only stale files owned by this generator, never source documents.
for (const dir of ['', 'docs', 'verification']) {
  const location = path.join(generated, dir);
  if (!fs.existsSync(location)) continue;
  for (const name of fs.readdirSync(location)) {
    if (name.endsWith('.md') && !known.has(path.posix.join(dir, name)))
      fs.unlinkSync(path.join(location, name));
  }
}
fs.cpSync(path.join(root, 'docs/images'), path.join(website, 'static/generated/images'), {
  recursive: true,
});
fs.cpSync(path.join(root, 'assets/branding'), path.join(website, 'static/generated/branding'), {
  recursive: true,
});
const darkColors = {
  '#0E3538': '#d3e3dc',
  '#174445': '#d3e3dc',
  '#255E5B': '#a4c5b7',
  '#306F66': '#a4c5b7',
  '#4F7C4D': '#a0bd8c',
};
mkdirWrite(
  path.join(website, 'static/generated/branding/revitthyme-logo-dark.svg'),
  logo.replace(/#[0-9A-F]{6}/g, (color) => darkColors[color] ?? color),
);
mkdirWrite(
  path.join(website, 'static/generated/favicon.svg'),
  logo
    .replace(/viewBox="[^"]+"/, 'viewBox="1.45 28.41 24 23.1"')
    .replace(/width="[^"]+" height="[^"]+"/, 'width="32" height="32"'),
);
mkdirWrite(
  path.join(website, 'static/llms.txt'),
  `# RevitThyme\n\n> Open-source native Revit 2027 toolkit. C#/.NET, WPF, CLI and ${metadata.toolCount}-tool MCP. Release ${metadata.tag}. Pre-1.0; GPL-3.0-or-later.\n\nNative tools do not require AI. Trusted C# is opt-in and not sandboxed. Read session targeting and recovery before writes.\n\n## Documentation\n\n${index.map((doc) => `- [${doc.title}](${origin}${doc.route}): ${doc.file}`).join('\n')}\n\n## Full text\n\n- [Complete documentation](${origin}/llms-full.txt)\n- [Source](${repository})\n`,
);
mkdirWrite(
  path.join(website, 'static/llms-full.txt'),
  index
    .map(
      (doc) =>
        `# ${doc.title}\nSource: ${repository}/blob/master/${doc.file}\nURL: ${origin}${doc.route}\n\n${doc.raw}`,
    )
    .join('\n\n---\n\n'),
);
mkdirWrite(
  path.join(website, '.generated/content-index.json'),
  JSON.stringify(
    index.map(({ raw, ...doc }) => doc),
    null,
    2,
  ),
);
mkdirWrite(
  path.join(website, 'static/content-manifest.json'),
  JSON.stringify(
    {
      version: metadata.version,
      tools: metadata.tools,
      documents: Object.fromEntries(
        index.map((doc) => [doc.file, createHash('sha256').update(doc.raw).digest('hex')]),
      ),
    },
    null,
    2,
  ),
);
// Keep curl/irm endpoints as PowerShell bytes, not framework HTML routes.
fs.copyFileSync(path.join(root, 'installer/bootstrap.ps1'), path.join(website, 'static/install'));
fs.copyFileSync(
  path.join(root, 'installer/bootstrap.ps1'),
  path.join(website, 'static/install.ps1'),
);
const bootstrap = fs.readFileSync(path.join(root, 'installer/bootstrap.ps1'), 'utf8');
if (bootstrap.split('param([string]$Version,').length !== 2)
  throw new Error('Bootstrap version pin must be reviewed after parameter changes');
mkdirWrite(
  path.join(website, 'static/install-0.8.1'),
  `#Requires -Version 5.1\n& {\n${bootstrap.replace('param([string]$Version,', "param([string]$Version = '0.8.1',")}\n} @args\n`,
);
console.log(
  `Prepared ${index.length} canonical documents, native screenshots, installer endpoints and agent indexes.`,
);
