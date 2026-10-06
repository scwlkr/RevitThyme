"""One deadline-bound HTTP POST; no proxies, redirects or retries."""
import http.client
import io
import json
import socket
import time

from .protocol import InvalidResponse, MAX_RESPONSE


class BridgeUnavailable(OSError):
    """Connection failed before any HTTP request could be sent."""


class DeadlineReader(io.RawIOBase):
    def __init__(self, sock, deadline):
        super().__init__()
        self.sock, self.deadline = sock, deadline
        # Retain the socket's file reference when HTTPConnection closes an
        # HTTP/1.0 connection before its response body has finished reading.
        self.raw = sock.makefile('rb', buffering=0)

    def readable(self):
        return True

    def readinto(self, buffer):
        remaining = self.deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError('Request deadline expired.')
        self.sock.settimeout(remaining)
        return self.raw.readinto(buffer)

    def close(self):
        try:
            self.raw.close()
        finally:
            super().close()


class DeadlineSocket:
    def __init__(self, sock, deadline):
        self.sock, self.deadline = sock, deadline

    def sendall(self, data):
        remaining = self.deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError('Request deadline expired.')
        self.sock.settimeout(remaining)
        self.sock.sendall(data)

    def makefile(self, mode):
        return io.BufferedReader(DeadlineReader(self.sock, self.deadline))

    def close(self):
        self.sock.close()


def post(port, route, parameters, timeout):
    deadline = time.monotonic() + timeout
    connection = http.client.HTTPConnection('127.0.0.1', port, timeout=timeout)
    try:
        try:
            connection.connect()
        except ConnectionRefusedError as error:
            raise BridgeUnavailable('Loopback Routes refused the connection; no request was sent.') from error
        except OSError as error:
            raise BridgeUnavailable('Loopback Routes connection was not established ({0}: {1}); no request was sent.'.format(
                type(error).__name__, error)) from error
        connection.sock = DeadlineSocket(connection.sock, deadline)
        connection.request('POST', '/revitthyme' + route,
                           json.dumps(parameters, allow_nan=False).encode('utf-8'),
                           {'Content-Type': 'application/json', 'Connection': 'close'})
        response = connection.getresponse()
        if response.status != 200:
            raise InvalidResponse('Routes returned HTTP {0}; redirects are refused.'.format(response.status))
        content_type = response.getheader('Content-Type', '').split(';', 1)[0].strip().lower()
        if content_type != 'application/json':
            raise InvalidResponse('Routes response Content-Type must be application/json.')
        body = response.read(MAX_RESPONSE + 1)
        if len(body) > MAX_RESPONSE:
            raise InvalidResponse('Routes response exceeds the 2 MiB limit.')
        return body.decode('utf-8', errors='strict')
    finally:
        connection.close()
