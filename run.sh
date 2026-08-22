#!/usr/bin/env bash
# Thin wrapper so Linux users can `./run.sh` instead of `python3 run.py`.
# All the actual launcher logic lives in run.py.
set -e
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "$DIR/run.py" "$@"
