"""PyInstaller entry point for the frozen sidecar.

Deliberately not `factoryforge_sidecar/__main__.py` itself. Freezing that module
directly strips its package context, so its relative imports raise
``ImportError: attempted relative import with no known parent package`` the
moment a command actually does something -- `--help` still works, which makes
the break easy to miss. Importing through the package keeps `__package__` set.
"""
import sys

from factoryforge_sidecar.__main__ import main

if __name__ == "__main__":
    sys.exit(main())
