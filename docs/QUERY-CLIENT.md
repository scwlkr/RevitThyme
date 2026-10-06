# Local named query client

From a source checkout, use `./project query-revit` (`project.cmd query-revit` on Windows) as the repository entry point. Pass `-- --help` for the underlying client help. The direct Python commands below expose the same implementation.

The CPython standard-library client invokes only `revitthyme_status` and
`timberfold_inspect` through the existing loopback Routes. It provides local
request correlation, cooperating-process serialization and persistent quarantine
after an uncertain response. It is a source-checkout developer tool, outside the
extension ZIP. The extension remains IronPython compatible.

```sh
python3 scripts/query-revit.py status > artifacts/status.json
python3 scripts/query-revit.py inspect --target-file artifacts/status.json
```

Create `artifacts` first. On Windows use `python` instead of `python3`. Every
command prints JSON and returns zero only for successful status/inspection,
an idle journal, or an accepted local reconciliation record. Other outcomes return
one. An inspection requires a successful status envelope, preserving the exact
session/document target. A document change or reload requires a fresh status.
Neither command saves, synchronizes or modifies a document.

`--base` accepts only `http://127.0.0.1:<port>/revitthyme`; the default port is
48884. The transport ignores proxy environment variables, refuses redirects and
does not retry. The total HTTP deadline (`--timeout`, default 20 seconds) is
bounded to 0.05–120 seconds, including response headers and body; responses are
limited to 2 MiB. The lock wait (`--lock-wait`, default 5 seconds) is bounded to
0–60 seconds. JSON validation checks the operation, target, schema, declared
read-only effects, empty changed IDs and operation-specific data.

Validation requires complete HTTP framing: an early EOF before the declared body length leaves the request quarantined, even if the received bytes form valid JSON.

## Serialization and quarantine

All cooperating callers for an endpoint must use the same state directory. The
default is `%LOCALAPPDATA%/RevitThyme/query-client` on Windows and
`~/.cache/RevitThyme/query-client` elsewhere. `--state-directory` overrides it.
The client uses a persistent lock file with `msvcrt` on Windows or `flock` on
Unix. It holds the lock through dispatch, complete response read, independent
validation and journal removal. Use a local filesystem; network filesystem lock
semantics are outside this contract. Never remove an active lock file or switch
directories to bypass a busy or quarantined endpoint.

Before dispatch, the client atomically persists and flushes a pending journal
containing a UUID, operation, endpoint and UTC timestamp. The UUID stays local;
Routes receives only `{}` or `{"target": ...}`. It is **not** a server job ID,
idempotency key, authentication token or evidence of exactly-once execution.

| Client state | Meaning |
| --- | --- |
| `completed` | A validated operation result arrived; inspect its server status. |
| `target_mismatch` | The server explicitly rejected the stale target; rediscover it. |
| `busy` | Lock wait expired; this caller sent no request. |
| `bridge_unavailable` | Connection failed before dispatch; no request was sent. |
| `unknown_outcome` | Transport failed or timed out; execution may still be running. |
| `invalid_response` | Response cannot establish the named request's completion. |
| `quarantined` | A pending journal blocks all new queries, including status. |
| `local_state_error` | Local persistence failed or the journal is corrupt; investigate. |

Timeouts, malformed/crossed responses and caller crashes leave the journal in
place. OS lock release or journal age does not establish that Revit execution
ended. A failed connection can safely clear its own new journal because no POST
was dispatched. There is no automatic retry, expiration, cancellation or stale
lock reclamation.

## Explicit reconciliation

Inspect local state without issuing a Routes request:

```sh
python3 scripts/query-revit.py pending
```

Before clearing quarantine, obtain independent host evidence that execution has
ended, or perform a normal host restart and confirm a new session. Routes has no
job-status operation, and this client cannot query through its quarantine to
prove recovery. Record the pending UUID and the evidence:

```sh
python3 scripts/query-revit.py reconcile --request-id <pending-uuid> \
  --basis host_restarted --evidence "Normal restart confirmed independently; new session observed."
```

Alternatively use `--basis execution_ended` with independent completion evidence.
The matching ID and nonempty evidence are mandatory. The recovery log records
`evidence_verification: caller_supplied_unverified`: this command does not itself
verify server completion. Reconciliation records evidence and permits the next
query; it never resends the previous request. Rediscover status before inspection.
Preserve corrupt journals for manual investigation instead of deleting them.

## Library and verification

Add `tools/query-client` to the CPython import path and import
`QueryClient` from `revitthyme_client`. `query(operation, target=None)` returns a
client envelope with `request_id`, `state`, timing and a validated `result` when
available. `pending()` and `reconcile(request_id, basis, evidence)` operate only
on local state. This is a small seam for future named clients, not an MCP server.

```sh
python3 -m unittest discover -s tests -p 'test_query_client*.py' -v
```

Tests use synthetic loopback servers on random ports and disposable data. They
cover real subprocess serialization through the response body, bounded busy waits,
caller crashes, timeout while a fixture still runs, quarantine/reconciliation,
target mismatches, malformed envelopes, proxies, redirects and trickling input.
They do not query a live Revit model. Mac passes do not qualify Windows byte locks,
the installed pyRevit HTTP response or Revit execution/reload behavior. Unrelated
Routes clients and the development bridge can bypass this cooperative lock;
concurrent clients and repeated reload remain unqualified.
