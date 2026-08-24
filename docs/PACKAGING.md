# Packaging a FactoryForge release

*Verified end to end on 2026-08-23: Godot 4.7.2-mono on Windows, producing a
92.8 MB archive whose engine passes all 22 headless self-tests and whose frozen
sidecar drives it with no Python installed.*

Running FactoryForge from a checkout means installing Godot, the .NET 8 SDK and
Python first — a real barrier for someone who wanted to learn ladder logic, not
to install a game engine and a compiler. A release exists so that stops being
true.

```bash
python tools/build_release.py
```

That is the whole recipe. It exports the engine, freezes the sidecar, copies the
payload, and writes `dist/FactoryForge-<platform>.zip`. The rest of this file is
what it does and why, for when it breaks.

---

## What you need

| | |
|---|---|
| **Godot 4.7.2 .NET (mono)** | the version the project is built and tested with; `project.godot` asks only for 4.7, so any 4.7.x works |
| **Export templates** | a ~1.2 GB download, once — see below |
| **.NET 8 SDK** | on the machine doing the export, not on the user's |
| **PyInstaller** | `pip install pyinstaller`, to freeze the sidecar |

### Installing the export templates

The editor's *Editor → Manage Export Templates → Download* does this, but it
needs a GUI. Headless, fetch the `.tpz` for your exact Godot version and unpack
it flat into Godot's template directory:

```bash
curl -L -o templates.tpz https://github.com/godotengine/godot/releases/download/4.7.2-stable/Godot_v4.7.2-stable_mono_export_templates.tpz
```

The archive holds a `templates/` folder with a `version.txt` in it — that file's
contents (`4.7.2.stable.mono`) is the directory name Godot looks for. Unpack the
files, without their `templates/` prefix, into:

* Windows — `%APPDATA%\Godot\export_templates\4.7.2.stable.mono\`
* Linux — `~/.local/share/godot/export_templates/4.7.2.stable.mono/`

`tools/build_release.py` does not do this for you: a 1.2 GB download is not
something a build script should start on its own.

---

## The one that will bite you: the solution file

**A .NET export needs `engine/FactoryForge.sln`.** Without it Godot prints

```
ERROR: Export .NET Project: This project contains C# files but no solution file
```

…and then **writes the binary anyway and exits 0**. You get a `FactoryForge.exe`
of the right size, with no `data_FactoryForge_*` folder beside it, whose every
C# script fails at runtime. It looks like a successful build.

`dotnet build` never creates a `.sln` — it only needs the `.csproj` — so a
checkout that has only ever been built from the command line will not have one.
Create it once:

```bash
dotnet new sln -n FactoryForge --format sln
```

`--format sln` matters on .NET 9+: without it you get a `.slnx`, the newer XML
format, which Godot 4.7 does not look for. `tools/build_release.py` refuses to
run if the `.sln` is missing rather than producing the silently-broken binary.

---

## What a release contains

```
FactoryForge-windows/
  FactoryForge.exe                     the engine, with the .pck embedded
  data_FactoryForge_windows_x86_64/    the .NET assemblies — required
  factoryforge-sidecar.exe             every PLC protocol, frozen
  examples/                            TIA project, Node-RED flow, mappings
  docs/                                GETTING_STARTED, tag-bus, authoring guides
  README.md, LICENSE
```

The five scene templates are `res://` resources and travel **inside** the
binary; they are deliberately not in that list.

### One file, or one folder — one folder

`binary_format/embed_pck=true` on both presets, so the `.pck` is inside the
executable. That is as far as "one file" goes: a .NET export always needs its
assemblies folder beside the binary, so a single self-contained executable is
not available. Two items instead of three is the improvement that was actually
on the table, and it removes the `.pck` a user could lose.

### How Python ships

**Frozen with PyInstaller, one executable per platform.** The engine speaks the
tag bus and nothing else — every PLC protocol lives in the Python sidecar — so a
release without one can render a factory and talk to nothing.

The alternative was documenting `pip install -e sidecar`, which pushes an
install step onto exactly the audience least likely to enjoy it. Freezing costs
~20 MB and a few minutes of build time, and is the only option where a person
downloads one archive and connects to a PLC without reading anything.

Two things about the freeze are worth knowing:

* The entry point is `tools/packaging/sidecar_entry.py`, **not**
  `factoryforge_sidecar/__main__.py`. Freezing the module directly strips its
  package context, and its relative imports then raise
  `ImportError: attempted relative import with no known parent package` — but
  only once a command does real work. `--help` still prints, which makes the
  break easy to ship.
* The frozen binary carries whichever optional drivers were installed when it
  was built. `websockets` is the only hard dependency; `asyncua`, `python-snap7`
  and `pythonnet` are extras. Build on a machine with `pip install -e
  "sidecar[opcua,s7,plcsim]"` if the release should support all of them.

### How the engine finds it

`SidecarLocator` looks in order: the `FACTORYFORGE_SIDECAR` environment
variable, then beside the binary, then beside `engine/` (a checkout), then the
executable's directory. A frozen `factoryforge-sidecar[.exe]` wins over a source
`sidecar/` package in the same directory, because the one that needs no Python
is the one to run.

`--self-test=sidecar` reports which it found and how it would start it. Run it
against a release and it should say `Bundled`; against a checkout, `Source`.

---

## Verifying a release

Every engine self-test runs against the exported binary — that is the point of
them being headless:

```bash
./dist/windows/FactoryForge.exe --headless -- --self-test=scenes
```

All 22 pass against the packaged Windows build, including the two that read
checked-in fixtures. Those two used to fail in an export for two separate
reasons, both now fixed: the fixtures lived outside `res://` at a path that does
not exist beside a binary, and even once moved in, `System.IO.File` cannot read
inside a `.pck` — only Godot's `FileAccess` can (see `engine/src/Sim/FixtureFile.cs`).

---

## Code signing — not signed, and the download page says so

Releases are **not** signed. Windows SmartScreen will warn on first run, and the
user has to click *More info → Run anyway*.

That is a deliberate choice, not an oversight. An OV code-signing certificate
runs a few hundred dollars a year and requires an identity that an
individual-authored open project may not want to maintain; an EV one needs
hardware. The honest alternative to paying for it is saying plainly that the
warning is expected and why — which the README does — rather than leaving a
first-time user to guess whether the download is safe.

Revisit if the project ever distributes through a channel where the warning
blocks installation outright rather than just alarming.

---

## Known gaps

- **The Linux binary is built but not run here.** It exports cleanly from
  Windows; the release CI job (`.github/workflows/release.yml`) runs the
  self-tests against it on a real Linux runner, which is where that claim gets
  checked.
- **macOS is not packaged.** No preset exists and it cannot be tested from here.
  `TerminalLauncher` already handles macOS terminals when someone picks it up.
- **The frozen sidecar is per-platform.** A Windows release cannot ship the
  Linux one; each platform's archive has to be built on that platform, which is
  what the CI matrix is for.
