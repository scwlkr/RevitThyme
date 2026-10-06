"""Small named read-only client; request identity belongs to this client only."""
from datetime import datetime, timezone
import http.client
import math
import time
import uuid

from .protocol import OPERATIONS, InvalidResponse, endpoint, validate_response, validate_target
from .state import LockBusy, State, default_state_directory
from .transport import BridgeUnavailable, post

BASE = 'http://127.0.0.1:48884/revitthyme'


def timestamp():
    return datetime.now(timezone.utc).isoformat()


def duration(value, lower, upper, name):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or not lower <= value <= upper:
        raise ValueError('{0} must be between {1} and {2} seconds.'.format(name, lower, upper))
    return value


class QueryClient:
    def __init__(self, base=BASE, state_directory=None, timeout=20, lock_wait=5):
        self.base, self.port = endpoint(base)
        self.directory = state_directory if state_directory is not None else default_state_directory()
        self.timeout = duration(timeout, 0.05, 120, 'timeout')
        self.lock_wait = duration(lock_wait, 0, 60, 'lock_wait')

    def _state(self):
        return State(self.directory, self.base, self.lock_wait)

    def query(self, operation, target=None):
        started = time.monotonic()
        envelope = {'client_schema_version': 1, 'request_id': str(uuid.uuid4()),
                    'operation': operation, 'endpoint': self.base,
                    'request_id_scope': 'local_client_correlation_only'}

        def finish(state, diagnostic=None, **values):
            envelope.update(state=state, elapsed_ms=round((time.monotonic() - started) * 1000), **values)
            if diagnostic:
                envelope['diagnostic'] = diagnostic
            return envelope

        try:
            if operation not in OPERATIONS:
                raise ValueError('Only revitthyme_status and timberfold_inspect are available.')
            if operation == 'timberfold_inspect' and target is None:
                raise ValueError('timberfold_inspect requires the target discovered by status.')
            target = validate_target(target) if target is not None else None
        except (ValueError, TypeError) as error:
            return finish('invalid_request', str(error))
        try:
            with self._state() as state:
                pending = state.pending()
                if pending is not None:
                    return finish('quarantined', 'A prior request may still be executing. Reconcile independently before another query.',
                                  pending_request=pending)
                pending = {'schema_version': 1, 'request_id': envelope['request_id'],
                           'operation': operation, 'endpoint': self.base, 'started_at': timestamp()}
                state.begin(pending)
                parameters = {} if target is None else {'target': target}
                try:
                    raw = post(self.port, OPERATIONS[operation], parameters, self.timeout)
                    result = validate_response(raw, operation, target)
                except BridgeUnavailable as error:
                    state.clear()
                    return finish('bridge_unavailable', str(error))
                except (InvalidResponse, UnicodeError) as error:
                    return finish('invalid_response', str(error) + ' Endpoint quarantined; completion is unconfirmed.',
                                  pending_request=pending)
                except (OSError, http.client.HTTPException) as error:
                    return finish('unknown_outcome', '{0}: {1}. Request may still execute; endpoint quarantined, no retry was sent.'.format(
                        type(error).__name__, error), pending_request=pending)
                state.clear()
                return finish('target_mismatch' if result['status'] == 'target_mismatch' else 'completed', result=result)
        except LockBusy as error:
            return finish('busy', str(error) + ' No request was sent.')
        except (OSError, ValueError) as error:
            return finish('local_state_error', str(error) + ' No automatic recovery is allowed.')

    def pending(self):
        """Inspect local quarantine without contacting Routes."""
        with self._state() as state:
            return state.pending()

    def reconcile(self, request_id, basis, evidence):
        """Record caller-supplied evidence; does not verify host execution itself."""
        if basis not in {'host_restarted', 'execution_ended'} or not isinstance(evidence, str) or not evidence.strip():
            raise ValueError('Supply basis host_restarted or execution_ended and independent host evidence.')
        with self._state() as state:
            pending = state.pending()
            if pending is None or pending['request_id'] != request_id:
                raise ValueError('Reconciliation request ID must match the pending journal.')
            record = {'client_schema_version': 1, 'state': 'reconciled', 'request_id': request_id,
                      'endpoint': self.base, 'operation': pending['operation'], 'basis': basis,
                      'evidence': evidence.strip(), 'evidence_verification': 'caller_supplied_unverified',
                      'reconciled_at': timestamp(),
                      'diagnostic': 'Caller recorded independent recovery evidence. Rediscover target before inspection.'}
            state.record_recovery(record)
            return record
