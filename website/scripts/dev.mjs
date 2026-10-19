import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { website, root } from './content.mjs';

const generate = () =>
  new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [path.join(website, 'scripts/prepare-content.mjs')], {
      stdio: 'inherit',
    });
    child.on('exit', (code) =>
      code === 0 ? resolve() : reject(new Error(`Content generation exited ${code}`)),
    );
  });
let timer;
let pending = Promise.resolve();
const changed = () => {
  clearTimeout(timer);
  timer = setTimeout(() => {
    pending = pending.then(generate).catch((error) => console.error(error.message));
  }, 200);
};
for (const folder of ['docs', 'verification'])
  fs.watch(path.join(root, folder), { recursive: true }, changed);
fs.watch(root, (_, file) => {
  if (file?.endsWith('.md')) changed();
});
const child = spawn(
  process.execPath,
  [
    path.join(website, 'node_modules/@docusaurus/core/bin/docusaurus.mjs'),
    'start',
    '--host',
    '127.0.0.1',
    '--port',
    '3100',
    '--no-open',
    ...process.argv.slice(2),
  ],
  { stdio: 'inherit' },
);
child.on('exit', (code) => process.exit(code ?? 1));
for (const signal of ['SIGINT', 'SIGTERM'])
  process.on(signal, () => {
    child.kill(signal);
    process.exit();
  });
