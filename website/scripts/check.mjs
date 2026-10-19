import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { load } from 'cheerio';
import { root, website, sources, product } from './content.mjs';

const build = path.join(website, 'build');
const errors = [];
let checked = 0;
const requireThat = (condition, message) => {
  checked++;
  if (!condition) errors.push(message);
};
const read = (file) => fs.readFileSync(path.join(root, file), 'utf8').replace(/^\uFEFF/, '');
const metadata = product();
for (const file of ['README.md', 'AGENTS.md', 'website/README.md', ...sources()]) {
  for (const match of read(file).matchAll(/!?\[[^\]\n]*\]\(([^\s)]+)\)/g)) {
    const href = match[1];
    if (/^(?:[a-z]+:|#|\/)/i.test(href)) continue;
    requireThat(
      fs.existsSync(path.resolve(root, path.dirname(file), href.split('#')[0])),
      `${file} has a broken source link: ${href}`,
    );
  }
}
const index = JSON.parse(
  fs.readFileSync(path.join(website, '.generated/content-index.json'), 'utf8'),
);
requireThat(
  JSON.stringify(index.map((doc) => doc.file).sort()) === JSON.stringify(sources().sort()),
  'Generated document index is stale: run npm run build',
);
requireThat(
  JSON.stringify(metadata) ===
    JSON.stringify(
      JSON.parse(fs.readFileSync(path.join(website, '.generated/product.json'), 'utf8')),
    ),
  'Generated product metadata differs from C# source',
);

// Equality guards catch both undocumented additions and documentation for removed tools.
const reference = [...read('docs/MCP.md').matchAll(/^\| `([a-z_]+)` \|/gm)]
  .map((match) => match[1])
  .sort();
requireThat(
  JSON.stringify(reference) === JSON.stringify(metadata.tools),
  `MCP reference must match source tool inventory (${metadata.toolCount} tools)`,
);
const cliCommands = [
  ...read('src/RevitThyme.Cli/Program.cs').matchAll(/^\s*revitthyme ([^\r\n]+)/gm),
]
  .filter((match) => !match[1].startsWith('--version'))
  .flatMap((match) => {
    const [command, subcommand] = match[1].split(' ');
    return command.includes('|')
      ? command.split('|')
      : [
          /^(update|view-range|room-dimensions)$/.test(command)
            ? `${command} ${subcommand}`
            : command,
        ];
  })
  .sort();
const cliReference = read('docs/CLI.md')
  .split('\n')
  .filter((line) => line.startsWith('| `'))
  .flatMap((line) => [...line.split(' | ')[0].matchAll(/`([^`]+)`/g)].map((match) => match[1]))
  .sort();
requireThat(
  JSON.stringify(cliCommands) === JSON.stringify(cliReference),
  `CLI guide inventory differs from implemented --help: ${cliCommands.join(', ')}`,
);
for (const file of ['README.md', 'FEATURES.md', 'PROJECT.md', 'docs/ROADMAP.md', 'docs/INDEX.md']) {
  const text = read(file);
  const counts = [
    ...text.matchAll(
      /(?:an? )?(\d+)[- ]tool(?: local)? MCP|MCP (?:server, |server exposes |and CLI \| )?(\d+) tools/g,
    ),
  ].map((match) => Number(match[1] ?? match[2]));
  for (const count of counts)
    requireThat(
      count === metadata.toolCount,
      `${file} advertises ${count} MCP tools, source has ${metadata.toolCount}`,
    );
}
requireThat(
  read('docs/INSTALLATION.md').includes(`Version **${metadata.version}**`),
  'Installation guide release differs from Directory.Build.props',
);
requireThat(
  new RegExp(`^## ${metadata.version.replaceAll('.', '\\.')}[ —-]`, 'm').test(read('CHANGELOG.md')),
  'Changelog needs an entry for the current source release',
);
requireThat(
  read('FEATURES.md').includes(`Version ${metadata.version} `),
  'Feature inventory release differs from source',
);
for (const file of [
  'PROJECT.md',
  'CONTRIBUTING.md',
  'docs/MCP.md',
  'docs/NATIVE-UTILITIES.md',
  'docs/DYNAMIC-EXECUTION.md',
]) {
  requireThat(
    !/Built-in Agent and Exploration remain deferred|there is no built-in agent runtime|scope stops at Dynamic execution/i.test(
      read(file),
    ),
    `${file} contains superseded current-scope text`,
  );
}

function walk(dir) {
  return fs
    .readdirSync(dir, { withFileTypes: true })
    .flatMap((item) =>
      item.isDirectory() ? walk(path.join(dir, item.name)) : [path.join(dir, item.name)],
    );
}
assert.ok(fs.existsSync(path.join(build, 'index.html')), 'Run npm run build before npm run check');
const manifest = JSON.parse(fs.readFileSync(path.join(build, 'content-manifest.json'), 'utf8'));
requireThat(
  manifest.version === metadata.version &&
    JSON.stringify(manifest.tools) === JSON.stringify(metadata.tools),
  'Built site product metadata is stale',
);
for (const doc of sources())
  requireThat(
    manifest.documents[doc] === createHash('sha256').update(read(doc)).digest('hex'),
    `Built site is stale for ${doc}; rebuild after source changes`,
  );
const htmlFiles = walk(build).filter((file) => file.endsWith('.html'));
const sidebar = (await import('../sidebars.js')).default.docs;
const published = new Set();
function collect(items) {
  for (const item of items) {
    if (typeof item === 'string') published.add(item);
    else if (item.type === 'autogenerated')
      index
        .filter((doc) => doc.file.startsWith(item.dirName + '/'))
        .forEach((doc) => published.add(doc.file.replace(/\.md$/, '')));
    else if (item.items) collect(item.items);
  }
}
collect(sidebar);
for (const doc of index)
  requireThat(
    published.has(doc.file.replace(/\.md$/, '')),
    `${doc.file} is missing from sidebar navigation`,
  );
const cache = new Map(htmlFiles.map((file) => [file, load(fs.readFileSync(file, 'utf8'))]));
const seen = new Set();
for (const [file, $] of cache) {
  const source =
    '/' +
    path
      .relative(build, file)
      .replaceAll('\\', '/')
      .replace(/index\.html$/, '');
  for (const element of $('a[href],img[src],script[src],link[href]').toArray()) {
    const href = $(element).attr('href') ?? $(element).attr('src');
    if (!href || /^(?:[a-z]+:|\/\/)/i.test(href)) continue;
    const url = new URL(href, `https://local.invalid${source}`);
    const key = `${source}:${url.pathname}${url.hash}`;
    if (seen.has(key)) continue;
    seen.add(key);
    const decoded = decodeURIComponent(url.pathname);
    let target = path.join(build, decoded);
    if (fs.existsSync(target) && fs.statSync(target).isDirectory())
      target = path.join(target, 'index.html');
    else if (!fs.existsSync(target) && fs.existsSync(target + '.html')) target += '.html';
    requireThat(fs.existsSync(target), `${source} links to missing ${decoded}`);
    if (url.hash && cache.has(target)) {
      const id = decodeURIComponent(url.hash.slice(1));
      requireThat(
        cache
          .get(target)('[id]')
          .toArray()
          .some((el) => cache.get(target)(el).attr('id') === id),
        `${source} links to missing anchor ${decoded}#${id}`,
      );
    }
    if (element.tagName === 'img')
      requireThat($(element).attr('alt') !== undefined, `${source} has an image without alt text`);
  }
}
const llms = fs.readFileSync(path.join(build, 'llms.txt'), 'utf8');
const full = fs.readFileSync(path.join(build, 'llms-full.txt'), 'utf8');
for (const doc of index) {
  requireThat(
    fs.existsSync(path.join(build, doc.route, 'index.html')),
    `${doc.file} has no rendered route`,
  );
  const page = cache.get(path.join(build, doc.route, 'index.html'));
  requireThat(
    Boolean(page?.('meta[name="description"]').attr('content')),
    `${doc.route} is missing a description`,
  );
  requireThat(
    Boolean(page?.('link[rel="canonical"]').attr('href')),
    `${doc.route} is missing a canonical URL`,
  );
  requireThat(llms.includes(doc.route), `${doc.file} is missing from llms.txt`);
  requireThat(full.includes(read(doc.file)), `${doc.file} is stale in llms-full.txt`);
  const edit = page?.('a[href]')
    .toArray()
    .some(
      (el) =>
        page(el).attr('href') === `https://github.com/scwlkr/RevitThyme/edit/master/${doc.file}`,
    );
  requireThat(edit, `${doc.route} needs an edit link to canonical source`);
}
const home = cache.get(path.join(build, 'index.html'));
const download = cache.get(path.join(build, 'download/index.html'));
const homeCopy = home('main').clone();
homeCopy.find('.install-command').remove();
const homeWords = homeCopy.text().trim().split(/\s+/).filter(Boolean).length;
requireThat(
  homeWords <= 100,
  `Homepage has ${homeWords} words; keep explanations in the docs (100-word limit)`,
);
requireThat(
  home('.navbar__logo img').attr('src') === '/generated/branding/revitthyme-logo.svg',
  'Use the original supplied RevitThyme wordmark',
);
requireThat(
  fs
    .readFileSync(path.join(build, 'generated/branding/revitthyme-logo.svg'))
    .equals(fs.readFileSync(path.join(root, 'assets/branding/revitthyme-logo.svg'))),
  'Published wordmark differs from the original',
);
requireThat(home.text().includes(metadata.tag), 'Homepage version differs from source');
requireThat(
  download.text().includes(metadata.version),
  'Download page version differs from source',
);
const assets = download('a[href]')
  .toArray()
  .map((el) => download(el).attr('href'))
  .filter((href) => /releases\/download\//.test(href));
for (const asset of assets)
  requireThat(
    asset.startsWith(metadata.assetBase + '/'),
    `Download page links to a stale release: ${asset}`,
  );
requireThat(
  assets.includes(`${metadata.assetBase}/RevitThyme-${metadata.version}-win-x64.exe`),
  'Missing current installer download',
);
requireThat(
  assets.some((href) => href.endsWith('.exe.sha256')),
  'Missing manual installer checksum',
);
requireThat(
  fs
    .readFileSync(path.join(build, 'install'))
    .equals(fs.readFileSync(path.join(root, 'installer/bootstrap.ps1'))),
  '/install is not the canonical bootstrap bytes',
);
requireThat(
  fs
    .readFileSync(path.join(build, 'install-0.8.1'), 'utf8')
    .includes("param([string]$Version = '0.8.1',"),
  'Historical installer endpoint lost its pin',
);
const search = walk(build).filter((file) => /search-index.*\.json$/.test(file));
requireThat(
  search.length > 0 && search.every((file) => fs.statSync(file).size > 1000),
  'Missing or empty production search index',
);
requireThat(fs.existsSync(path.join(build, 'sitemap.xml')), 'Missing sitemap');
requireThat(fs.existsSync(path.join(build, 'img/social-card.png')), 'Missing share image');
console.log(
  `${checked} checks across ${htmlFiles.length} HTML pages, ${index.length} canonical documents and ${metadata.toolCount} MCP tools.`,
);
const evidence = path.join(root, 'artifacts/website');
fs.mkdirSync(evidence, { recursive: true });
fs.writeFileSync(
  path.join(evidence, 'checks.json'),
  JSON.stringify(
    {
      passed: errors.length === 0,
      checks: checked,
      htmlPages: htmlFiles.length,
      documents: index.length,
      product: metadata,
      errors,
    },
    null,
    2,
  ),
);
if (errors.length) {
  console.error(errors.join('\n'));
  process.exitCode = 1;
} else
  console.log(
    'Links, anchors, assets, metadata, agent indexes, source edit links and product contracts passed.',
  );
