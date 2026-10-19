import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  digest,
  publishedBytes,
  validatePublication,
  verifyFile,
  verifyProvenance,
  verifySettings,
} from './publication.mjs';
import fs from 'node:fs';
import path from 'node:path';
import { root } from './content.mjs';

const read = (file) => JSON.parse(fs.readFileSync(path.join(root, '.github', file), 'utf8'));
const record = read('website-publication.json');
const settings = read('repository-presentation.json');
const labels = read('labels.json');
const verify = (actual = settings, liveLabels = labels, social = settings.socialImage) =>
  verifySettings(actual, settings, liveLabels, labels, social, true);

test('approved record contains the critical endpoints and valid hashes', () =>
  validatePublication(record));
test('missing installer coverage is rejected', () => {
  const changed = structuredClone(record);
  delete changed.files['/install'];
  assert.throws(() => validatePublication(changed), /missing \/install/);
});
test('a baseline cannot silently claim a newer source than the deployed provenance', () => {
  const source = Buffer.from(
    JSON.stringify({
      repository: 'https://github.com/scwlkr/RevitThyme',
      commit: record.sourceCommit,
    }),
  );
  verifyProvenance(source, record);
  assert.throws(
    () => verifyProvenance(source, { ...record, sourceCommit: 'a'.repeat(40) }),
    /revision differs/,
  );
});
test('external paths cannot redirect a file check', () => {
  const changed = structuredClone(record);
  changed.files['//other.example/install'] = digest('x');
  assert.throws(() => validatePublication(changed), /Invalid publication path/);
});
test('a changed live byte fails even with a correct version or provenance file', () => {
  verifyFile('/', Buffer.from('approved'), digest('approved'));
  assert.throws(() => verifyFile('/', Buffer.from('changed'), digest('approved')), /differs/);
});
test('only the exact hosting security script is excluded from HTML hashes', () => {
  const fixture = fs.readFileSync(new URL('./fixtures/cloudflare.txt', import.meta.url));
  const approved = '<html><body><h1>Approved</h1></body></html>';
  verifyFile('/', fixture, digest(approved));
  const anotherRay = Buffer.from(
    fixture.toString().replace(/r:'[a-f0-9]+'/, "r:'0123456789abcdef'"),
  );
  verifyFile('/', anotherRay, digest(approved));
  assert.throws(
    () =>
      verifyFile(
        '/',
        Buffer.from(fixture.toString().replace('Approved', 'Changed')),
        digest(approved),
      ),
    /differs/,
  );
  assert.throws(
    () =>
      verifyFile(
        '/',
        Buffer.from(fixture.toString().replace('function c()', 'function malicious()')),
        digest(approved),
      ),
    /differs/,
  );
  assert.equal(
    publishedBytes('/install', fixture),
    fixture,
    'Binary/script endpoints must never be normalized',
  );
});
test('correct settings and unmanaged extra labels are allowed', () => {
  verify();
  verify(settings, [...labels, { name: 'another issue category' }]);
});
test('repository description, homepage and topic drift fail', () => {
  for (const key of ['description', 'homepage', 'topics', 'has_issues', 'has_discussions']) {
    const changed = structuredClone(settings);
    changed[key] = key === 'topics' ? [...changed.topics, 'unexpected'] : 'changed';
    assert.throws(() => verify(changed), /drifted/);
  }
});
test('disabling private vulnerability reporting fails', () => {
  assert.throws(
    () => verifySettings(settings, settings, labels, labels, settings.socialImage, false),
    /reporting drifted/,
  );
});
test('missing labels and changed descriptions or colors fail', () => {
  assert.throws(() => verify(settings, []), /Missing GitHub label/);
  for (const key of ['description', 'color']) {
    const changed = structuredClone(labels);
    changed[0][key] = 'changed';
    assert.throws(() => verify(settings, changed), /drifted/);
  }
});
test('replaced or removed social preview fails', () => {
  assert.throws(
    () => verify(settings, labels, 'https://other.example/image'),
    /social preview drifted/,
  );
  assert.throws(() => verify(settings, labels, null), /social preview drifted/);
});
