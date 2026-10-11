"""Run the stdlib-only named query client from a source checkout."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools/query-client'))

from revitthyme_client.cli import main

if __name__ == '__main__':
    raise SystemExit(main())
