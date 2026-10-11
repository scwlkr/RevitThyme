"""JSON-output CLI; all network operations are named and read-only."""
import argparse
import json
from pathlib import Path

from .client import BASE, QueryClient
from .protocol import decode_json, validate_response, validate_target
from .state import LockBusy


def parser():
    value = argparse.ArgumentParser(description=__doc__)
    commands = value.add_subparsers(dest='command', required=True)
    for name in ('status', 'inspect', 'pending', 'reconcile'):
        command = commands.add_parser(name)
        command.add_argument('--base', default=BASE)
        command.add_argument('--state-directory', type=Path,
                             help='Shared by every cooperating client for this endpoint.')
        command.add_argument('--lock-wait', type=float, default=5)
        if name in ('status', 'inspect'):
            command.add_argument('--timeout', type=float, default=20)
            command.add_argument('--target-file', type=Path, required=name == 'inspect',
                                 help='JSON envelope from a completed status query.')
        if name == 'reconcile':
            command.add_argument('--request-id', required=True)
            command.add_argument('--basis', choices=('host_restarted', 'execution_ended'), required=True)
            command.add_argument('--evidence', required=True,
                                 help='Independent host evidence; caller supplied, not client verified.')
    return value


def target_from_file(path, expected_endpoint=None):
    envelope = decode_json(path.read_text(encoding='utf-8'))
    if (not isinstance(envelope, dict) or type(envelope.get('client_schema_version')) is not int or
            envelope['client_schema_version'] != 1 or
            envelope.get('state') != 'completed' or envelope.get('operation') != 'revitthyme_status' or
            not isinstance(envelope.get('result'), dict) or
            envelope['result'].get('status') != 'succeeded'):
        raise ValueError('Target file must contain a completed successful client status envelope.')
    if expected_endpoint is not None and envelope.get('endpoint') != expected_endpoint:
        raise ValueError('Target file belongs to a different endpoint; discover status from this endpoint.')
    validate_response(json.dumps(envelope['result']), 'revitthyme_status', None)
    return validate_target(envelope['result'].get('target'))


def main(arguments=None):
    args = parser().parse_args(arguments)
    try:
        client = QueryClient(base=args.base, state_directory=args.state_directory,
                             timeout=getattr(args, 'timeout', 20), lock_wait=args.lock_wait)
        if args.command in ('status', 'inspect'):
            target = target_from_file(args.target_file, client.base) if args.target_file else None
            operation = 'revitthyme_status' if args.command == 'status' else 'timberfold_inspect'
            result = client.query(operation, target)
            passed = result.get('state') == 'completed' and result['result']['status'] in {'succeeded', 'inspected'}
        elif args.command == 'pending':
            pending = client.pending()
            result = {'client_schema_version': 1, 'state': 'quarantined' if pending else 'idle',
                      'pending_request': pending}
            passed = pending is None
        else:
            result = client.reconcile(args.request_id, args.basis, args.evidence)
            passed = True
    except (OSError, ValueError) as error:
        result = {'client_schema_version': 1,
                  'state': 'busy' if isinstance(error, LockBusy) else 'local_error',
                  'diagnostic': str(error)}
        passed = False
    print(json.dumps(result, indent=2, allow_nan=False))
    return 0 if passed else 1
