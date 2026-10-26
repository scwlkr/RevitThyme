import { test } from 'node:test';
import assert from 'node:assert/strict';
import { githubEnvironment } from './publish-tools.mjs';
import { root } from './content.mjs';

test('GitHub environment preserves existing tokens without credential commands', () => {
  for (const token of ['GH_TOKEN', 'GITHUB_TOKEN']) {
    const original = { PATH: 'fixture-path', [token]: 'fixture-value' };
    const result = githubEnvironment(original, () =>
      assert.fail('Existing tokens need no credential command'),
    );
    assert.deepEqual(result, original);
    assert.notEqual(result, original);
  }
});

test('GitHub CLI authentication avoids reading Git credentials', () => {
  const calls = [];
  assert.deepEqual(
    githubEnvironment({ PATH: 'fixture' }, (...args) => {
      calls.push(args);
      return '';
    }),
    { PATH: 'fixture' },
  );
  assert.deepEqual(calls, [['gh', ['auth', 'status']]]);
});

test('Git credentials are passed only in the child environment', () => {
  const original = { PATH: 'fixture' },
    calls = [];
  const result = githubEnvironment(original, (...args) => {
    calls.push(args);
    if (args[0] === 'gh') throw new Error('not authenticated');
    return 'username=fixture\r\npassword=fixture-value\r\n';
  });
  assert.deepEqual(original, { PATH: 'fixture' });
  assert.deepEqual(result, { PATH: 'fixture', GH_TOKEN: 'fixture-value' });
  assert.deepEqual(calls[1], [
    'git',
    ['credential', 'fill'],
    root,
    { input: 'protocol=https\nhost=github.com\n\n' },
  ]);
});

test('missing or empty passwords do not invent an environment token', () => {
  for (const credential of ['username=fixture\n', 'password=\n', 'other=password=fixture\n']) {
    const result = githubEnvironment({}, (executable) => {
      if (executable === 'gh') throw new Error('not authenticated');
      return credential;
    });
    assert.deepEqual(result, {});
  }
});

test('credential failures produce actionable text without exposing their output', () => {
  assert.throws(
    () =>
      githubEnvironment({}, () => {
        throw new Error('private fixture details');
      }),
    {
      message: 'Authenticate GitHub CLI or configure Git Credential Manager before publication.',
    },
  );
});
