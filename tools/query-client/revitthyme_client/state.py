"""Cooperating-process lock and persistent quarantine; never reclaim by age."""
import errno
import hashlib
import json
import os
from pathlib import Path
import tempfile
import time


class LockBusy(OSError):
    pass


def default_state_directory():
    local = os.environ.get('LOCALAPPDATA') if os.name == 'nt' else None
    return Path(local or Path.home() / '.cache') / 'RevitThyme' / 'query-client'


def sync_directory(directory):
    if os.name != 'nt':
        descriptor = os.open(directory, os.O_RDONLY)
        try:
            os.fsync(descriptor)
        finally:
            os.close(descriptor)


class State:
    def __init__(self, directory, endpoint, wait):
        self.directory = Path(directory).expanduser().resolve()
        self.directory.mkdir(parents=True, exist_ok=True, mode=0o700)
        key = hashlib.sha256(endpoint.encode('utf-8')).hexdigest()[:24]
        self.lock_path = self.directory / (key + '.lock')
        self.pending_path = self.directory / (key + '.pending.json')
        self.recovery_path = self.directory / (key + '.recoveries.jsonl')
        self.wait, self.stream = wait, None

    def __enter__(self):
        self.stream = self.lock_path.open('a+b')
        if self.stream.seek(0, os.SEEK_END) == 0:
            self.stream.write(b'0')
            self.stream.flush()
        deadline = time.monotonic() + self.wait
        while True:
            try:
                self.stream.seek(0)
                if os.name == 'nt':
                    import msvcrt
                    msvcrt.locking(self.stream.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(self.stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                return self
            except OSError as error:
                if error.errno not in (errno.EACCES, errno.EAGAIN, errno.EDEADLK):
                    self.stream.close()
                    raise
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    self.stream.close()
                    raise LockBusy('Another cooperating client holds the endpoint lock.') from error
                time.sleep(min(0.05, remaining))

    def __exit__(self, *unused):
        try:
            self.stream.seek(0)
            if os.name == 'nt':
                import msvcrt
                msvcrt.locking(self.stream.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                import fcntl
                fcntl.flock(self.stream.fileno(), fcntl.LOCK_UN)
        finally:
            self.stream.close()

    def pending(self):
        try:
            value = json.loads(self.pending_path.read_text(encoding='utf-8'))
        except FileNotFoundError:
            return None
        except (ValueError, UnicodeError) as error:
            raise ValueError('Quarantine journal is unreadable; preserve it for manual investigation.') from error
        if (not isinstance(value, dict) or value.get('schema_version') != 1 or
                not isinstance(value.get('request_id'), str) or not value['request_id'] or
                not isinstance(value.get('operation'), str) or
                not isinstance(value.get('endpoint'), str)):
            raise ValueError('Quarantine journal is invalid; preserve it for manual investigation.')
        return value

    def begin(self, value):
        descriptor, temporary = tempfile.mkstemp(prefix='.query-', dir=self.directory)
        try:
            with os.fdopen(descriptor, 'w', encoding='utf-8') as stream:
                json.dump(value, stream, allow_nan=False)
                stream.write('\n')
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, self.pending_path)
            sync_directory(self.directory)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)

    def clear(self):
        self.pending_path.unlink()
        sync_directory(self.directory)

    def record_recovery(self, value):
        with self.recovery_path.open('a', encoding='utf-8') as stream:
            json.dump(value, stream, allow_nan=False)
            stream.write('\n')
            stream.flush()
            os.fsync(stream.fileno())
        self.clear()
