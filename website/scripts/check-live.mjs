import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { load } from 'cheerio';
import { root } from './content.mjs';
import {
  validatePublication,
  verifyFile,
  verifyProvenance,
  verifySettings,
} from './publication.mjs';

const read = (name) => JSON.parse(fs.readFileSync(path.join(root, '.github', name), 'utf8'));
const publication = read('website-publication.json');
const settings = read('repository-presentation.json');
validatePublication(publication);
if (settings.repository !== 'scwlkr/RevitThyme') throw new Error('Unexpected repository');
const failures = [];
async function request(url, json = false) {
  const headers = { 'User-Agent': 'RevitThyme-publication-check' };
  // Never send the GitHub token to the website, redirects or image hosts.
  if (new URL(url).origin === 'https://api.github.com') {
    headers.Accept = 'application/vnd.github+json';
    if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;
  }
  const response = await fetch(url, {
    headers,
    redirect: 'error',
    signal: AbortSignal.timeout(30000),
  });
  if (!response.ok)
    throw new Error(`${url}: HTTP ${response.status}; live state could not be verified`);
  return json ? response.json() : Buffer.from(await response.arrayBuffer());
}
async function check(name, action) {
  try {
    await action();
  } catch (error) {
    failures.push(`${name}: ${error.message}`);
  }
}

const queue = Object.entries(publication.files);
await Promise.all(
  Array.from({ length: 6 }, async () => {
    while (queue.length) {
      const [url, hash] = queue.shift();
      await check(url, async () => {
        const bytes = await request(new URL(url, publication.origin));
        verifyFile(url, bytes, hash);
        if (url === '/deployment-source.json') verifyProvenance(bytes, publication);
      });
    }
  }),
);
await check('GitHub presentation', async () => {
  const api = `https://api.github.com/repos/${settings.repository}`;
  const [repo, html, reporting] = await Promise.all([
    request(api, true),
    request(`https://github.com/${settings.repository}`),
    request(`${api}/private-vulnerability-reporting`, true),
  ]);
  const labels = [];
  for (let page = 1; ; page++) {
    const batch = await request(`${api}/labels?per_page=100&page=${page}`, true);
    labels.push(...batch);
    if (batch.length < 100) break;
    if (page === 20) throw new Error('Label pagination exceeded safety limit');
  }
  const social = load(html)('meta[property="og:image"]').attr('content');
  verifySettings(repo, settings, labels, read('labels.json'), social, reporting.enabled);
  await request(settings.socialImage); // Missing/deleted artwork is also a failure.
});

// New source is allowed to await approval. Never advance the production baseline here.
const changes = execFileSync(
  'git',
  [
    'diff',
    '--name-only',
    publication.sourceCommit,
    '--',
    'website',
    'docs',
    'verification',
    '*.md',
    'Directory.Build.props',
    'installer/bootstrap.ps1',
    'assets/branding',
    'src/RevitThyme.Mcp',
    'src/RevitThyme.Cli',
  ],
  { cwd: root, encoding: 'utf8' },
).trim();
const summary = [
  '# Publication drift check',
  `Approved source: ${publication.sourceCommit}; Site version: ${publication.siteVersion}.`,
  `Checked ${Object.keys(publication.files).length} public files and managed GitHub presentation settings.`,
  changes
    ? `Source changes await a separate deployment review:\n\n${changes
        .split('\n')
        .map((file) => `- ${file}`)
        .join('\n')}`
    : 'No unpublished source changes.',
  failures.length
    ? `Failures:\n\n${failures.map((failure) => `- ${failure}`).join('\n')}`
    : 'Live publication matches the approved baseline.',
].join('\n\n');
console.log(summary);
if (process.env.GITHUB_STEP_SUMMARY)
  fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${summary}\n`);
if (failures.length) process.exitCode = 1;
