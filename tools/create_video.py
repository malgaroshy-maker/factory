"""Stitch engine screenshots into the animated demo GIF used by the README.

Frames come from ``docs/images/frames/`` in filename order, so numbering them
(``01-editor.png``, ``02-wiring.png``, …) decides the sequence. Explicit frames
can be passed instead.

    python tools/create_video.py
    python tools/create_video.py shot_a.png shot_b.png
    python tools/create_video.py --out docs/images/tour.gif --ms 1200 frames/*.png

Capture frames with the engine's screenshot flag:

    <GODOT> --path engine/ --resolution 1600x900 -- \
        --duration=26 --screenshot=docs/images/frames/01-editor.png --screenshot-at=18
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
FRAME_DIR = ROOT / "docs" / "images" / "frames"
DEFAULT_OUT = ROOT / "docs" / "images" / "demo_video.gif"
SIZE = (1280, 720)


def resolve(path_text: str) -> Path:
    """Paths are repo-relative unless absolute, so the script is not tied to
    one machine the way the hardcoded C:\\Users\\… list was."""
    path = Path(path_text)
    return path if path.is_absolute() else (ROOT / path)


def display(path: Path) -> str:
    """Repo-relative for readability, absolute when it lives outside the repo —
    Path.relative_to raises rather than falling back."""
    try:
        return str(path.relative_to(ROOT))
    except ValueError:
        return str(path)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("frames", nargs="*",
                        help=f"frame images (default: every PNG in {display(FRAME_DIR)})")
    parser.add_argument("--out", default=display(DEFAULT_OUT),
                        help="output GIF path")
    parser.add_argument("--ms", type=int, default=1500,
                        help="milliseconds per frame (default: 1500)")
    args = parser.parse_args(argv)

    if args.frames:
        frames = [resolve(f) for f in args.frames]
    else:
        frames = sorted(FRAME_DIR.glob("*.png")) if FRAME_DIR.is_dir() else []

    missing = [f for f in frames if not f.is_file()]
    if missing:
        # Loudly, rather than silently skipping: the previous version dropped
        # absent frames and could happily write a one-frame "animation".
        for f in missing:
            print(f"error: no such frame: {f}", file=sys.stderr)
        return 1

    if not frames:
        print(f"error: no frames given and none found in {FRAME_DIR}", file=sys.stderr)
        print("       capture some with the engine's --screenshot flag first.", file=sys.stderr)
        return 1

    images = [Image.open(f).convert("RGB").resize(SIZE, Image.Resampling.LANCZOS)
              for f in frames]

    out = resolve(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    images[0].save(out, save_all=True, append_images=images[1:],
                   duration=args.ms, loop=0)

    print(f"wrote {display(out)} — {len(images)} frames @ {args.ms} ms")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
