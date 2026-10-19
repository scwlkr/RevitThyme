import assert from 'node:assert/strict';
import { product, repository } from './content.mjs';

// Explicit network check: does not install, execute, publish or modify a release.
const metadata = product();
const api = 'https://api.github.com/repos/scwlkr/RevitThyme';
async function getJson(url) {
  const response = await fetch(url, {
    headers: {
      'User-Agent': 'RevitThyme-documentation-check',
      Accept: 'application/vnd.github+json',
    },
    signal: AbortSignal.timeout(20000),
  });
  if (!response.ok) throw new Error(`GitHub release check failed: ${response.status} ${url}`);
  return response.json();
}
const release = await getJson(`${api}/releases/tags/${metadata.tag}`);
assert.equal(release.tag_name, metadata.tag);
assert.equal(release.draft, false, 'Advertised release must be public');
assert.equal(release.prerelease, false, 'Advertised stable release must not be a prerelease');
assert.equal(release.html_url, metadata.releaseUrl);
const required = [
  `RevitThyme-${metadata.version}-win-x64.exe`,
  `RevitThyme-${metadata.version}-win-x64.exe.sha256`,
  'install.ps1',
  'install.ps1.sha256',
  'install-revitthyme.ps1',
  'install-revitthyme.ps1.sha256',
  'release.json',
];
for (const name of required) {
  const assets = release.assets.filter((asset) => asset.name === name);
  assert.equal(assets.length, 1, `Expected one release asset: ${name}`);
  assert.equal(assets[0].browser_download_url, `${metadata.assetBase}/${name}`);
  assert.ok(assets[0].size > 0, `${name} is empty`);
}
const latest = await getJson(`${api}/releases/latest`);
assert.equal(
  latest.tag_name,
  metadata.tag,
  `Website source advertises ${metadata.tag}, but latest stable is ${latest.tag_name}; align the source, docs and release`,
);
console.log(
  `${repository}: ${metadata.tag} is the latest public stable release with all ${required.length} required assets.`,
);
