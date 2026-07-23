# Fast Action

[![CI](https://github.com/Luttik/fast-action/actions/workflows/ci.yml/badge.svg)](https://github.com/Luttik/fast-action/actions/workflows/ci.yml)

A PowerToys-inspired Windows keyboard overlay. Press a global hotkey to open a QWERTY-aligned action grid. Keys and clicks run shell commands or open nested grids.

## Requirements

- Windows 10 1809+ / Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows App SDK runtime (bundled via self-contained build)

## Run

It is **not** a Windows Store / installed app yet — you run the unpackaged build locally.

```powershell
cd c:\workspace\fast-action
.\run.ps1
```

Or double-click / run `run.cmd`. Options:

```powershell
.\run.ps1            # Debug build + launch
.\run.ps1 -Release   # Release build + launch
.\run.ps1 -NoBuild   # Launch last build only
```

Manual equivalent:

```powershell
dotnet build src\FastAction\FastAction.csproj -c Debug -p:Platform=x64
dotnet run --project src\FastAction\FastAction.csproj -c Debug -p:Platform=x64
```

The app starts in the system tray. Default hotkey: **Win+Shift+Space** (registered via a low-level keyboard hook so Win combinations work reliably).

## Tray menu

- **Open overlay** — show the root grid
- **Open config folder** — `%LOCALAPPDATA%\FastAction`
- **Reload config** — re-read `config.yaml`
- **Exit**

## Config

On first launch the app copies an example config to:

`%LOCALAPPDATA%\FastAction\config.yaml`

Edits are watched and reloaded live.

### Schema

```yaml
hotkey:
  modifiers: [Win, Shift]   # Win, Ctrl, Alt, Shift
  key: Space                # Space, Tab, Enter, Esc, A–Z, 0–9, F1–F12
rootGridId: home
editOnRightClick: true      # right-click a tile to edit or clear it
grids:
  - id: home
    title: Home
    items:
      - key: Q              # Must be one of Q–P / A–L / Z–M
        name: Notepad
        icon:
          type: app         # app | lucide | svg
          path: notepad.exe
        action:
          type: shell       # shell | grid | hotkey
          command: notepad.exe
          args: []          # optional
          workingDirectory: # optional
      - key: W
        name: Dev tools
        icon:
          type: lucide
          name: wrench      # Assets/Icons/lucide/{name}.svg
        action:
          type: grid
          gridId: devtools
      - key: E
        name: Explorer
        icon:
          type: lucide
          name: folder
        action:
          type: hotkey
          modifiers: [Win]
          key: E
      - key: R
        name: Custom SVG
        icon:
          type: svg
          path: icons/my.svg  # relative to config folder, or absolute
        action:
          type: shell
          command: powershell
          args: [-NoProfile, -Command, "Write-Host hi"]
```

### Keyboard layout

Built as a 3x3 letter core, plus number row `1–3`, plus a 4th column (`4 R F V`):

|     |     |     |     |
| --- | --- | --- | --- |
| 1   | 2   | 3   | 4   |
| Q   | W   | E   | R   |
| A   | S   | D   | F   |
| Z   | X   | C   | V   |

Empty slots stay blank. Esc pops a nested grid or closes at root. Backspace pops when nested.

Values with colons (protocol handlers like `ms-settings:`) must be quoted in YAML:

```yaml
command: "ms-settings:"
```


| Type     | Fields                         | Notes                                      |
| -------- | ------------------------------ | ------------------------------------------ |
| `app`    | `path`                         | `.exe` / `.lnk`; icon extracted via Shell  |
| `lucide` | `name`                         | Full Lucide set under `Assets/Icons/lucide/` (e.g. `wrench`, `file`) |
| `svg`    | `path`                         | Custom SVG on disk                         |

Browse names at [lucide.dev/icons](https://lucide.dev/icons). To refresh the bundled set:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\sync_lucide_icons.ps1
```

### Regenerating the app icon / brand assets

`AppIcon.ico`, the tray icon, and the Windows Store-style logos (`StoreLogo.png`, `Square44x44Logo.*`, `SplashScreen.*`, etc.) are all generated from the source marks in `src/FastAction/Assets/Brand/`. After changing a `mark-*.png`, regenerate everything with:

```powershell
pip install -r scripts/requirements.txt
python scripts/build_icons.py
```

## Project layout

```
src/FastAction/          WinUI 3 unpackaged tray app
  Overlay/               Acrylic keyboard grid overlay
  Services/              Config, hotkey, actions, icons, theme
  Models/                YAML models + QWERTY layout
samples/config.example.yaml
```

## Development

CI runs on every push/PR to `main` (see [.github/workflows/ci.yml](.github/workflows/ci.yml)): a Release build, `dotnet format` verification, `ruff` for the Python scripts, and `PSScriptAnalyzer` for the PowerShell scripts.

To check the same things locally before pushing:

```powershell
# Build
dotnet build src\FastAction\FastAction.csproj -c Release -p:Platform=x64

# C# style/format check (add `dotnet format FastAction.sln` without --verify-no-changes to auto-fix)
dotnet format FastAction.sln --verify-no-changes

# Python lint/format (scripts/build_icons.py)
pip install ruff
ruff check scripts/
ruff format --check scripts/

# PowerShell lint (run.ps1, scripts/*.ps1)
Install-Module -Name PSScriptAnalyzer -Scope CurrentUser
Get-ChildItem -Recurse -Filter *.ps1 -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    ForEach-Object { Invoke-ScriptAnalyzer -Path $_.FullName -Settings .\PSScriptAnalyzerSettings.psd1 }
```
