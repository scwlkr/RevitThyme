"""Synthetic Routes fixtures; these never contact the Revit host."""
import copy
import json
from pathlib import Path
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools/query-client'))
TARGET = {'session': 'fixture-session', 'document': 'fixture-document'}


def response(operation='revitthyme_status', target=None):
    target = copy.deepcopy(TARGET if target is None else target)
    result = {'schema_version': 1, 'suite_version': '0.2.0', 'operation': operation,
              'effects': ['read_model'], 'changed_ids': [], 'skipped_ids': [],
              'target': target, 'status': 'succeeded'}
    if operation == 'revitthyme_status':
        result['data'] = {
            'host': {'adapter': 'pyrevit', 'pyrevit_version': 'fixture',
                     'revit_version': '2027', 'revit_build': 'fixture-build'},
            'target': copy.deepcopy(target), 'document': None if target['document'] is None else {
                'title': 'Disposable fixture', 'is_family': False, 'is_modified': False,
                'is_read_only': False, 'is_modifiable': False, 'active_view_id': 1},
            'readiness': 'no_document' if target['document'] is None else 'read_only_operations_available',
            'generation_available': False,
        }
    else:
        result['status'] = 'inspected'
        result['skipped_ids'] = [2]
        result['data'] = {
            'tool': {'id': 'timberfold', 'registered_version': '1.1.0',
                     'reference_commit': 'fixture-revision', 'source_revision_verified': False,
                     'configuration': 'unconfigured', 'missing_files': [], 'generation_available': False},
            'scope': {'wall_count': 2, 'exterior_wall_ids': [1], 'roof_ids': [3],
                      'unsupported_wall_ids': [], 'skipped_interior_wall_ids': [2]},
            'diagnostics': ['Synthetic counts do not validate fabrication.'],
        }
    return result


class Fixture:
    def __init__(self, mode='normal', delay=0):
        self.mode, self.delay = mode, delay
        self.received, self.finished, self.release = (threading.Event() for _ in range(3))
        self.requests, self.active, self.maximum = [], 0, 0
        self.guard = threading.Lock()
        fixture = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *unused):
                pass

            def do_POST(self):
                payload = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
                with fixture.guard:
                    fixture.requests.append((self.path, payload))
                    fixture.active += 1
                    fixture.maximum = max(fixture.maximum, fixture.active)
                fixture.received.set()
                try:
                    if fixture.mode == 'blocked':
                        fixture.release.wait(3)
                    time.sleep(fixture.delay)
                    operation = 'timberfold_inspect' if self.path.endswith('/inspect/') else 'revitthyme_status'
                    value = response(operation)
                    if payload.get('target', TARGET) != TARGET:
                        value.pop('data')
                        value.update(status='target_mismatch', diagnostics=['Synthetic target changed.'])
                    if fixture.mode == 'wrong_operation':
                        value['operation'] = 'crossed_response'
                    if fixture.mode == 'crossed_target':
                        value['target']['document'] = 'crossed-document'
                    body = json.dumps(value).encode('utf-8')
                    if fixture.mode == 'invalid':
                        body = b'not-json'
                    if fixture.mode == 'trickle_headers':
                        for item in b'HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n\r\n':
                            self.wfile.write(bytes([item]))
                            self.wfile.flush()
                            time.sleep(0.01)
                        return
                    self.send_response(302 if fixture.mode == 'redirect' else 200)
                    if fixture.mode == 'redirect':
                        self.send_header('Location', fixture.base + '/status/')
                    self.send_header('Content-Type', 'text/plain' if fixture.mode == 'wrong_type' else 'application/json')
                    declared_length = len(body) + (100 if fixture.mode == 'truncated_body' else 0)
                    self.send_header('Content-Length', str(declared_length))
                    self.end_headers()
                    if fixture.mode == 'trickle_body':
                        for item in body:
                            self.wfile.write(bytes([item]))
                            self.wfile.flush()
                            time.sleep(0.01)
                    elif fixture.mode == 'split_body':
                        self.wfile.write(body[:10])
                        self.wfile.flush()
                        time.sleep(0.15)
                        self.wfile.write(body[10:])
                    else:
                        self.wfile.write(body)
                except (BrokenPipeError, ConnectionResetError):
                    pass
                finally:
                    with fixture.guard:
                        fixture.active -= 1
                    fixture.finished.set()

        self.server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        self.base = 'http://127.0.0.1:{0}/revitthyme'.format(self.server.server_port)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *unused):
        self.release.set()
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)
