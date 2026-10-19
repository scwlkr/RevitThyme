import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

export function publicationFiles(directory) {
  const files = {};
  function walk(dir) {
    for (const entry of fs
      .readdirSync(dir, { withFileTypes: true })
      .sort((a, b) => a.name.localeCompare(b.name))) {
      const file = path.join(dir, entry.name);
      if (entry.isSymbolicLink()) throw new Error(`Symlink in publication: ${file}`);
      if (entry.isDirectory()) walk(file);
      else if (entry.isFile()) {
        const relative = path.relative(directory, file).split(path.sep).join('/');
        const url =
          relative === '404.html'
            ? '/404'
            : `/${entry.name === 'index.html' ? relative.slice(0, -10) : relative}`;
        files[url] = digest(fs.readFileSync(file));
      }
    }
  }
  walk(directory);
  return files;
}

// Match only the exact host-injected Cloudflare script, allowing its ray/time tokens.
// Every other HTML byte, including other scripts, remains covered by the hash.
const hostScript =
  "<script>(function(){function c(){var b=a.contentDocument||(a.contentWindow&&a.contentWindow.document);if(b){var d=b.createElement('script');d.innerHTML=\"window.__CF$cv$params={r:'RAY_TOKEN',t:'TIME_TOKEN'};var a=document.createElement('script');a.src='/cdn-cgi/challenge-platform/scripts/jsd/main.js';document.getElementsByTagName('head')[0].appendChild(a);\";b.getElementsByTagName('head')[0].appendChild(d)}}if(document.body){var a=document.createElement('iframe');a.height=1;a.width=1;a.style.position='absolute';a.style.top=0;a.style.left=0;a.style.border='none';a.style.visibility='hidden';document.body.appendChild(a);if('loading'!==document.readyState)c();else if(window.addEventListener)document.addEventListener('DOMContentLoaded',c);else{var e=document.onreadystatechange||function(){};document.onreadystatechange=function(b){e(b);'loading'!==document.readyState&&(document.onreadystatechange=e,c())}}}})();</script>";
const hostPattern = new RegExp(
  hostScript
    .replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    .replace('RAY_TOKEN', '[a-f0-9]+')
    .replace('TIME_TOKEN', '[A-Za-z0-9+/=]+'),
);
export function publishedBytes(url, bytes) {
  if (url.endsWith('/') || url === '/404')
    return Buffer.from(bytes.toString('utf8').replace(hostPattern, ''));
  return bytes;
}

export const digest = (bytes) => createHash('sha256').update(bytes).digest('hex');

export function validatePublication(record) {
  assert.equal(record.schema, 1, 'Unsupported publication record');
  assert.equal(record.origin, 'https://revitthyme.wlkrlabs.com');
  assert.ok(
    Number.isSafeInteger(record.siteVersion) && record.siteVersion > 0,
    'Invalid Site version',
  );
  assert.match(record.approval, /^https:\/\/github.com\/scwlkr\/RevitThyme\/pull\/\d+$/);
  for (const key of ['sourceCommit', 'siteSourceCommit'])
    assert.match(record[key], /^[a-f0-9]{40}$/, `Invalid ${key}`);
  for (const required of [
    '/',
    '/content-manifest.json',
    '/deployment-source.json',
    '/install',
    '/install.ps1',
    '/llms.txt',
    '/llms-full.txt',
  ])
    assert.ok(record.files[required], `Publication is missing ${required}`);
  for (const [url, hash] of Object.entries(record.files)) {
    assert.ok(
      url.startsWith('/') && !url.startsWith('//') && !/[?#\\]/.test(url),
      `Invalid publication path: ${url}`,
    );
    assert.equal(new URL(url, record.origin).origin, record.origin);
    assert.match(hash, /^[a-f0-9]{64}$/, `Invalid hash: ${url}`);
  }
}

export function verifyFile(url, bytes, expected) {
  assert.equal(
    digest(publishedBytes(url, bytes)),
    expected,
    `Live file differs from approved deployment: ${url}`,
  );
}

export function verifyProvenance(bytes, record) {
  const source = JSON.parse(bytes.toString('utf8'));
  assert.equal(source.repository, 'https://github.com/scwlkr/RevitThyme');
  assert.equal(source.commit, record.sourceCommit, 'Live source revision differs from approval');
}

export function verifySettings(actual, expected, labels, expectedLabels, socialImage, reporting) {
  for (const key of ['description', 'homepage', 'has_issues', 'has_discussions'])
    assert.equal(actual[key], expected[key], `GitHub ${key} drifted`);
  assert.deepEqual([...actual.topics].sort(), [...expected.topics].sort(), 'GitHub topics drifted');
  assert.equal(socialImage, expected.socialImage, 'GitHub social preview drifted');
  assert.equal(
    reporting,
    expected.privateVulnerabilityReporting,
    'Private vulnerability reporting drifted',
  );
  // Only declared labels are managed. Additional project labels are permitted.
  for (const label of expectedLabels) {
    const live = labels.find((item) => item.name === label.name);
    assert.ok(live, `Missing GitHub label: ${label.name}`);
    assert.equal(
      live.color.toLowerCase(),
      label.color.toLowerCase(),
      `Label color drifted: ${label.name}`,
    );
    assert.equal(live.description, label.description, `Label description drifted: ${label.name}`);
  }
}
