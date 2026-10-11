"""Network fixtures prove uncertainty persists and no request is retried."""
import json
from pathlib import Path
import socket
import tempfile
import time
import unittest
from unittest.mock import patch

from query_client_support import Fixture, TARGET
from revitthyme_client import QueryClient
from revitthyme_client.cli import target_from_file


class HttpTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.directory = Path(self.temporary.name)

    def tearDown(self):
        self.temporary.cleanup()

    def client(self, fixture, **options):
        return QueryClient(fixture.base, self.directory, **options)

    def test_named_status_inspect_and_mismatch_send_only_target(self):
        with Fixture() as fixture:
            client = self.client(fixture)
            status = client.query('revitthyme_status')
            self.assertEqual(status['state'], 'completed', status)
            inspect = client.query('timberfold_inspect', status['result']['target'])
            mismatch = client.query('timberfold_inspect', {'session': 'stale', 'document': 'stale'})
            self.assertEqual(status['state'], 'completed')
            self.assertEqual(inspect['result']['status'], 'inspected')
            self.assertEqual(mismatch['state'], 'target_mismatch')
            self.assertIsNone(client.pending())
            self.assertEqual(fixture.requests[:2], [('/revitthyme/status/', {}),
                                                   ('/revitthyme/timberfold/inspect/', {'target': TARGET})])
            self.assertEqual(status['request_id_scope'], 'local_client_correlation_only')
            self.assertNotEqual(status['request_id'], inspect['request_id'])

    def test_unknown_operations_or_unbound_inspection_never_contact_server(self):
        with Fixture() as fixture:
            client = self.client(fixture)
            for operation, target in (('execute_revit_code', None), ('timberfold_inspect', None),
                                      ('revitthyme_status', {'session': 'x', 'document': 'x', 'save': True})):
                self.assertEqual(client.query(operation, target)['state'], 'invalid_request')
            self.assertEqual(fixture.requests, [])

    def test_closed_endpoint_reports_no_dispatch_and_does_not_quarantine(self):
        with socket.socket() as probe:
            probe.bind(('127.0.0.1', 0))
            port = probe.getsockname()[1]
            # Bound but not listening: connection refusal, not a reused public port.
            client = QueryClient('http://127.0.0.1:{0}/revitthyme'.format(port), self.directory, timeout=0.15)
            result = client.query('revitthyme_status')
            self.assertEqual(result['state'], 'bridge_unavailable', result)
            self.assertIn('no request was sent', result['diagnostic'])
            self.assertIsNone(client.pending())

    def test_refused_connection_has_a_precise_no_dispatch_diagnostic(self):
        with Fixture() as fixture, patch('http.client.HTTPConnection.connect',
                                        side_effect=ConnectionRefusedError('Synthetic refusal')):
            client = self.client(fixture)
            result = client.query('revitthyme_status')
            self.assertEqual(result['state'], 'bridge_unavailable')
            self.assertIn('refused the connection; no request was sent', result['diagnostic'])
            self.assertIsNone(client.pending())
            self.assertEqual(fixture.requests, [])

    def test_timeout_still_running_blocks_new_clients_until_explicit_reconciliation(self):
        with Fixture('blocked') as fixture:
            result = self.client(fixture, timeout=0.08).query('revitthyme_status')
            self.assertEqual(result['state'], 'unknown_outcome')
            self.assertFalse(fixture.finished.is_set())
            client = self.client(fixture)
            blocked = client.query('revitthyme_status')
            self.assertEqual(blocked['state'], 'quarantined')
            self.assertEqual(len(fixture.requests), 1)
            request_id = result['request_id']
            with self.assertRaises(ValueError):
                client.reconcile('wrong-request-id', 'execution_ended', 'Fixture finished.')
            with self.assertRaises(ValueError):
                client.reconcile(request_id, 'execution_ended', '')
            self.assertEqual(client.pending()['request_id'], request_id)
            fixture.release.set()
            self.assertTrue(fixture.finished.wait(2))
            recovery = client.reconcile(request_id, 'execution_ended', 'Synthetic fixture completion event observed.')
            self.assertEqual(recovery['evidence_verification'], 'caller_supplied_unverified')
            self.assertIsNone(client.pending())
            self.assertEqual(client.query('revitthyme_status')['state'], 'completed')
            self.assertEqual(len(fixture.requests), 2)
            log = next(self.directory.glob('*.recoveries.jsonl')).read_text(encoding='utf-8')
            self.assertEqual(json.loads(log)['request_id'], request_id)

    def test_trickling_headers_and_body_cannot_extend_total_deadline(self):
        for mode in ('trickle_headers', 'trickle_body'):
            with self.subTest(mode=mode), Fixture(mode) as fixture:
                client = self.client(fixture, timeout=0.08)
                started = time.monotonic()
                result = client.query('revitthyme_status')
                self.assertEqual(result['state'], 'unknown_outcome')
                self.assertLess(time.monotonic() - started, 0.6)
                self.assertIsNotNone(client.pending())
                self.assertEqual(len(fixture.requests), 1)

    def test_invalid_crossed_and_redirect_responses_quarantine_without_retry(self):
        for mode in ('invalid', 'wrong_operation', 'crossed_target', 'redirect', 'wrong_type'):
            with self.subTest(mode=mode), Fixture(mode) as fixture:
                client = self.client(fixture)
                result = client.query('timberfold_inspect', TARGET)
                self.assertEqual(result['state'], 'invalid_response')
                self.assertEqual(client.query('revitthyme_status')['state'], 'quarantined')
                self.assertEqual(len(fixture.requests), 1)

    def test_truncated_declared_body_is_rejected_even_when_json_is_complete(self):
        # The fixture sends valid status JSON, advertises 100 extra bytes, then
        # closes. A syntactically valid payload is insufficient HTTP completion.
        with Fixture('truncated_body') as fixture:
            client = self.client(fixture)
            result = client.query('revitthyme_status')
            self.assertEqual(result['state'], 'invalid_response', result)
            self.assertIn('Content-Length', result['diagnostic'])
            self.assertEqual(client.pending()['request_id'], result['request_id'])
            self.assertEqual(client.query('revitthyme_status')['state'], 'quarantined')
            self.assertEqual(len(fixture.requests), 1)

    def test_environment_proxies_are_ignored(self):
        with Fixture() as fixture, patch.dict('os.environ', {
            'http_proxy': 'http://127.0.0.1:1', 'HTTP_PROXY': 'http://127.0.0.1:1',
            'no_proxy': '', 'NO_PROXY': '',
        }):
            self.assertEqual(self.client(fixture).query('revitthyme_status')['state'], 'completed')
            self.assertEqual(len(fixture.requests), 1)

    def test_corrupt_journal_is_preserved_and_never_sent(self):
        with Fixture() as fixture:
            client = self.client(fixture)
            with client._state() as state:
                state.pending_path.write_text('corrupt', encoding='utf-8')
            self.assertEqual(client.query('revitthyme_status')['state'], 'local_state_error')
            self.assertEqual(fixture.requests, [])
            self.assertEqual(next(self.directory.glob('*.pending.json')).read_text(), 'corrupt')

    def test_target_file_accepts_only_successful_client_status_envelope(self):
        with Fixture() as fixture:
            value = self.client(fixture).query('revitthyme_status')
            path = self.directory / 'status.json'
            path.write_text(json.dumps(value), encoding='utf-8')
            self.assertEqual(target_from_file(path), TARGET)
            with self.assertRaises(ValueError):
                target_from_file(path, 'http://127.0.0.1:1/revitthyme')
            value['result']['effects'] = ['write_model']
            path.write_text(json.dumps(value), encoding='utf-8')
            with self.assertRaises(ValueError):
                target_from_file(path)
            for change in ({'state': 'unknown_outcome'}, {'operation': 'timberfold_inspect'}):
                path.write_text(json.dumps({**value, **change}), encoding='utf-8')
                with self.assertRaises(ValueError):
                    target_from_file(path)


if __name__ == '__main__':
    unittest.main()
