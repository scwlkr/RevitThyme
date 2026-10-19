import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { publicationFiles } from './publication.mjs';
import { command, readJson, canonical } from './publish-tools.mjs';

export function verifyArchive(state) {
  const cwd = path.dirname(state.paths.archive),
    archive = path.basename(state.paths.archive);
  const names = command('tar', ['-tf', archive], cwd).split(/\r?\n/);
  const details = command('tar', ['-tvf', archive], cwd).split(/\r?\n/);
  assert.equal(names.length, details.length, 'Unrecognized archive listing');
  const expected = new Set(
    Object.keys(state.candidate.files).map(
      (url) =>
        `dist/${url === '/404' ? '404.html' : url.endsWith('/') ? `${url.slice(1)}index.html` : url.slice(1)}`,
    ),
  );
  expected.add('dist/.openai/hosting.json');
  const directories = new Set();
  for (const file of expected) {
    let dir = path.posix.dirname(file);
    while (dir !== '.') {
      directories.add(dir);
      dir = path.posix.dirname(dir);
    }
  }
  const seen = new Set();
  for (let i = 0; i < names.length; i++) {
    const name = names[i].replace(/^\.\//, '').replace(/\/$/, '');
    assert.ok(
      !name.startsWith('/') && !/[\\:\0]/.test(name) && !name.split('/').includes('..'),
      'Unsafe archive path',
    );
    const type = details[i][0];
    assert.ok(
      ['-', 'd'].includes(type) && !/ -> | link to /.test(details[i]),
      'Archive links/special files are unsupported',
    );
    if (type === 'd') assert.ok(directories.has(name), `Unexpected archive directory: ${name}`);
    else {
      assert.ok(
        expected.has(name) && !seen.has(name),
        `Unexpected/duplicate archive file: ${name}`,
      );
      seen.add(name);
    }
  }
  assert.equal(seen.size, expected.size, 'Archive is missing candidate files');
  const extracted = fs.mkdtempSync(path.join(state.paths.run, 'archive-check-'));
  command('tar', ['-xf', archive, '-C', extracted], cwd);
  const manifest = readJson(path.join(extracted, 'dist/.openai/hosting.json'));
  assert.equal(
    canonical(manifest),
    canonical(readJson(path.join(state.paths.site, '.openai/hosting.json'))),
    'Packaged hosting manifest changed',
  );
  const files = publicationFiles(path.join(extracted, 'dist'));
  delete files['/.openai/hosting.json']; // Validated helper deployment metadata, not a public asset.
  assert.equal(
    canonical(files),
    canonical(state.candidate.files),
    'Archive bytes differ from the approved candidate',
  );
}
