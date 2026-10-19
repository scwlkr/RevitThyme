// Records reviewed build bytes; does not upload, deploy or change GitHub settings.
import fs from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { execFileSync } from 'node:child_process';
import { root, origin } from './content.mjs';
import { publicationFiles, validatePublication } from './publication.mjs';

const { values } = parseArgs({
  options: Object.fromEntries(
    ['build', 'source', 'site-source', 'site-version', 'approval', 'output'].map((key) => [
      key,
      { type: 'string' },
    ]),
  ),
});
for (const key of ['build', 'source', 'site-source', 'site-version', 'approval'])
  if (!values[key]) throw new Error(`Required: --${key}`);
if (!/^https:\/\/github.com\/scwlkr\/RevitThyme\/pull\/\d+$/.test(values.approval))
  throw new Error('--approval must link the reviewed publication PR');
for (const key of ['source', 'site-source'])
  if (!/^[a-f0-9]{40}$/.test(values[key])) throw new Error(`--${key} must be a full commit SHA`);
const build = path.resolve(values.build);
const provenance = JSON.parse(fs.readFileSync(path.join(build, 'deployment-source.json'), 'utf8'));
if (
  provenance.commit !== values.source ||
  provenance.repository !== 'https://github.com/scwlkr/RevitThyme'
)
  throw new Error('Build provenance does not match the approved source');
if (
  execFileSync('git', ['cat-file', '-t', values.source], { cwd: root, encoding: 'utf8' }).trim() !==
  'commit'
)
  throw new Error('--source must identify a locally available source commit');
const files = publicationFiles(build);
const record = {
  schema: 1,
  origin,
  sourceCommit: values.source,
  siteSourceCommit: values['site-source'],
  siteVersion: Number(values['site-version']),
  approval: values.approval,
  files,
};
validatePublication(record);
fs.writeFileSync(
  values.output ? path.resolve(values.output) : path.join(root, '.github/website-publication.json'),
  `${JSON.stringify(record, null, 2)}\n`,
);
console.log(
  `Recorded ${Object.keys(files).length} reviewed files. Review the diff; deployment remains separate.`,
);
