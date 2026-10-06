# -*- coding: utf-8 -*-
"""Per-user settings, separate from replaceable extension code."""
import json
import os


def extension_root():
    return os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))


def read_json(path):
    with open(path, 'r') as stream:
        return json.load(stream)


def settings_path():
    return os.path.join(os.environ['LOCALAPPDATA'], 'RevitThyme', 'settings.json')


def preissue_standards():
    path = os.path.join(os.environ['LOCALAPPDATA'], 'RevitThyme', 'preissue-standards.json')
    return read_json(path) if os.path.isfile(path) else None


def tool_root():
    path = settings_path()
    if not os.path.isfile(path):
        return None
    data = read_json(path)
    if data.get('schema_version') != 1:
        raise ValueError('Unsupported RevitThyme settings schema')
    root = data.get('timberfold_root')
    if root and not os.path.isabs(root):
        raise ValueError('TimberFold root must be an absolute path')
    return root


def missing_entries(root):
    return [name for name in ('scripts/launcher.py', 'scripts/run_pipeline.py',
                             'prototype-settings.json')
            if not os.path.isfile(os.path.join(root, name))]


def configure(root):
    root = os.path.abspath(root)
    missing = missing_entries(root)
    if missing:
        raise ValueError('Missing TimberFold files: ' + ', '.join(missing))
    path = settings_path()
    folder = os.path.dirname(path)
    if not os.path.isdir(folder):
        os.makedirs(folder)
    with open(path, 'w') as stream:
        json.dump({'schema_version': 1, 'timberfold_root': root}, stream, indent=2)
    return path
