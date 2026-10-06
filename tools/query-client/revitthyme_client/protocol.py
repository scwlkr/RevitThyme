"""Validate the existing v1 read-only Routes contract independently of Revit."""
import json
from urllib.parse import urlsplit

OPERATIONS = {
    'revitthyme_status': '/status/',
    'timberfold_inspect': '/timberfold/inspect/',
}
REJECTIONS = {'invalid_request', 'target_mismatch', 'failed'}
MAX_RESPONSE = 2 * 1024 * 1024


class InvalidResponse(ValueError):
    """A response does not prove completion of the requested operation."""


def endpoint(value):
    """Only the literal IPv4 loopback listener and existing namespace are allowed."""
    parsed = urlsplit(value)
    try:
        port = parsed.port
    except ValueError as error:
        raise ValueError('Invalid loopback port.') from error
    if (parsed.scheme != 'http' or parsed.hostname != '127.0.0.1' or
            parsed.username is not None or parsed.password is not None or
            parsed.query or parsed.fragment or
            parsed.path.rstrip('/') != '/revitthyme' or
            port is None or not 1 <= port <= 65535 or
            parsed.netloc != '127.0.0.1:' + str(port)):
        raise ValueError('Use http://127.0.0.1:<port>/revitthyme with no credentials or query.')
    return 'http://127.0.0.1:{0}/revitthyme'.format(port), port


def validate_target(target):
    if (not isinstance(target, dict) or set(target) != {'session', 'document'} or
            not isinstance(target['session'], str) or not target['session'] or
            (target['document'] is not None and
             (not isinstance(target['document'], str) or not target['document']))):
        raise ValueError('Target requires a nonempty session and a document token or null.')
    return dict(target)


def decode_json(raw):
    def object_pairs(pairs):
        value = {}
        for key, item in pairs:
            if key in value:
                raise ValueError('Duplicate JSON key: ' + key)
            value[key] = item
        return value

    def invalid_constant(value):
        raise ValueError('Nonfinite JSON value: ' + value)

    try:
        return json.loads(raw, object_pairs_hook=object_pairs,
                          parse_constant=invalid_constant)
    except (ValueError, UnicodeError, RecursionError) as error:
        raise InvalidResponse('Response is not unambiguous UTF-8 JSON.') from error


def require(condition, message):
    if not condition:
        raise InvalidResponse(message)


def ids(value):
    return (isinstance(value, list) and
            all(type(item) is int and item >= 0 for item in value) and
            len(value) == len(set(value)))


def strings(value):
    return isinstance(value, list) and all(isinstance(item, str) for item in value)


def validate_response(raw, operation, target):
    result = decode_json(raw)
    require(isinstance(result, dict), 'Response must be a JSON object.')
    require(type(result.get('schema_version')) is int and result['schema_version'] == 1,
            'Response schema_version must be 1.')
    require(isinstance(result.get('suite_version'), str) and result['suite_version'],
            'Response must identify the suite version.')
    require(result.get('operation') == operation, 'Response operation differs from request.')
    require(result.get('effects') == ['read_model'] and result.get('changed_ids') == [],
            'Response does not declare the read-only contract.')
    require(ids(result.get('skipped_ids')), 'Response skipped_ids must be unique element IDs.')
    try:
        actual_target = validate_target(result.get('target'))
    except ValueError as error:
        raise InvalidResponse('Response has an invalid target.') from error
    state = result.get('status')
    allowed = REJECTIONS | ({'succeeded'} if operation == 'revitthyme_status' else
                            {'inspected', 'no_document', 'unsupported_document', 'target_required'})
    require(isinstance(state, str) and state in allowed, 'Response status is unknown.')
    if state == 'target_mismatch':
        require(target is not None and actual_target != target,
                'Target-mismatch response does not identify a changed target.')
    elif target is not None:
        require(actual_target == target, 'Response target differs from the bound request.')
    if state not in {'succeeded', 'inspected'}:
        diagnostics = result.get('diagnostics')
        if state == 'unsupported_document' and isinstance(result.get('data'), dict):
            diagnostics = result['data'].get('diagnostics')
        require(strings(diagnostics), 'Rejected response lacks diagnostics.')
        return result
    data = result.get('data')
    require(isinstance(data, dict), 'Completed response lacks operation data.')
    if operation == 'revitthyme_status':
        validate_status(data, actual_target)
    else:
        validate_inspection(data, result['skipped_ids'])
    return result


def validate_status(data, target):
    require(data.get('target') == target, 'Status data target differs from its envelope.')
    host = data.get('host')
    require(isinstance(host, dict) and host.get('adapter') == 'pyrevit' and all(
        isinstance(host.get(key), str) and host[key] for key in
        ('pyrevit_version', 'revit_version', 'revit_build')), 'Status host metadata is incomplete.')
    require(data.get('generation_available') is False, 'Status advertises unsupported writes.')
    document = data.get('document')
    if target['document'] is None:
        require(document is None and data.get('readiness') == 'no_document',
                'No-document status contradicts its target.')
    else:
        require(isinstance(document, dict) and isinstance(document.get('title'), str) and
                all(type(document.get(key)) is bool for key in
                    ('is_family', 'is_modified', 'is_read_only', 'is_modifiable')) and
                type(document.get('active_view_id')) is int and
                data.get('readiness') == 'read_only_operations_available',
                'Document status metadata is incomplete.')


def validate_inspection(data, skipped):
    tool, scope = data.get('tool'), data.get('scope')
    require(isinstance(tool, dict) and tool.get('id') == 'timberfold' and
            tool.get('generation_available') is False and
            tool.get('source_revision_verified') is False and
            isinstance(tool.get('configuration'), str) and
            tool.get('configuration') in {'unconfigured', 'missing_files', 'files_available'} and
            all(isinstance(tool.get(key), str) and tool[key] for key in
                ('registered_version', 'reference_commit')) and strings(tool.get('missing_files')),
            'Inspection tool metadata is incomplete or advertises unsupported capabilities.')
    require(isinstance(scope, dict) and type(scope.get('wall_count')) is int and
            scope['wall_count'] >= 0 and all(ids(scope.get(key)) for key in
                ('exterior_wall_ids', 'roof_ids', 'unsupported_wall_ids',
                 'skipped_interior_wall_ids')), 'Inspection scope is invalid.')
    exterior, interior = set(scope['exterior_wall_ids']), set(scope['skipped_interior_wall_ids'])
    require(not exterior & interior and len(exterior) + len(interior) == scope['wall_count'] and
            set(scope['unsupported_wall_ids']) <= exterior and
            skipped == scope['skipped_interior_wall_ids'] and strings(data.get('diagnostics')),
            'Inspection counts, IDs or diagnostics contradict the envelope.')
