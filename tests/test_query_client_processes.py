"""Real subprocess callers cooperate through one endpoint lock and journal."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest

from query_client_support import Fixture, ROOT


class ProcessTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.directory = Path(self.temporary.name)
        self.children = []

    def tearDown(self):
        for child in self.children:
            if child.poll() is None:
                child.kill()
            child.communicate(timeout=3)
        self.temporary.cleanup()

    def start(self, fixture, command='status', *arguments):
        child = subprocess.Popen([sys.executable, str(ROOT / 'scripts/query-revit.py'), command,
                                  '--base', fixture.base, '--state-directory', str(self.directory),
                                  *arguments], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        self.children.append(child)
        return child

    def collect(self, child):
        output, errors = child.communicate(timeout=5)
        self.assertEqual(errors, '')
        return child.returncode, json.loads(output)

    def test_independent_processes_serialize_through_entire_response_body(self):
        with Fixture('split_body') as fixture:
            first = self.start(fixture)
            self.assertTrue(fixture.received.wait(2))
            second = self.start(fixture)
            self.assertNotEqual(first.pid, second.pid)
            first_exit, first_result = self.collect(first)
            second_exit, second_result = self.collect(second)
            self.assertEqual((first_exit, second_exit), (0, 0), (first_result, second_result))
            self.assertEqual((first_result['state'], second_result['state']), ('completed', 'completed'))
            self.assertEqual(fixture.maximum, 1)
            self.assertEqual(len(fixture.requests), 2)
            self.assertNotEqual(first_result['request_id'], second_result['request_id'])

    def test_busy_wait_is_bounded_and_does_not_dispatch(self):
        with Fixture('blocked') as fixture:
            first = self.start(fixture)
            self.assertTrue(fixture.received.wait(2))
            started = time.monotonic()
            second = self.start(fixture, 'status', '--lock-wait', '0.08')
            exit_code, result = self.collect(second)
            self.assertEqual((exit_code, result['state']), (1, 'busy'))
            self.assertLess(time.monotonic() - started, 1)
            self.assertEqual(len(fixture.requests), 1)
            fixture.release.set()
            self.assertEqual(self.collect(first)[0], 0)

    def test_killed_caller_releases_lock_but_persistent_quarantine_blocks_dispatch(self):
        with Fixture('blocked') as fixture:
            first = self.start(fixture)
            self.assertTrue(fixture.received.wait(2))
            first.kill()
            first.communicate(timeout=3)
            exit_code, result = self.collect(self.start(fixture))
            self.assertEqual((exit_code, result['state']), (1, 'quarantined'))
            self.assertFalse(fixture.finished.is_set())
            self.assertEqual(len(fixture.requests), 1)
            request_id = result['pending_request']['request_id']
            pending_exit, pending = self.collect(self.start(fixture, 'pending'))
            self.assertEqual((pending_exit, pending['state']), (1, 'quarantined'))
            wrong_exit, _ = self.collect(self.start(fixture, 'reconcile', '--request-id', 'wrong',
                                                  '--basis', 'execution_ended', '--evidence', 'Fixture ended.'))
            self.assertEqual(wrong_exit, 1)
            fixture.release.set()
            self.assertTrue(fixture.finished.wait(2))
            recovery_exit, recovery = self.collect(self.start(
                fixture, 'reconcile', '--request-id', request_id, '--basis', 'execution_ended',
                '--evidence', 'Synthetic fixture completion event observed.'))
            self.assertEqual((recovery_exit, recovery['state']), (0, 'reconciled'))
            self.assertEqual(self.collect(self.start(fixture))[0], 0)
            self.assertEqual(len(fixture.requests), 2)


if __name__ == '__main__':
    unittest.main()
