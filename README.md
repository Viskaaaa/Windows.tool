# Rig Booster

Single-file Windows desktop app: finds and clears game cache junk (FiveM first), reads the PC's
hardware, applies a low-end settings preset, and shows an estimated before/after FPS. Gated behind a
username + license key.

Built around a low-end baseline: GT 1030 / 8 GB RAM / 4-core CPU is the default profile.

## Layout

```
src/RigBooster/            .NET 8 + WPF app
  Services/                LicenseService, HardwareService, CacheScanner, OptimizerService,
                           FpsEstimator, ThemeService, AppState
  Views/                   LicenseWindow, DashboardView, CacheCleanerView, GameOptimizerView,
                           SettingsView
  Themes/                  Palette.Red, Palette.HighContrast, Controls
tools/LicenseGen/          console tool that builds the encrypted licenses.dat
```

## Build

Windows only — WPF does not compile on macOS or Linux. Needs the .NET 8 SDK.

1. Pick a build secret and put the same string in two places:
   - `src/RigBooster/Services/LicenseService.cs` → `BuildSecret`
   - the `<secret>` argument you pass to `licensegen`

2. Generate keys and pack them:

```bash
dotnet run --project tools/LicenseGen -- new my-build-secret friendname 5 > users.txt
dotnet run --project tools/LicenseGen -- pack my-build-secret users.txt src/RigBooster/licenses.dat
```

Or write `users.txt` by hand — one `username,key` per line. A key is 4-32 letters and/or digits and
is matched case-insensitively, so word keys like `giorgakis` work as well as generated 6-digit ones.
The format lives in `LicenseService.IsWellFormedKey` and `LicenseGen.IsWellFormedKey`; if you change
one, change both or the hashes stop lining up.

`users.txt` is the only copy of the plaintext keys — the `.dat` stores `username|sha256(lowercased key)`, then
encrypts the whole table with AES-256 (PBKDF2-SHA256, 200k iterations, per-file salt). Keep
`users.txt` out of git; `.gitignore` already excludes it.

3. Publish the single exe:

```bash
dotnet publish src/RigBooster/RigBooster.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true
```

Output: `src/RigBooster/bin/Release/net8.0-windows/win-x64/publish/RigBooster.exe`. Trimming is off
on purpose — WPF and `System.Management` both reflect, and a trimmed build fails at runtime.

## Things worth being straight about

**The license check is not real DRM.** The AES key is baked into the exe, and .NET IL decompiles
easily. This stops someone opening the file in Notepad; it does not stop anyone determined. If it
has to be airtight, the key list must live on a server and the app has to validate online.

**SmartScreen will flag it.** An unsigned single-file .NET exe trips Windows SmartScreen and some
antivirus heuristics on first run. That is normal for indie tools. A code-signing certificate is the
only real fix.

**The FPS figures are estimates, not measurements.** `FpsEstimator` models a number from the
hardware tier and which changes were applied, and the UI says so on the card. To make them real,
replace that class with a PresentMon capture before and after, parse the CSV, and flip
`FpsEstimate.IsEstimate` to false.

**WMI under-reports VRAM.** `Win32_VideoController.AdapterRAM` is a 32-bit field, so cards with more
than 4 GB report 4 GB. The tier classifier leans on RAM, thread count and the GPU name for that
reason.

## Safety behaviour

- Nothing is deleted without a confirmation dialog that names every folder and the total size.
- Only caches that the game or Windows rebuilds by itself are listed. Files locked by a running game
  are skipped, not forced.
- Settings files are copied to `<file>.rigbooster.bak` before the first write, and the backup is
  never overwritten, so **Restore** always returns the pristine file.
- The three Windows tweaks are opt-in per checkbox and reversible from **Undo tweaks**.

## Accessibility

- Full keyboard operation: Tab order, Enter/Space, Esc closes dialogs, Ctrl+1–4 switch pages.
- Every control has an `AutomationProperties.Name`; status lines are live regions so screen readers
  announce scan and cleanup results.
- High-contrast theme (pure black/white, 21:1) alongside the dark red default, and a text size
  slider from 85% to 175% that rescales the whole UI.
- Status is never colour-only — the tier badge, error box and license pill each carry text and a
  glyph.
- Buttons, checkboxes, radios and nav items are all at least 40px tall.
