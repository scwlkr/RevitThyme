import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { root } from './content.mjs';

export const readJson = (file) => JSON.parse(fs.readFileSync(file, 'utf8'));
export const canonical = (value) =>
  JSON.stringify(value, (_, item) =>
    item && typeof item === 'object' && !Array.isArray(item)
      ? Object.fromEntries(Object.entries(item).sort(([a], [b]) => a.localeCompare(b)))
      : item,
  );
export function command(executable, args, cwd = root, options = {}) {
  const output = execFileSync(executable, args, {
    cwd,
    encoding: 'utf8',
    stdio: ['pipe', 'pipe', 'pipe'],
    ...options,
  });
  return output?.trim() ?? '';
}
export const git = (args, cwd = root) => command('git', args, cwd);
export function npm(args, cwd = root) {
  const cli =
    process.env.npm_execpath ??
    path.join(path.dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js');
  command(process.execPath, [cli, ...args], cwd, { stdio: 'inherit' });
}
export function gh(args, cwd = root) {
  const env = { ...process.env };
  let loggedIn = false;
  if (!env.GH_TOKEN && !env.GITHUB_TOKEN) {
    try {
      command('gh', ['auth', 'status']);
      loggedIn = true;
    } catch {
      /* Try the existing Git credential next. */
    }
  }
  if (!env.GH_TOKEN && !env.GITHUB_TOKEN && !loggedIn) {
    // Reuse Git Credential Manager without persisting or printing its credential.
    let credential;
    try {
      credential = command('git', ['credential', 'fill'], root, {
        input: 'protocol=https\nhost=github.com\n\n',
      });
    } catch {
      throw new Error(
        'Authenticate GitHub CLI or configure Git Credential Manager before publication.',
      );
    }
    const token = credential
      .split('\n')
      .map((line) => line.replace(/\r$/, ''))
      .find((line) => line.startsWith('password='))
      ?.slice(9);
    if (token) env.GH_TOKEN = token;
  }
  return command('gh', args, cwd, { env });
}
export function liveCheck(publication) {
  command(
    process.execPath,
    [path.join(root, 'website/scripts/check-live.mjs'), '--publication', publication],
    root,
    { stdio: 'inherit' },
  );
}
export function writeJson(file, value) {
  fs.writeFileSync(`${file}.next`, `${JSON.stringify(value, null, 2)}\n`);
  fs.renameSync(`${file}.next`, file);
}
