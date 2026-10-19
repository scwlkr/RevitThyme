import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const website = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const root = path.resolve(website, '..');
export const origin = 'https://revitthyme.wlkrlabs.com';
export const repository = 'https://github.com/scwlkr/RevitThyme';
export function product() {
  const props = fs.readFileSync(path.join(root, 'Directory.Build.props'), 'utf8');
  const version = props.match(/<Version>([^<]+)<\/Version>/)?.[1];
  if (!/^\d+\.\d+\.\d+$/.test(version ?? ''))
    throw new Error('Expected a three-part release version in Directory.Build.props');
  const tools = fs
    .readdirSync(path.join(root, 'src/RevitThyme.Mcp'))
    .filter((name) => name.endsWith('.cs'))
    .flatMap((name) => {
      const source = fs.readFileSync(path.join(root, 'src/RevitThyme.Mcp', name), 'utf8');
      return [...source.matchAll(/\[McpServerTool\(Name\s*=\s*"([^"]+)"/g)].map(
        (match) => match[1],
      );
    })
    .sort();
  if (!tools.length || new Set(tools).size !== tools.length)
    throw new Error('Missing or duplicate MCP tool names');
  return {
    version,
    tag: `v${version}`,
    releaseUrl: `${repository}/releases/tag/v${version}`,
    assetBase: `${repository}/releases/download/v${version}`,
    toolCount: tools.length,
    tools,
  };
}
const rootPages = [
  'FEATURES',
  'CONTRIBUTING',
  'PROJECT',
  'GOVERNANCE',
  'CODE_OF_CONDUCT',
  'SUPPORT',
  'SECURITY',
  'GLOSSARY',
  'CHANGELOG',
  'THIRD-PARTY-NOTICES',
];
export function sources() {
  return [
    ...rootPages.map((name) => `${name}.md`),
    ...['docs', 'verification'].flatMap((dir) =>
      fs
        .readdirSync(path.join(root, dir))
        .filter((name) => name.endsWith('.md'))
        .map((name) => `${dir}/${name}`),
    ),
  ];
}
export function slug(file) {
  if (file === 'docs/INDEX.md') return '/';
  if (file === 'verification/README.md') return '/verification';
  if (file.startsWith('verification/')) return `/${file.replace(/\.md$/, '')}`;
  const name = path.basename(file, '.md').toLowerCase().replaceAll('_', '-');
  return `/${name}`;
}
export const route = (file) => `/docs${slug(file) === '/' ? '/' : `${slug(file)}/`}`;
export function mkdirWrite(target, content) {
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, content);
}
