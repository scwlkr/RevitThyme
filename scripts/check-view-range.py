"""Run the portable Visual View Range source fixtures (no live Revit)."""
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'extensions/RevitThyme.extension/lib'))
suite = unittest.defaultTestLoader.discover(str(ROOT / 'tests'), pattern='test_view_range*.py')
result = unittest.TextTestRunner(verbosity=2).run(suite)
raise SystemExit(0 if result.wasSuccessful() else 1)
