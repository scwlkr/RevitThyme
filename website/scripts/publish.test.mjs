import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { digest, publicationFiles } from './publication.mjs';
import { command, writeJson, readJson } from './publish-tools.mjs';
import {
  candidateKey,
  presentationHash,
  approve,
  stage,
  packageRun,
  intent,
  accept,
  loadRun,
} from './publish-state.mjs';
import { finish } from './publish-finish.mjs';

function fixture(t) {
  const run = fs.mkdtempSync(path.join(os.tmpdir(), 'revitthyme-publish-test-'));
  // Remove only the exact newly allocated fixture, never a supplied path.
  t.after(() => fs.rmSync(run, { recursive: true, force: true }));
  const dist = path.join(run, 'dist');
  fs.mkdirSync(dist);
  for (const name of [
    'index.html',
    'content-manifest.json',
    'install',
    'install.ps1',
    'llms.txt',
    'llms-full.txt',
  ])
    fs.writeFileSync(path.join(dist, name), 'reviewed');
  writeJson(path.join(dist, 'deployment-source.json'), {
    repository: 'https://github.com/scwlkr/RevitThyme',
    commit: 'a'.repeat(40),
  });
  const candidate = {
    sourceCommit: 'a'.repeat(40),
    approval: 'https://github.com/scwlkr/RevitThyme/pull/35',
    projectId: 'appgprj_6ac4df23af808191aaa3703281ec0bff',
    origin: 'https://revitthyme.wlkrlabs.com',
    audience: 'public',
    priorBaselineHash: 'd'.repeat(64),
    presentationHash: presentationHash(),
    files: publicationFiles(dist),
  };
  const state = {
    schema: 1,
    phase: 'awaiting-approval',
    candidate,
    candidateKey: candidateKey(candidate),
    paths: { run, dist, site: path.join(run, 'site'), archive: path.join(run, 'site.tar.gz') },
  };
  writeJson(path.join(run, 'state.json'), state);
  return state;
}
function packed(t) {
  const state = fixture(t);
  approve(
    state,
    state.candidateKey,
    'Owner explicitly approves this candidate, deployment and verified baseline merge',
  );
  fs.mkdirSync(state.paths.site);
  fs.cpSync(state.paths.dist, path.join(state.paths.site, 'dist'), { recursive: true });
  fs.mkdirSync(path.join(state.paths.site, '.openai'));
  writeJson(path.join(state.paths.site, '.openai/hosting.json'), {
    project_id: state.candidate.projectId,
    static: { directory: 'dist' },
  });
  const source = path.join(state.paths.run, 'archive-source');
  fs.mkdirSync(source);
  fs.cpSync(state.paths.site, source, { recursive: true });
  fs.mkdirSync(path.join(source, 'dist/.openai'));
  fs.copyFileSync(
    path.join(source, '.openai/hosting.json'),
    path.join(source, 'dist/.openai/hosting.json'),
  );
  command('tar', ['-cf', 'site.tar.gz', '-C', 'archive-source', 'dist'], state.paths.run);
  const git = (args) => command('git', args, state.paths.site);
  git(['init']);
  git(['add', '.']);
  git([
    '-c',
    'user.name=Publication Test',
    '-c',
    'user.email=test@example.invalid',
    'commit',
    '-m',
    'fixture',
  ]);
  packageRun(state, {
    project_id: state.candidate.projectId,
    checkout_path: state.paths.site,
    archive: state.paths.archive,
    commit_sha: git(['rev-parse', 'HEAD']),
  });
  return state;
}
const offline = { checkBaseline() {}, verify() {}, takeLock() {}, releaseLock() {} };
function deployed(t) {
  const state = packed(t);
  intent(state, 'save', offline);
  accept(state, 'save', {
    project_id: state.candidate.projectId,
    id: 'saved-version',
    version_number: 9,
    source: { commit_sha: state.package.commit },
  });
  intent(state, 'deploy', offline);
  accept(state, 'deploy', {
    project_id: state.candidate.projectId,
    id: 'deployment',
    version_id: 'saved-version',
    status: 'succeeded',
    url: state.candidate.origin,
  });
  return state;
}

test('preparation has no publication authorization; a wrong approval digest fails', (t) => {
  const state = fixture(t);
  assert.throws(() => intent(state, 'save', offline), /approval is required/);
  assert.throws(() => approve(state, '0'.repeat(64), 'yes'), /exact candidate/);
  assert.equal(loadRun(state.paths.run).phase, 'awaiting-approval');
});
test('changed reviewed bytes invalidate approval instead of silently rebuilding', (t) => {
  const state = fixture(t);
  fs.appendFileSync(path.join(state.paths.dist, 'install'), 'changed');
  assert.throws(() => approve(state, state.candidateKey, 'yes'), /bytes changed/);
});
test('packaging rejects a changed hosting identity or candidate source', (t) => {
  const state = packed(t);
  assert.throws(() => packageRun(state, { project_id: 'other' }));
  fs.appendFileSync(path.join(state.paths.site, 'dist/install'), 'changed');
  assert.throws(() => intent(state, 'save', offline), /Packaged source changed/);
});
test('staging replaces only static files and preserves the old static tree and manifest', (t) => {
  const state = packed(t);
  state.phase = 'approved';
  fs.writeFileSync(path.join(state.paths.site, 'dist/install'), 'old deployment');
  command('git', ['add', '.'], state.paths.site);
  command(
    'git',
    [
      '-c',
      'user.name=Publication Test',
      '-c',
      'user.email=test@example.invalid',
      'commit',
      '-m',
      'old deployment',
    ],
    state.paths.site,
  );
  const manifest = fs.readFileSync(path.join(state.paths.site, '.openai/hosting.json'), 'utf8');
  stage(state);
  assert.equal(fs.readFileSync(path.join(state.paths.site, 'dist/install'), 'utf8'), 'reviewed');
  assert.equal(
    fs.readFileSync(path.join(state.paths.site, '.openai/hosting.json'), 'utf8'),
    manifest,
  );
  const backup = fs
    .readdirSync(state.paths.run)
    .find((name) => name.startsWith('site-static-backup-'));
  assert.equal(
    fs.readFileSync(path.join(state.paths.run, backup, 'install'), 'utf8'),
    'old deployment',
  );
});
test('a stale/wrong archive is rejected before it can be saved or deployed', (t) => {
  const state = packed(t);
  fs.appendFileSync(path.join(state.paths.run, 'archive-source/dist/install'), 'wrong archive');
  command('tar', ['-cf', 'site.tar.gz', '-C', 'archive-source', 'dist'], state.paths.run);
  assert.throws(
    () =>
      packageRun(state, {
        project_id: state.candidate.projectId,
        checkout_path: state.paths.site,
        archive: state.paths.archive,
        commit_sha: state.package.commit,
      }),
    /Archive bytes differ/,
  );
});
test('changed archive and concurrent baseline block a save before any native intent', (t) => {
  const state = packed(t);
  assert.throws(
    () =>
      intent(state, 'save', {
        ...offline,
        checkBaseline() {
          throw new Error('concurrent baseline');
        },
      }),
    /concurrent baseline/,
  );
  assert.equal(loadRun(state.paths.run).phase, 'packaged');
  fs.appendFileSync(state.paths.archive, 'changed');
  assert.throws(() => intent(state, 'save', offline), /archive changed/);
});
test('save/deploy intent survives interruption and prevents a second mutation', (t) => {
  const state = packed(t);
  const args = intent(state, 'save', offline);
  assert.equal(args.archive, state.paths.archive);
  const resumed = loadRun(state.paths.run);
  assert.equal(resumed.phase, 'saving');
  assert.throws(() => intent(resumed, 'save', offline), /Reconcile/);
  assert.throws(() =>
    accept(resumed, 'save', {
      project_id: state.candidate.projectId,
      id: 'wrong',
      version_number: 9,
      source: { commit_sha: '0'.repeat(40) },
    }),
  );
  accept(resumed, 'save', {
    project_id: state.candidate.projectId,
    id: 'version',
    version_number: 9,
    source: { commit_sha: state.package.commit },
  });
  intent(resumed, 'deploy', offline);
  assert.equal(loadRun(state.paths.run).phase, 'deploying');
  assert.throws(() => intent(resumed, 'deploy', offline), /Reconcile/);
  accept(resumed, 'deploy', {
    project_id: state.candidate.projectId,
    id: 'deployment',
    version_id: 'version',
    status: 'building',
  });
  assert.equal(resumed.phase, 'pending');
  assert.throws(
    () =>
      accept(resumed, 'status', {
        project_id: state.candidate.projectId,
        id: 'another-deployment',
        version_id: 'version',
        status: 'succeeded',
      }),
    /ID changed/,
  );
});
test('failed live verification creates no baseline checkout or PR', (t) => {
  const state = deployed(t);
  assert.throws(
    () =>
      finish(state, {
        checkBaseline() {},
        verify() {
          throw new Error('Live file drift');
        },
      }),
    /Live file drift/,
  );
  assert.equal(state.phase, 'deployed');
  assert.equal(fs.existsSync(path.join(state.paths.run, 'baseline-checkout')), false);
});
test('verified workflow creates one baseline-only PR, checks CI and merges; resume is idempotent', (t) => {
  const state = deployed(t);
  let merged = false,
    verifies = 0,
    creates = 0,
    releases = 0;
  const head = 'b'.repeat(40),
    merge = 'c'.repeat(40);
  const tools = {
    checkBaseline() {},
    verify() {
      verifies++;
    },
    release() {
      releases++;
    },
    gitCommand(args, cwd) {
      if (args[0] === 'worktree') {
        fs.mkdirSync(path.join(args[4], '.github'), { recursive: true });
        return '';
      }
      if (args[0] === 'branch') return state.baseline.branch;
      if (args[0] === 'status') return 'M .github/website-publication.json';
      if (args[0] === 'diff') return '.github/website-publication.json';
      if (args[0] === 'rev-parse') return head;
      if (args[0] === 'show')
        return fs.readFileSync(path.join(state.paths.run, 'candidate-baseline.json'), 'utf8');
      assert.ok(['fetch', 'add', 'commit', 'push'].includes(args[0]), `${args[0]} ${cwd}`);
      return '';
    },
    ghCommand(args) {
      if (args[1] === 'list') return '[]';
      if (args[1] === 'create') {
        creates++;
        return 'https://github.com/scwlkr/RevitThyme/pull/99';
      }
      if (args[1] === 'view' && args.at(-1).includes('statusCheckRollup'))
        return JSON.stringify({ headRefOid: head, statusCheckRollup: [{ conclusion: 'SUCCESS' }] });
      if (args[1] === 'view')
        return JSON.stringify({ state: merged ? 'MERGED' : 'OPEN', mergeCommit: { oid: merge } });
      if (args[1] === 'merge') merged = true;
      else assert.equal(args[1], 'checks');
      return '';
    },
  };
  finish(state, tools);
  assert.equal(state.phase, 'complete');
  assert.equal(creates, 1);
  assert.equal(verifies, 2);
  const baseline = readJson(path.join(state.baseline.checkout, '.github/website-publication.json'));
  assert.equal(baseline.files['/install'], digest('reviewed'));
  finish(loadRun(state.paths.run), tools);
  assert.equal(creates, 1);
  assert.equal(releases, 2);
});
