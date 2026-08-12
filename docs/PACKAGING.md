# Packaging FactoryForge

*Status: presets written, **export not yet verified on this machine** — the
Godot export templates are not installed here, so nobody has produced a binary
from these presets yet. Treat the steps below as the intended recipe, not as a
tested one. If you run it, please correct this file with what actually happened.*

Until a release is published, running FactoryForge means building from source:
Godot 4.7-mono **and** the .NET 8 SDK. That is a real barrier for the audience
this project is for — a student learning PLC programming should not have to
install a game engine and a compiler first.

---

## What you need

| | |
|---|---|
| **Godot 4.7.1 .NET (mono)** | the same version the project was built with |
| **Export templates** | Editor → Editor menu → *Manage Export Templates* → Download |
| **.NET 8 SDK** | on the machine doing the export, not on the user's |

The export templates are a separate ~1 GB download from the editor itself, and
a .NET project cannot be exported without them.

---

## Building

`engine/export_presets.cfg` defines two targets. From the project root:

```bash
# Windows
godot --headless --path engine/ --export-release "Windows Desktop" ../dist/windows/FactoryForge.exe

# Linux
godot --headless --path engine/ --export-release "Linux" ../dist/linux/FactoryForge.x86_64
```

`--export-debug` instead of `--export-release` keeps the stack traces, which is
what you want while the packaging itself is still being proven.

---

## What ships alongside the binary

The engine on its own speaks the tag bus and nothing else. **Every PLC protocol
lives in the Python sidecar**, so a binary with no sidecar can render a factory
and talk to nothing.

A release therefore needs:

- the exported engine
- the `sidecar/` package, and a Python to run it
- `examples/` — the TIA project, the Node-RED flow, a sample mapping

The open question is how to ship Python. Options, none of them tried yet:

1. **Require Python** and document `pip install -e sidecar`. Simplest, and
   pushes an install step onto exactly the audience least likely to enjoy it.
2. **PyInstaller the sidecar** into a single executable per platform, and have
   the engine's driver dialog launch that instead of `python`. Best experience,
   most build machinery. `DriverConnectionUI.LaunchSidecar` is the one place
   that would need to change.
3. **Ship a bundled interpreter** next to the binary. Fewest moving parts at
   runtime, largest download.

Option 2 is the one worth trying first: it is the only one where a person can
download one archive and connect to a PLC without reading anything.

---

## Known gaps

- Nothing here is verified. See the status note at the top.
- No code signing, so Windows SmartScreen will warn on first run.
- No CI job builds a release; the self-tests
  (`--self-test=buttons`, `--self-test=io`) run headless and could gate one.
