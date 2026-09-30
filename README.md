# Diagnostiq

Portable Windows tool that checks any laptop (Dell, Lenovo, HP, ASUS, …): specs and identity,
Windows 11 readiness, battery, storage health, stability under load, display, keyboard,
touchpad, audio, webcam, USB and network, with a printable report at the end.

> **Status:** v2 is being rebuilt in WPF. Working so far: hardware detection, Windows 11
> readiness, the startup splash and loading screen, and the Home screen. Automatic and Manual
> test modes come next. The previous WinForms version (Dell refurbishment edition with Excel
> inventory import) is preserved at tag **`v1.0-refurb`**.

## Layout

| Path | What |
|---|---|
| `src/Diagnostiq.Core` | UI-free services: hardware probes, stress/storage tests, scoring, report |
| `src/Diagnostiq` | WPF app (WPF-UI / Fluent) |
| `tests/Diagnostiq.Core.Tests` | unit tests for Core logic |
| `docs/design-tokens.md` | colors, type, spacing; contrast measurements |

## Sensors and PawnIO

Exact CPU temperature and fan speed need the signed [PawnIO](https://pawnio.eu) driver (used by
LibreHardwareMonitor since it dropped WinRing0, which Windows Defender flags). Diagnostiq bundles
the official PawnIO 2.2.0 setup, unmodified (GPL-2.0, see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)). On a laptop without it, the app asks before
installing and offers to remove it again when you close the app.

Without PawnIO, Diagnostiq falls back to ACPI thermal zones where the firmware has them and
labels the value "limited". CPU load and real clock speed (throttling) come from Windows
performance counters and work everywhere.

## Build

Requires the .NET 10 SDK on Windows 10/11.

```powershell
.\build.ps1            # build + test
.\build.ps1 -Publish   # also produce publish\Diagnostiq.exe (single portable file)
```

The release exe asks for administrator rights (TPM, sensors, raw disk reads). Debug builds run
without elevation; admin-only checks then report "Needs admin".
