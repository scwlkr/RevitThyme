import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { root } from './content.mjs';
import { publicationFiles } from './publication.mjs';
import { config, presentationHash, baselineHash, candidateKey, saveRun } from './publish-state.mjs';
import { command, git, gh, npm, writeJson, liveCheck } from './publish-tools.mjs';

export function prepare(options) {
  const run = options.run;
  assert.ok(
    path.isAbsolute(run ?? ''),
    '--run must be an absolute new directory outside the repository',
  );
  assert.ok(!fs.existsSync(run), 'Run already exists; use status/resume or choose a new directory');
  assert.match(options.source ?? '', /^[a-f0-9]{40}$/, '--source must be a full commit SHA');
  assert.match(
    options.approval ?? '',
    /^https:\/\/github.com\/scwlkr\/RevitThyme\/pull\/\d+$/,
    '--approval must link the source review PR',
  );
  assert.equal(
    git(['status', '--porcelain', '--untracked-files=no']),
    '',
    'Start with a clean tracked source tree',
  );
  git(['fetch', 'origin', 'master']);
  git(['merge-base', '--is-ancestor', options.source, 'origin/master']);
  const review = JSON.parse(
    gh([
      'pr',
      'view',
      options.approval,
      '--repo',
      'scwlkr/RevitThyme',
      '--json',
      'state,mergeCommit',
    ]),
  );
  assert.equal(review.state, 'MERGED', 'Source review PR must be merged');
  git(['merge-base', '--is-ancestor', review.mergeCommit.oid, options.source]);
  fs.mkdirSync(path.dirname(run), { recursive: true });
  const relative = path.relative(
    fs.realpathSync(root),
    path.join(fs.realpathSync(path.dirname(run)), path.basename(run)),
  );
  assert.ok(
    relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative),
    'Run directory must be outside the source repository',
  );
  fs.mkdirSync(run);
  const checkout = path.join(run, 'checkout');
  fs.mkdirSync(checkout);
  git(['archive', '--format=tar', '--output', path.join(run, 'source.tar'), options.source]);
  command('tar', ['-xf', 'source.tar', '-C', checkout], run);
  npm(['ci', '--ignore-scripts', '--prefix', 'website'], checkout);
  for (const script of ['build', 'check', 'check:release'])
    npm(['run', script, '--prefix', 'website'], checkout);
  const dist = path.join(run, 'dist');
  fs.cpSync(path.join(checkout, 'website/build'), dist, { recursive: true });
  writeJson(path.join(dist, 'deployment-source.json'), {
    repository: 'https://github.com/scwlkr/RevitThyme',
    commit: options.source,
    pull_request: options.approval,
  });
  const host = config('website-hosting.json');
  const prior = JSON.parse(git(['show', 'origin/master:.github/website-publication.json']));
  writeJson(path.join(run, 'prior-baseline.json'), prior);
  liveCheck(path.join(run, 'prior-baseline.json'));
  const candidate = {
    sourceCommit: options.source,
    approval: options.approval,
    projectId: host.projectId,
    origin: host.origin,
    audience: host.audience,
    priorBaselineHash: baselineHash(prior),
    presentationHash: presentationHash(),
    files: publicationFiles(dist),
  };
  const state = {
    schema: 1,
    phase: 'awaiting-approval',
    candidate,
    candidateKey: candidateKey(candidate),
    paths: {
      run: path.resolve(run),
      checkout,
      dist,
      site: path.join(run, 'site'),
      archive: path.join(run, 'site.tar.gz'),
    },
  };
  saveRun(state);
  return state;
}
