# Viska Tweak

Single-file Windows desktop app: finds and clears game cache junk (FiveM first), reads the PC's
hardware, applies a low-end settings preset, and shows an estimated before/after FPS. Gated behind a
username + license key.

Built around a low-end baseline: GT 1030 / 8 GB RAM / 4-core CPU is the default profile.

## Layout

```
src/ViskaTweak/            .NET 8 + WPF app
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
   - `src/ViskaTweak/Services/LicenseService.cs` → `BuildSecret`
   - the `<secret>` argument you pass to `licensegen`

2. Generate keys and pack them:

```bash
dotnet run --project tools/LicenseGen -- new my-build-secret friendname 5 > users.txt
dotnet run --project tools/LicenseGen -- pack my-build-secret users.txt src/ViskaTweak/licenses.dat
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
dotnet publish src/ViskaTweak/ViskaTweak.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true
```

Output: `src/ViskaTweak/bin/Release/net8.0-windows/win-x64/publish/ViskaTweak.exe`. Trimming is off
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

## Features

**Dashboard** — WMI hardware detection and a Low/Medium/High tier, live CPU and memory meters
sampled from the kernel counters, an animated FPS estimate gauge, and a breakdown of reclaimable
space by category.

**Cache cleaner** — FiveM (cache, server assets, NUI, crashes, logs), Windows temp, DirectX and
GPU-vendor shader caches, and Steam libraries scanned by folder-name pattern so unknown games are
covered too.

**Game optimizer** — rewrites GTA V `settings.xml` from a preset. Potato holds nothing back: every
dial at its floor, every distance at zero, deferred lighting and fog volumes off. It looks bad on
purpose, and it is for a machine that cannot otherwise hold a playable frame rate. The low profile also cuts
ped and vehicle variety, extended distance scaling and streaming, which is what spikes frame times
on a busy server.

**FPS boost** — all per-user and reversible:
- Desktop animations, window shadows and transparency off. Windows composites those on the same
  GPU the game wants, which is not free on an integrated chip or a GT 1030.
- Startup app manager. Disabling one parks the entry in our own key rather than deleting it, so
  enabling restores exactly what was there. On 8 GB this is the largest recoverable chunk of memory
  available, and worth more than any graphics setting.

FiveM-specific:
- Fullscreen optimisations off, which evens out frame times rather than raising the average
- High-performance GPU preference, for laptops with two graphics chips
- Process priority raised while the game is running (above normal, not high - high starves audio
  and input and feels worse)
- ReShade: performance mode, stripping expensive techniques from the active preset, or switching
  the loader off entirely by renaming its DLL. ReShade costs frames and never adds them, so every
  option here makes it cheaper or removes it.

## Installing it on someone's PC

Give them `ViskaTweak.exe` and either:

- open it and use **License → Install on this PC**, or
- run `install.bat` next to the exe.

Both copy it to `%LocalAppData%\Viska Tweak`, add a Start menu and desktop shortcut, and register
it in Add or Remove Programs. Everything is per-user (LocalAppData and HKCU) so no administrator
rights are needed and no other account on the machine is touched.

Uninstall from Add or Remove Programs, or from the same page in the app. Settings, licence and
history are left alone; the install folder has to be deleted by hand, because a running program
cannot delete itself.

## Safety behaviour

- Nothing is deleted without a confirmation dialog that names every folder and the total size.
- Only caches that the game or Windows rebuilds by itself are listed. Files locked by a running game
  are skipped, not forced.
- Settings files are copied to `<file>.viskatweak.bak` before the first write, and the backup is
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
