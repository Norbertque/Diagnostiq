# Diagnostiq

A portable Windows tool for checking any laptop (Dell, Lenovo, HP, ASUS, Acer, MSI, …). Use it on
a used laptop before buying or selling it, or on your own laptop when something feels off. It
reads the specs and identity, checks Windows 11 readiness, stress-tests the hardware, walks you
through the screen, keyboard, touchpad, audio, camera and ports, and ends with a health score and a
printable report.

One `Diagnostiq.exe`, nothing to install. It runs from a USB stick.

> The earlier WinForms version (Dell refurbishment edition with Excel inventory import) is kept at
> tag **`v1.0-refurb`**.

## What it checks

**At startup (about 5 seconds, the window never freezes)**
- Make, model and serial number (with the vendor's own wording, such as Service Tag), BIOS, processor,
  memory modules, graphics, display panel, storage health, battery wear and charge cycles, Wi-Fi,
  Bluetooth and TPM.
- Windows edition, version, activation, BitLocker, antivirus and devices with driver problems.
- **Windows 11 readiness**, requirement by requirement. The processor is matched against Microsoft's
  supported lists, and TPM, Secure Boot, UEFI, DirectX 12, memory, storage and the screen are checked.
  Each failed requirement has a fix, including the BIOS key for that vendor.

**Automatic check (fullscreen, guided)**
1. Quick checks: Wi-Fi scan, network adapters and internet connection.
2. **Stress test** (2, 5 or 15 minutes). The processor, memory pattern test and disk surface scan run
   at the same time, with live temperature, clock speed, fans and throttling detection. There is an
   optional battery drain test.
3. Disk speed on an idle system.
4. Hands-on slides:
   - screen colours and dead pixels, and brightness;
   - keyboard: every key, including Esc, Win, Alt+Tab and Alt+F4, is tested and not acted on;
   - touchpad coverage, clicks and scrolling;
   - left and right speakers plus a frequency sweep, headphone jack and microphone level;
   - camera;
   - USB ports (plug something into each port);
   - charger unplug and replug.
5. Summary with the health score. The report is saved on its own.

Navigation is by mouse or touchpad only, and Esc never quits. You can skip any step.

**Manual mode:** a dashboard where every test, the stress test (choose parts and length) and every
slide can be run on its own, plus full hardware details, live sensors, Windows 11 readiness, and
Windows & security status.

## Health score

The score starts at 100.
- **Critical faults** cost 30 points and always give a *Needs repair* verdict. These are:
  - a failing drive or disk surface errors;
  - memory errors;
  - a faulty screen or keys;
  - thermal throttling under load;
  - a battery below 40 % of its original capacity.
- **Major faults** cost 15 points: speakers, touchpad, camera, microphone, USB, charger, Wi-Fi, or a
  battery at 40–60 %.
- **Minor issues** cost 5 points: brightness, headphone jack, a slow disk, driver problems, no
  Bluetooth, and warnings.

Verdicts: **Excellent** at 85 and above, **OK** at 60–84, **Needs repair** below 60. Skipped and
untested items are listed but cost nothing. Windows 11 readiness is reported separately and does not
change the score.

## Report

Every Automatic run saves a report, and you can save one from the Manual mode *Report* page at any
time. Each report is two files:
- an **HTML page** that is self-contained, opens in any browser and prints cleanly (use *Print → Save as PDF*);
- a **JSON file** with the same data, for scripts and inventories.

Reports go to a `Reports` folder next to `Diagnostiq.exe`. If that folder can't be written to, for
example on a read-only stick, they go to `Documents\Diagnostiq Reports`.

The report contains the laptop's serial number. Nothing is uploaded anywhere. The only network
traffic is the connectivity test, which pings the gateway, 8.8.8.8, 1.1.1.1 and google.com.

## Requirements

- Windows 10 or 11, 64-bit (x64). **Windows 7 and 8.1 are not supported**, because .NET 10 doesn't
  run there.
- Administrator rights. The exe asks for them when it starts. They are needed for TPM details,
  BitLocker, drive wear, exact temperatures and the disk surface scan. Without them the app still
  runs in a limited mode.

## Sensors and PawnIO

Exact CPU temperature and fan speed need the signed [PawnIO](https://pawnio.eu) driver.
LibreHardwareMonitor uses PawnIO since it dropped WinRing0, which Windows Defender flags. Diagnostiq
bundles the official PawnIO 2.2.0 setup, unmodified (GPL-2.0, see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)). On a laptop without the driver, the app asks
before installing it and offers to remove it again when you close the app.

Without PawnIO, Diagnostiq uses ACPI thermal zones where the firmware has them, and labels the value
"limited". CPU load and real clock speed, which throttling detection needs, come from Windows
performance counters and work everywhere.

## Build

You need the .NET 10 SDK on Windows 10 or 11.

```powershell
.\build.ps1            # build + test
.\build.ps1 -Publish   # also produce publish\Diagnostiq.exe (single portable file)
```

Debug builds run without elevation, so admin-only checks show "Needs admin". The Debug exe also has
a screenshot tool for checking layouts without clicking through the app:

```powershell
Diagnostiq.exe --snapshot out.png [--theme dark] [--size 1280x800] [--full] [--session]
               [--view loading|home|win11|auto|auto-summary|manual-<tests|hardware|win11|windows|report>]
               [--from <StepClass>] [--delay <seconds>]
```

## Layout

| Path | What |
|---|---|
| `src/Diagnostiq.Core` | UI-free services: hardware probes, Windows 11 rules, stress and storage tests, scoring, report |
| `src/Diagnostiq` | WPF app (WPF-UI / Fluent) |
| `tests/Diagnostiq.Core.Tests` | unit tests for the Core logic |
| `docs/design-tokens.md` | colours, type, spacing; contrast measurements |

## Before a release

- Recheck Microsoft's supported-processor lists (Intel, AMD and Qualcomm on learn.microsoft.com) and
  update `SupportedCpus.cs` and `win11-amd-models.txt`. The app shows the date of the list it uses.
- Run `.\build.ps1 -Publish` and attach `publish\Diagnostiq.exe` to the GitHub release.
