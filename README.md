# Fast Action

[![CI](https://github.com/Luttik/fast-action/actions/workflows/ci.yml/badge.svg)](https://github.com/Luttik/fast-action/actions/workflows/ci.yml)
[![Release](https://github.com/Luttik/fast-action/actions/workflows/release.yml/badge.svg)](https://github.com/Luttik/fast-action/actions/workflows/release.yml)

A PowerToys-inspired Windows keyboard overlay. Press a global hotkey to open a QWERTY-aligned action grid. Keys and clicks run shell commands or open nested grids.

## Requirements

- Windows 10 1809+ / Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows App SDK runtime (bundled via self-contained build)

## Install

Once the first release has gone through the one-time WinGet setup described in [Releases & distribution](#releases--distribution):

```powershell
winget install Luttik.FastAction
```

Until then, or if you'd rather grab it directly, download the latest `FastActionSetup-*.exe` from [Releases](https://github.com/Luttik/fast-action/releases) and run it — it's a normal machine-wide installer (via Inno Setup; requires admin) with a Start Menu shortcut, per-user startup registration (`HKCU\...\Run`), and a clean uninstaller. No separate .NET or Windows App SDK runtime install is required; the app is self-contained.

## Run from source

You can also build and run the unpackaged app directly.

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
- **Start with Windows** — toggle launching automatically at sign-in (checked by default)
- **Exit**

## Config

On first launch the app copies an example config to:

`%LOCALAPPDATA%\FastAction\config.yaml`

Edits are watched and reloaded live.

`runOnStartup` (default `true`) is applied via a per-user `HKCU\...\CurrentVersion\Run` registry entry. The installer writes that entry so silent installs (including WinGet) register startup even before the first launch; the running app then keeps the same value pointed at the current exe and in sync with this setting — on every launch, when you flip **Start with Windows** in the tray menu, or when you edit the config directly.

### Schema

```yaml
hotkey:
  modifiers: [Win, Shift]   # Win, Ctrl, Alt, Shift
  key: Space                # Space, Tab, Enter, Esc, A–Z, 0–9, F1–F12
rootGridId: home
editOnRightClick: true      # right-click a tile to edit or clear it
runOnStartup: true          # launch automatically at Windows sign-in
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
  Assets/config.example.yaml   shipped first-run template
```

## Releases & distribution

Releases are cut by pushing a `v*.*.*` tag. Two ways that happens:

- **Automatically**: [.github/workflows/version-bump.yml](.github/workflows/version-bump.yml) bumps the minor version (`X.Y.Z` -> `X.(Y+1).0`) and pushes a new tag every time a PR is merged into `main`.
- **Manually**, e.g. for a patch release:
  ```powershell
  git tag v1.2.3
  git push origin v1.2.3
  ```

Either way, the tag push triggers [.github/workflows/release.yml](.github/workflows/release.yml), which:

1. Publishes a self-contained Release build (`dotnet publish`, currently `win-x64`; other `Platforms` in the csproj can be added to the build matrix later).
2. Compiles it into `FastActionSetup-<version>-x64.exe` with [Inno Setup](https://jrsoftware.org/isinfo.php) (`installer/FastAction.iss`) — a normal Windows installer with a Start Menu shortcut, per-user `HKCU\...\Run` startup registration, and a proper uninstaller.
3. Publishes a GitHub Release with that installer attached and auto-generated release notes.
4. Opens a PR against the [WinGet Community Repository](https://github.com/microsoft/winget-pkgs) (via [winget-releaser](https://github.com/vedantmgoyal9/winget-releaser)) so `winget install Luttik.FastAction` picks up the new version automatically.

(The WinGet step lives in the same workflow rather than triggering off `release: published`, because GitHub Actions doesn't fire other workflows off events created by the default `GITHUB_TOKEN`.) The installer is currently unsigned, so Windows SmartScreen may warn on first run until the file builds up reputation; see "Code signing" below if that becomes a priority.

**Status**: [v0.0.1](https://github.com/Luttik/fast-action/releases/tag/v0.0.1) is out. First WinGet submission is open: [microsoft/winget-pkgs#408285](https://github.com/microsoft/winget-pkgs/pull/408285). Once that merges, `winget install Luttik.FastAction` works and later releases auto-update via `release.yml`.

### WinGet setup (one-time, manual)

`winget-releaser` can only *update* a package that's already in `winget-pkgs` — it can't create the first submission. Status:

1. **Fork `microsoft/winget-pkgs`** — done: [Luttik/winget-pkgs](https://github.com/Luttik/winget-pkgs).
2. **Create a classic GitHub PAT** with the `public_repo` scope and store it as repo secret `WINGET_TOKEN` — done.
3. **Submit the first manifest** — done / awaiting merge: [PR #408285](https://github.com/microsoft/winget-pkgs/pull/408285).

After that PR merges, every subsequent tagged release keeps WinGet in sync automatically via the `winget` job in `release.yml`.

### Code signing

The installer ships unsigned today. Two low/no-cost options to reduce SmartScreen friction later:

- [SignPath.io's free OSS program](https://signpath.io/pricing) — free code signing for open-source projects, integrates with GitHub Actions.
- A low-cost OV certificate (e.g. Certum ~$25-30/yr) added as a GitHub secret and wired into the `release.yml` build step.

Neither is required for WinGet distribution to work.

### Microsoft Store (future work)

The project already ships Store-ready assets (`Package.appxmanifest`, `StoreLogo.png`, tile logos) with a placeholder identity, but nothing is wired up yet. Getting there requires steps only the account owner can do:

1. Create a [Microsoft Partner Center](https://partner.microsoft.com/dashboard) developer account (one-time fee + identity verification).
2. Reserve the app name to get a real `Package/Identity/Name` and `Publisher` — update `Package.appxmanifest` with those values.
3. Register an Azure AD app associated with the Partner Center account to get submission-API credentials (tenant ID, client ID, client secret).
4. Add a CD job that builds an MSIX/MSIX bundle (`-p:WindowsPackageType=MSIX`) and submits it via the Microsoft Store submission API/CLI.

Once published, `winget` also picks up the Store listing automatically via its `msstore` source — no extra WinGet-side work needed for that path.

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
