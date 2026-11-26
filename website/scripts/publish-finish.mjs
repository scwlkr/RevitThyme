import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { root } from './content.mjs';
import { digest, validatePublication } from './publication.mjs';
import { frozen, currentBaseline, saveRun, releaseDeployLock } from './publish-state.mjs';
import { git, gh, canonical, readJson, writeJson, liveCheck } from './publish-tools.mjs';

export function finish(
  state,
  {
    baselinePr,
    checkBaseline = currentBaseline,
    verify = liveCheck,
    gitCommand = git,
    ghCommand = gh,
    release = releaseDeployLock,
  } = {},
) {
  frozen(state);
  assert.ok(
    ['deployed', 'baseline-pr', 'complete'].includes(state.phase),
    'Finish requires a confirmed successful deployment',
  );
  if (state.phase === 'complete') {
    release(state);
    return;
  }
  const record = {
    schema: 1,
    origin: state.candidate.origin,
    sourceCommit: state.candidate.sourceCommit,
    siteSourceCommit: state.package.commit,
    siteVersion: state.saved.number,
    approval: state.candidate.approval,
    files: state.candidate.files,
  };
  validatePublication(record);
  const candidateFile = path.join(state.paths.run, 'candidate-baseline.json');
  writeJson(candidateFile, record);
  // Reconcile a successful merge if the process stopped before its receipt was saved.
  function mergedReceipt() {
    if (!state.baseline?.pr) return false;
    const pr = JSON.parse(
      ghCommand([
        'pr',
        'view',
        state.baseline.pr,
        '--repo',
        'scwlkr/RevitThyme',
        '--json',
        'state,mergeCommit,headRefOid',
      ]),
    );
    if (pr.state !== 'MERGED') return false;
    assert.equal(pr.headRefOid, state.baseline.head, 'Baseline PR head changed');
    gitCommand(['fetch', 'origin', 'master']);
    const landed = JSON.parse(
      gitCommand(['show', `${pr.mergeCommit.oid}:.github/website-publication.json`]),
    );
    assert.equal(
      canonical(landed),
      canonical(record),
      'Merged baseline differs from the verified candidate',
    );
    state.baseline.mergeCommit = pr.mergeCommit.oid;
    state.phase = 'complete';
    saveRun(state);
    release(state);
    return true;
  }
  if (baselinePr) {
    assert.match(baselinePr, /^https:\/\/github.com\/scwlkr\/RevitThyme\/pull\/\d+$/);
    assert.ok(state.baseline?.head, 'Prepare the verified baseline branch first');
    const previous = state.baseline.pr;
    assert.ok(!previous || previous === baselinePr, 'Another baseline PR is already recorded');
    state.baseline.pr = baselinePr;
    verify(candidateFile);
    assert.ok(mergedReceipt(), 'Baseline PR must be merged before recording delivery');
    return;
  }
  if (mergedReceipt()) return;
  checkBaseline(state);
  verify(candidateFile); // No baseline write/PR is allowed before live verification.
  state.verifiedAt = new Date().toISOString();
  state.baseline ??= {
    branch: `codex/website-baseline-${state.candidateKey.slice(0, 12)}-${digest(state.paths.run).slice(0, 6)}`,
    checkout: path.join(state.paths.run, 'baseline-checkout'),
    base: gitCommand(['rev-parse', 'origin/master']),
  };
  state.phase = 'baseline-pr';
  saveRun(state);
  const baseline = state.baseline;
  if (!fs.existsSync(baseline.checkout))
    gitCommand(['worktree', 'add', '-b', baseline.branch, baseline.checkout, baseline.base]);
  assert.equal(gitCommand(['branch', '--show-current'], baseline.checkout), baseline.branch);
  fs.copyFileSync(candidateFile, path.join(baseline.checkout, '.github/website-publication.json'));
  assert.equal(
    canonical(readJson(path.join(baseline.checkout, '.github/website-publication.json'))),
    canonical(record),
  );
  const changed = gitCommand(['status', '--porcelain'], baseline.checkout);
  if (changed) {
    assert.equal(
      changed.trim(),
      'M .github/website-publication.json',
      'Baseline checkout contains unrelated edits',
    );
    gitCommand(['add', '.github/website-publication.json'], baseline.checkout);
    gitCommand(
      ['commit', '-m', `Record verified website version ${state.saved.number}`],
      baseline.checkout,
    );
  }
  assert.equal(
    gitCommand(['diff', '--name-only', baseline.base, 'HEAD'], baseline.checkout),
    '.github/website-publication.json',
    'Baseline PR must change exactly one file',
  );
  baseline.head = gitCommand(['rev-parse', 'HEAD'], baseline.checkout);
  saveRun(state);
  gitCommand(['push', '-u', 'origin', baseline.branch], baseline.checkout);
  console.log(
    `Open and merge the verified baseline branch ${baseline.branch}, then record its PR URL with --baseline-pr.`,
  );
}
