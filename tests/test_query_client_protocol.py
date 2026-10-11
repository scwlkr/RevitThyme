"""Independent envelope validation against the implemented Routes shapes."""
import copy
import json
import unittest

from query_client_support import TARGET, response
from revitthyme_client import QueryClient
from revitthyme_client.protocol import InvalidResponse, endpoint, validate_response


class ProtocolTests(unittest.TestCase):
    def test_current_status_and_inspection_shapes(self):
        for operation in ('revitthyme_status', 'timberfold_inspect'):
            value = response(operation)
            self.assertEqual(validate_response(json.dumps(value), operation, TARGET), value)
        no_document = {'session': TARGET['session'], 'document': None}
        value = response(target=no_document)
        self.assertEqual(validate_response(json.dumps(value), 'revitthyme_status', None), value)

    def test_family_result_keeps_diagnostics_inside_data(self):
        # operations.execute pops inspect's status, leaving its diagnostics in data.
        value = response('timberfold_inspect')
        value.update(status='unsupported_document', skipped_ids=[],
                     data={'diagnostics': ['TimberFold inspection requires a project document.']})
        self.assertEqual(validate_response(json.dumps(value), 'timberfold_inspect', TARGET), value)
        del value['data']['diagnostics']
        with self.assertRaises(InvalidResponse):
            validate_response(json.dumps(value), 'timberfold_inspect', TARGET)

    def test_target_mismatch_is_explicit_and_must_actually_differ(self):
        value = response()
        value.pop('data')
        value.update(status='target_mismatch', diagnostics=['Rediscover target.'])
        intended = {'session': 'stale-session', 'document': 'stale-document'}
        self.assertEqual(validate_response(json.dumps(value), 'revitthyme_status', intended), value)
        for target in (TARGET, None):
            with self.assertRaises(InvalidResponse):
                validate_response(json.dumps(value), 'revitthyme_status', target)

    def test_crossed_response_effects_and_invalid_metadata_are_rejected(self):
        mutations = [
            lambda value: value.update(operation='timberfold_inspect'),
            lambda value: value.update(schema_version=True),
            lambda value: value.update(changed_ids=[123]),
            lambda value: value.update(effects=['read_model', 'write_model']),
            lambda value: value.update(status=['succeeded']),
            lambda value: value.update(target={'session': 'other', 'document': 'other'}),
            lambda value: value['data'].update(generation_available=True),
            lambda value: value['data']['host'].pop('revit_build'),
            lambda value: value['data']['document'].update(is_modified=0),
        ]
        for mutate in mutations:
            value = response()
            mutate(value)
            with self.subTest(value=value), self.assertRaises(InvalidResponse):
                validate_response(json.dumps(value), 'revitthyme_status', TARGET)

    def test_inspection_counts_and_declared_capabilities_must_agree(self):
        value = response('timberfold_inspect')
        mutations = [
            lambda item: item['data']['tool'].update(configuration=[]),
            lambda item: item['data']['tool'].update(source_revision_verified=True),
            lambda item: item['data']['scope'].update(wall_count=3),
            lambda item: item['data']['scope'].update(roof_ids=[True]),
            lambda item: item['data']['scope'].update(unsupported_wall_ids=[2]),
            lambda item: item.update(skipped_ids=[]),
        ]
        for mutate in mutations:
            item = copy.deepcopy(value)
            mutate(item)
            with self.subTest(item=item), self.assertRaises(InvalidResponse):
                validate_response(json.dumps(item), 'timberfold_inspect', TARGET)

    def test_ambiguous_json_is_rejected(self):
        for raw in ('{"status":1,"status":2}', '{"value":NaN}', '[]', 'not-json'):
            with self.subTest(raw=raw), self.assertRaises(InvalidResponse):
                validate_response(raw, 'revitthyme_status', None)

    def test_endpoint_never_accepts_nonloopback_credentials_or_url_extras(self):
        for base in ('http://localhost:48884/revitthyme', 'http://192.0.2.1:48884/revitthyme',
                     'https://127.0.0.1:48884/revitthyme', 'http://user@127.0.0.1:48884/revitthyme',
                     'http://127.0.0.1:48884/revitthyme?x=y', 'http://127.0.0.1:48884/revitthyme#fragment',
                     'http://127.0.0.1:48884/execute', 'http://127.0.0.1:0/revitthyme'):
            with self.subTest(base=base), self.assertRaises(ValueError):
                endpoint(base)
        self.assertEqual(endpoint('http://127.0.0.1:48884/revitthyme/')[1], 48884)

    def test_time_bounds_are_finite_and_small(self):
        for parameters in ({'timeout': 0}, {'timeout': float('nan')}, {'timeout': 121},
                           {'lock_wait': float('inf')}, {'lock_wait': -1}, {'timeout': True}):
            with self.subTest(parameters=parameters), self.assertRaises(ValueError):
                QueryClient(**parameters)


if __name__ == '__main__':
    unittest.main()
