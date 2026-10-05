"""Exercise installed read-only Routes, identity validation and disconnected errors."""
import argparse
import json
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASE = 'http://127.0.0.1:48884/revitthyme'


def call(route, parameters, base=BASE):
    request = urllib.request.Request(base + route, data=json.dumps(parameters).encode('utf-8'),
                                     headers={'Content-Type': 'application/json'}, method='POST')
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            return json.load(response)
    except (OSError, urllib.error.URLError) as error:
        return {'status': 'bridge_unavailable', 'diagnostics': [str(error)]}


def verify(report):
    checks = []
    first = call('/status/', {})
    checks.append({'name': 'Installed status route', 'passed': first.get('status') == 'succeeded', 'result': first})
    if first.get('status') != 'succeeded':
        value = {'scope': 'live_read_only_routes', 'passed': False, 'checks': checks,
                 'diagnostic': 'Recover/reload pyRevit from its Revit ribbon, then retry. Do not reload through a synchronous Routes request.',
                 'native_cad_checked': False, 'physical_checked': False}
        report.parent.mkdir(parents=True, exist_ok=True)
        report.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')
        print('Live checks stopped: bridge/extension unavailable; report={0}'.format(report))
        return 1
    target = first.get('target')
    inspect = call('/timberfold/inspect/', {'target': target})
    checks.append({'name': 'Installed inspect route', 'passed': inspect.get('status') == 'inspected', 'result': inspect})
    wrong = call('/timberfold/inspect/', {'target': {'session': 'stale', 'document': 'wrong'}})
    checks.append({'name': 'Wrong document refused', 'passed': wrong.get('status') == 'target_mismatch', 'result': wrong})
    missing = call('/timberfold/inspect/', {})
    checks.append({'name': 'Unbound inspection refused', 'passed': missing.get('status') == 'target_required'})
    invalid = call('/status/', {'save': True})
    checks.append({'name': 'Unknown parameters refused', 'passed': invalid.get('status') == 'invalid_request'})
    disconnected = call('/status/', {}, base='http://127.0.0.1:1/revitthyme')
    checks.append({'name': 'Disconnected bridge diagnostic', 'passed': disconnected.get('status') == 'bridge_unavailable'})
    after = call('/status/', {'target': target})
    checks.append({'name': 'Identity and observable document state preserved',
                   'passed': after.get('status') == 'succeeded' and
                   after.get('data', {}).get('document') == first.get('data', {}).get('document')})
    checks.append({'name': 'Operations declare no changed IDs', 'passed': all(
        value.get('changed_ids') == [] for value in (first, inspect, wrong, after))})
    value = {'scope': 'live_read_only_routes', 'passed': all(x['passed'] for x in checks), 'checks': checks,
             'model_geometry_fingerprint_checked': False, 'native_cad_checked': False, 'physical_checked': False}
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')
    print('Live checks: passed={0}; report={1}'.format(value['passed'], report))
    return 0 if value['passed'] else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', type=Path, default=ROOT / 'artifacts/live/read-only.json')
    args = parser.parse_args()
    raise SystemExit(verify(args.report.resolve()))
