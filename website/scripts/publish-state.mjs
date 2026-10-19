import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { root } from './content.mjs';
import { digest, publicationFiles } from './publication.mjs';
import { canonical, readJson, writeJson, git, liveCheck } from './publish-tools.mjs';
import { verifyArchive } from './publish-archive.mjs';

export const config = (name) => readJson(path.join(root, '.github', name));
export const presentationHash = () =>
  digest(canonical([config('repository-presentation.json'), config('labels.json')]));
export const baselineHash = (record) => digest(canonical(record));
export const candidateKey = (candidate) => digest(canonical(candidate));
export function loadRun(run) {
  assert.ok(path.isAbsolute(run), '--run must be absolute');
  const state = readJson(path.join(run, 'state.json'));
  assert.equal(state.schema, 1);
  assert.equal(state.paths.run, path.resolve(run));
  return state;
}
export function saveRun(state) {
  writeJson(path.join(state.paths.run, 'state.json'), state);
}
export function frozen(state) {
  assert.equal(
    candidateKey(state.candidate),
    state.candidateKey,
    'Candidate metadata changed; prepare a new candidate',
  );
  assert.equal(
    canonical(publicationFiles(state.paths.dist)),
    canonical(state.candidate.files),
    'Candidate bytes changed; prepare again and obtain fresh approval',
  );
  const host = config('website-hosting.json');
  assert.equal(host.projectId, state.candidate.projectId, 'Hosting identity changed');
  assert.equal(host.origin, state.candidate.origin, 'Hosting domain changed');
  assert.equal(host.audience, state.candidate.audience, 'Hosting audience changed');
  assert.equal(
    presentationHash(),
    state.candidate.presentationHash,
    'GitHub expectations changed; prepare again',
  );
  if (state.approval)
    assert.equal(state.approval.key, state.candidateKey, 'Approval belongs to another candidate');
}
export function approved(state) {
  frozen(state);
  assert.ok(state.approval?.evidence, 'Owner approval is required before publishing');
}
export function currentBaseline(state) {
  git(['fetch', 'origin', 'master']);
  const remote = JSON.parse(git(['show', 'origin/master:.github/website-publication.json']));
  assert.equal(
    baselineHash(remote),
    state.candidate.priorBaselineHash,
    'Approved baseline changed concurrently; prepare a new candidate',
  );
}
export function approve(state, key, evidence) {
  frozen(state);
  assert.equal(state.phase, 'awaiting-approval');
  assert.equal(key, state.candidateKey, 'Approval key must identify this exact candidate');
  assert.ok(evidence?.trim(), 'Record the direct owner approval, not an inferred approval');
  state.approval = { key, evidence, recordedAt: new Date().toISOString() };
  state.phase = 'approved';
  saveRun(state);
}
export function packageRun(state, receipt) {
  approved(state);
  assert.ok(['approved', 'packaged'].includes(state.phase), 'Package only an approved candidate');
  assert.equal(receipt.project_id, state.candidate.projectId);
  assert.equal(path.resolve(receipt.checkout_path), state.paths.site);
  assert.equal(path.resolve(receipt.archive), state.paths.archive);
  assert.match(receipt.commit_sha, /^[a-f0-9]{40}$/);
  assert.equal(git(['rev-parse', 'HEAD'], state.paths.site), receipt.commit_sha);
  assert.equal(git(['status', '--porcelain'], state.paths.site), '', 'Sites source must be clean');
  const hosting = readJson(path.join(state.paths.site, '.openai/hosting.json'));
  assert.equal(hosting.project_id, state.candidate.projectId);
  assert.equal(hosting.static.directory, 'dist');
  assert.equal(
    canonical(publicationFiles(path.join(state.paths.site, 'dist'))),
    canonical(state.candidate.files),
    'Packaged source differs from the approved candidate',
  );
  verifyArchive(state);
  state.package = {
    commit: receipt.commit_sha,
    archiveHash: digest(fs.readFileSync(state.paths.archive)),
  };
  state.phase = 'packaged';
  saveRun(state);
}
export function stage(state) {
  approved(state);
  assert.equal(state.phase, 'approved', 'Stage only before sealing the package');
  const site = state.paths.site;
  const identity = (value) => (process.platform === 'win32' ? value.toLowerCase() : value);
  assert.equal(
    identity(fs.realpathSync(site)),
    identity(site),
    'Sites checkout path must not be a symlink',
  );
  const hosting = readJson(path.join(site, '.openai/hosting.json'));
  assert.equal(hosting.project_id, state.candidate.projectId);
  assert.equal(hosting.static.directory, 'dist');
  if (!state.stageStarted)
    assert.equal(git(['status', '--porcelain'], site), '', 'Open a clean Sites source repository');
  else
    assert.ok(
      git(['diff', '--name-only'], site)
        .split('\n')
        .every((file) => !file || file.startsWith('dist/')),
      'Unexpected Sites source edits',
    );
  state.stageStarted = true;
  saveRun(state);
  const destination = path.resolve(site, 'dist');
  assert.ok(
    destination.startsWith(`${state.paths.run}${path.sep}`),
    'Static staging must remain inside this run',
  );
  if (fs.existsSync(destination)) {
    const backup = path.join(state.paths.run, `site-static-backup-${Date.now()}`);
    assert.ok(path.resolve(backup).startsWith(`${state.paths.run}${path.sep}`));
    fs.renameSync(destination, backup);
  }
  fs.cpSync(state.paths.dist, destination, { recursive: true });
  assert.equal(canonical(publicationFiles(destination)), canonical(state.candidate.files));
  state.staged = true;
  saveRun(state);
}
export function packageIntact(state) {
  approved(state);
  assert.ok(state.package, 'An official Sites package receipt is required');
  assert.equal(
    digest(fs.readFileSync(state.paths.archive)),
    state.package.archiveHash,
    'Packaged archive changed',
  );
  assert.equal(
    canonical(publicationFiles(path.join(state.paths.site, 'dist'))),
    canonical(state.candidate.files),
    'Packaged source changed',
  );
}
function deployLockPath() {
  return path.join(
    path.resolve(root, git(['rev-parse', '--git-common-dir'])),
    'revitthyme-website-publication.lock',
  );
}
export function releaseDeployLock(state) {
  const file = deployLockPath();
  if (!fs.existsSync(file)) return;
  assert.equal(readJson(file).run, state.paths.run, 'Another publication owns the deployment lock');
  fs.unlinkSync(file);
}
function takeDeployLock(state) {
  const file = deployLockPath();
  try {
    fs.writeFileSync(file, JSON.stringify({ run: state.paths.run }), { flag: 'wx' });
  } catch (error) {
    if (error.code !== 'EEXIST') throw error;
    assert.equal(
      readJson(file).run,
      state.paths.run,
      'Another publication is active; resume that run before deploying',
    );
  }
}
export function intent(
  state,
  operation,
  {
    checkBaseline = currentBaseline,
    verify = liveCheck,
    takeLock = takeDeployLock,
    releaseLock = releaseDeployLock,
  } = {},
) {
  packageIntact(state);
  assert.equal(
    state.phase,
    operation === 'save' ? 'packaged' : 'saved',
    'Reconcile the existing operation before retrying',
  );
  assert.ok(['save', 'deploy'].includes(operation));
  if (operation === 'deploy') takeLock(state);
  try {
    checkBaseline(state);
    verify(path.join(state.paths.run, 'prior-baseline.json'));
  } catch (error) {
    if (operation === 'deploy') releaseLock(state);
    throw error;
  }
  state.phase = operation === 'save' ? 'saving' : 'deploying';
  saveRun(state); // Uncertain remote outcomes must be reconciled, never blindly repeated.
  return operation === 'save'
    ? {
        project_id: state.candidate.projectId,
        commit_sha: state.package.commit,
        archive: state.paths.archive,
      }
    : { project_id: state.candidate.projectId, version_id: state.saved.id };
}
export function accept(state, operation, value) {
  approved(state);
  assert.equal(value.project_id, state.candidate.projectId);
  assert.ok(typeof value.id === 'string' && value.id.length > 0);
  if (operation === 'save') {
    assert.ok(['saving', 'saved'].includes(state.phase));
    assert.equal(value.source.commit_sha, state.package.commit);
    assert.ok(Number.isSafeInteger(value.version_number) && value.version_number > 0);
    if (state.saved) assert.equal(value.id, state.saved.id, 'Saved version changed');
    state.saved = { id: value.id, number: value.version_number };
    state.phase = 'saved';
  } else {
    assert.ok(['deploy', 'status'].includes(operation));
    assert.ok(['deploying', 'pending', 'deployed', 'deployment-failed'].includes(state.phase));
    assert.equal(value.version_id, state.saved.id);
    if (state.deployment) assert.equal(value.id, state.deployment.id, 'Deployment ID changed');
    assert.ok(['pending', 'building', 'publishing', 'succeeded', 'failed'].includes(value.status));
    state.deployment = {
      id: value.id,
      status: value.status,
      url: value.url,
      failure: value.failure_message,
    };
    state.phase =
      value.status === 'succeeded'
        ? 'deployed'
        : value.status === 'failed'
          ? 'deployment-failed'
          : 'pending';
  }
  saveRun(state);
  if (state.phase === 'deployment-failed') releaseDeployLock(state);
}
export function status(state) {
  frozen(state);
  return {
    phase: state.phase,
    candidateKey: state.candidateKey,
    sourceCommit: state.candidate.sourceCommit,
    projectId: state.candidate.projectId,
    origin: state.candidate.origin,
    audience: state.candidate.audience,
    files: Object.keys(state.candidate.files).length,
    paths: state.paths,
    approval: state.approval,
    saved: state.saved,
    deployment: state.deployment,
    baseline: state.baseline,
    next: {
      'awaiting-approval':
        'Ask owner to approve this source and candidateKey, deployment and baseline PR/merge',
      approved: 'Open Sites source and package frozen dist with official helper',
      packaged: 'intent save',
      saving: 'Reconcile saved version; do not repeat save',
      saved: 'intent deploy',
      deploying: 'Reconcile deployment; do not repeat deploy',
      pending: 'Poll existing deployment ID',
      deployed: 'finish',
      'deployment-failed': 'Report failure; preserve prior baseline',
      'baseline-pr': 'finish',
      complete: 'done',
    }[state.phase],
  };
}
