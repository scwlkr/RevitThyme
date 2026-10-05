"""Diagnose and back up a narrow repair for the observed pyRevit Routes source."""
import argparse
import ast
import hashlib
import http.server
import json
import os
import socketserver
import subprocess
import sys
import tempfile
import threading
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
START = '''    def start(self):
        self.server_thread = threading.Thread(target=self.server.serve_forever)'''
FIXED_START = '''    def start(self):
        # Constructor and activate_server both call start; own exactly one loop.
        if hasattr(self, "server_thread") and self.server_thread.is_alive():
            return
        self.server_thread = threading.Thread(target=self._serve_safely)'''
SHUTDOWN = '''    def shutdown(self):
        self.socket.close()
        HTTPServer.shutdown(self)'''
FIXED_SHUTDOWN = '''    def shutdown(self):
        HTTPServer.shutdown(self)
        self.server_close()'''
WORKER = '''    def _serve_safely(self):
        try:
            self.server.serve_forever()
        except Exception:
            _write_routes_error(traceback.format_exc())

'''
LOGGING = '''def _write_routes_error(message):
    # HTTP workers must never open or write a pyRevit UI output console.
    import os
    import tempfile
    with open(os.path.join(tempfile.gettempdir(), "pyRevit-Routes-errors.log"), "a") as stream:
        stream.write(str(message) + "\\n")


'''
REQUEST_LOG = '''    def log_message(self, format, *args):
        # BaseHTTPRequestHandler otherwise writes to the script's UI stderr.
        return

'''
ERROR_LOG = '''    def handle_error(self, request, client_address):
        _write_routes_error(traceback.format_exc())

'''


def patched(source):
    if 'def _serve_safely(self):' in source:
        return source
    for token in (START, SHUTDOWN, 'class HttpRequestHandler(', 'class ThreadedHttpServer('):
        if source.count(token) != 1:
            raise ValueError('Unrecognized Routes source; no automatic repair: ' + token.splitlines()[0])
    source = source.replace(START, WORKER + FIXED_START).replace(SHUTDOWN, FIXED_SHUTDOWN)
    source = source.replace('class HttpRequestHandler(', LOGGING + 'class HttpRequestHandler(', 1)
    source = source.replace('    def _parse_api_path(self):', REQUEST_LOG + '    def _parse_api_path(self):', 1)
    source = source.replace(FIXED_SHUTDOWN, ERROR_LOG + FIXED_SHUTDOWN, 1)
    ast.parse(source)
    return source


def lifecycle_probe(path):
    """Run the actual server classes, with only Revit route execution excluded."""
    source = path.read_text(encoding='utf-8-sig')
    tree = ast.parse(source)
    names = {'RoutesServer', 'ThreadedHttpServer', '_write_routes_error'}
    nodes = [node for node in tree.body if getattr(node, 'name', None) in names]
    # Use the real request logging method while the health response stays API-free.
    request = next(node for node in tree.body if getattr(node, 'name', None) == 'HttpRequestHandler')
    logging_method = next((node for node in request.body if getattr(node, 'name', None) == 'log_message'), None)

    class QuietHealth(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b'healthy')

    namespace = {'HTTPServer': http.server.HTTPServer, 'ThreadingMixIn': socketserver.ThreadingMixIn,
                 'HttpRequestHandler': QuietHealth, 'threading': threading,
                 'traceback': __import__('traceback')}
    exec(compile(ast.Module(body=nodes, type_ignores=[]), str(path), 'exec'), namespace)
    if logging_method:
        method_tree = ast.Module(body=[logging_method], type_ignores=[])
        exec(compile(method_tree, str(path), 'exec'), namespace)
        QuietHealth.log_message = namespace['log_message']
    attempts = []

    class ObserveStderr:
        def write(self, value):
            attempts.append(value)
        def flush(self):
            pass

    original_stderr = sys.stderr
    sys.stderr = ObserveStderr()
    try:
        server = namespace['RoutesServer']('127.0.0.1', 0)
        thread = server.server_thread
        server.start()  # This is what upstream activate_server does after construction.
        single = server.server_thread is thread
        port = server.server.server_address[1]
        with urllib.request.urlopen('http://127.0.0.1:' + str(port), timeout=4) as response:
            healthy = response.read() == b'healthy'
        if single:
            server.stop()
            stopped = not thread.is_alive()
        else:
            # The child process owns these daemon threads and exits after its report.
            stopped = False
        value = {'single_server_thread': single, 'http_response': healthy,
                 'no_worker_ui_output': not attempts, 'stopped_cleanly': stopped}
        print(json.dumps(value))
        return 0 if all(value.values()) else 1
    finally:
        sys.stderr = original_stderr


def main(args):
    if args.probe:
        return lifecycle_probe(args.probe)
    path = args.pyrevit_root / 'pyrevitlib/pyrevit/routes/server/server.py'
    before_bytes = path.read_bytes()
    before = before_bytes.decode('utf-8-sig').replace('\r\n', '\n')
    after = patched(before)
    results = {}
    with tempfile.TemporaryDirectory(prefix='RevitThyme Routes check ') as folder:
        for name, text in (('before', before), ('after', after)):
            candidate = Path(folder) / (name + '.py')
            candidate.write_text(text, encoding='utf-8')
            run = subprocess.run([sys.executable, str(Path(__file__).resolve()), '--probe', str(candidate)],
                                 capture_output=True, text=True, timeout=15)
            results[name] = {'exit': run.returncode, 'result': run.stdout.strip(), 'stderr': run.stderr}
    if results['after']['exit'] != 0:
        raise RuntimeError('Candidate Routes lifecycle check failed: ' + str(results['after']))
    changed = before != after
    if args.apply and changed:
        backup = args.report.parent / 'pyrevit-routes-server-before.py'
        if backup.exists():
            raise ValueError('Existing repair backup must be preserved before a new repair')
        backup.parent.mkdir(parents=True, exist_ok=True)
        backup.write_bytes(before_bytes)
        path.write_text(after, encoding='utf-8', newline='\r\n')
        if path.read_text(encoding='utf-8-sig') != after:
            path.write_bytes(before_bytes)
            raise RuntimeError('Repair read-back failed; original source restored')
    value = {'scope': 'cpython_routes_lifecycle_only', 'source': str(path), 'changed': changed,
             'applied': args.apply and changed, 'before_sha256': hashlib.sha256(before_bytes).hexdigest(),
             'checks': results, 'live_revit_verified': False}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(value, indent=2))
    return 0


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pyrevit-root', type=Path,
                        default=Path(os.environ['APPDATA']) / 'pyRevit-Master')
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--report', type=Path, default=ROOT / 'artifacts/live/routes-repair.json')
    parser.add_argument('--probe', type=Path, help=argparse.SUPPRESS)
    raise SystemExit(main(parser.parse_args()))
