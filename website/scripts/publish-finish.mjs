import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { root } from './content.mjs';
import { digest, validatePublication } from './publication.mjs';
import { approved, currentBaseline, saveRun, releaseDeployLock } from './publish-state.mjs';
import { git, gh, canonical, readJson, writeJson, liveCheck } from './publish-tools.mjs';

export function finish(
  state,
  {
    checkBaseline = currentBaseline,
    verify = liveCheck,
    gitCommand = git,
    ghCommand = gh,
    release = releaseDeployLock,
  } = {},
) {
  approved(state);
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
        'state,mergeCommit',
      ]),
    );
    if (pr.state !== 'MERGED') return false;
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
      ['commit', '-m', `Record approved website version ${state.saved.number}`],
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
  if (!baseline.pr) {
    const existing = JSON.parse(
      ghCommand([
        'pr',
        'list',
        '--repo',
        'scwlkr/RevitThyme',
        '--head',
        baseline.branch,
        '--state',
        'all',
        '--json',
        'url,state',
      ]),
    );
    assert.ok(existing.length <= 1, 'Multiple baseline PRs require reconciliation');
    if (existing.length) {
      assert.notEqual(existing[0].state, 'CLOSED', 'Baseline PR was closed without merging');
      baseline.pr = existing[0].url;
    } else {
      const body = path.join(state.paths.run, 'baseline-pr.md');
      fs.writeFileSync(
        body,
        `Record separately owner-approved Site version ${state.saved.number}.\n\nSource: ${state.candidate.sourceCommit}\nCandidate: ${state.candidateKey}\nHosting source: ${state.package.commit}\n\nAll ${Object.keys(record.files).length} public files and managed GitHub settings passed live verification before this baseline-only PR. Expected hashes come from the frozen local candidate.\n`,
      );
      baseline.pr = ghCommand([
        'pr',
        'create',
        '--repo',
        'scwlkr/RevitThyme',
        '--base',
        'master',
        '--head',
        baseline.branch,
        '--title',
        `Record approved website version ${state.saved.number}`,
        '--body-file',
        body,
      ]);
    }
    saveRun(state);
  }
  if (mergedReceipt()) return;
  console.log(`Baseline PR: ${baseline.pr}. Waiting for CI before merge.`);
  ghCommand([
    'pr',
    'checks',
    baseline.pr,
    '--repo',
    'scwlkr/RevitThyme',
    '--watch',
    '--interval',
    '15',
  ]);
  const checks = JSON.parse(
    ghCommand([
      'pr',
      'view',
      baseline.pr,
      '--repo',
      'scwlkr/RevitThyme',
      '--json',
      'headRefOid,statusCheckRollup',
    ]),
  );
  assert.equal(checks.headRefOid, baseline.head, 'Baseline PR head changed');
  assert.ok(
    checks.statusCheckRollup.length > 0 &&
      checks.statusCheckRollup.every((check) => check.conclusion === 'SUCCESS'),
    'All baseline CI checks must pass',
  );
  checkBaseline(state);
  verify(candidateFile);
  ghCommand([
    'pr',
    'merge',
    baseline.pr,
    '--repo',
    'scwlkr/RevitThyme',
    '--squash',
    '--match-head-commit',
    baseline.head,
  ]);
  assert.ok(mergedReceipt(), 'Merge is not confirmed; resume finish to reconcile');
}
