import fs from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { readJson } from './publish-tools.mjs';
import { prepare } from './publish-prepare.mjs';
import { loadRun, status, approve, stage, packageRun, intent, accept } from './publish-state.mjs';
import { finish } from './publish-finish.mjs';

const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: {
    help: { type: 'boolean' },
    ...Object.fromEntries(
      ['run', 'source', 'approval', 'key', 'evidence', 'receipt', 'operation'].map((key) => [
        key,
        { type: 'string' },
      ]),
    ),
  },
});
const action = positionals[0];
if (values.help || !action || action === 'help') {
  console.log(`Use $publish-website in Codex for the complete native Sites workflow.\n
npm run publication -- prepare --run ABSOLUTE_NEW_DIRECTORY --source FULL_SHA --approval SOURCE_PR_URL
npm run publication -- status --run RUN
npm run publication -- approve --run RUN --key CANDIDATE_DIGEST --evidence DIRECT_OWNER_APPROVAL
npm run publication -- stage --run RUN
npm run publication -- package --run RUN --receipt OFFICIAL_HELPER_JSON
npm run publication -- intent --run RUN --operation save|deploy
npm run publication -- accept --run RUN --operation save|deploy|status --receipt NATIVE_RESULT_JSON
npm run publication -- finish --run RUN

Preparation never publishes. Approval records a trusted human decision; this local journal is not an authentication service.
Codex performs native Sites calls using the project skill, then finish verifies production and creates/merges the baseline-only PR.
Resume the same run after interruption. saving/deploying mean reconcile, not retry. No credentials belong in a run receipt.`);
} else if (action === 'prepare') {
  console.log(JSON.stringify(status(prepare(values)), null, 2));
} else {
  const state = loadRun(values.run ?? '');
  if (action === 'status') console.log(JSON.stringify(status(state), null, 2));
  else {
    const lock = path.join(state.paths.run, 'command.lock');
    if (fs.existsSync(lock)) {
      const previous = readJson(lock);
      let alive = true;
      try {
        process.kill(previous.pid, 0);
      } catch (error) {
        if (error.code === 'ESRCH') alive = false;
        else throw error;
      }
      if (alive) throw new Error('A publication command is still running; wait for that process.');
      fs.unlinkSync(lock); // Dead command only; pending native intent still requires reconciliation.
    }
    fs.writeFileSync(lock, JSON.stringify({ pid: process.pid }), { flag: 'wx' });
    try {
      let result;
      if (action === 'approve') approve(state, values.key, values.evidence);
      else if (action === 'stage') stage(state);
      else if (action === 'package') packageRun(state, readJson(values.receipt));
      else if (action === 'intent') result = intent(state, values.operation);
      else if (action === 'accept') {
        const receipt = readJson(values.receipt);
        if (receipt.isError)
          throw new Error('Native operation failed; reconcile the pending operation');
        accept(state, values.operation, receipt.structuredContent ?? receipt);
      } else if (action === 'finish') finish(state);
      else throw new Error(`Unknown publication action: ${action}`);
      console.log(JSON.stringify(result ?? status(state), null, 2));
    } finally {
      fs.unlinkSync(lock);
    }
  }
}
