# Third-party notices

Diagnostiq.exe bundles the following components.

## PawnIO setup (bundled program)

`src/Diagnostiq.Core/Resources/PawnIO_setup.exe`, version 2.2.0, is the official, signed
installer of the PawnIO kernel driver by namazso, redistributed unmodified. It is a separate
program: Diagnostiq only extracts and runs it (`-install -silent`) after the user agrees, and
can remove it again (`-uninstall -silent`).

- License: GNU General Public License v2.0
- Source code: https://github.com/namazso/PawnIO (driver),
  https://github.com/namazso/PawnIO.Setup (installer), https://github.com/namazso/PawnIO.Modules
- Release: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0
- SHA-256: `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`

## Libraries

| Package | License | Source |
|---|---|---|
| LibreHardwareMonitorLib | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| WPF-UI | MIT | https://github.com/lepoco/wpfui |
| NAudio.Core, NAudio.Wasapi | MIT | https://github.com/naudio/NAudio |
| ManagedNativeWifi | MIT | https://github.com/emoacht/ManagedNativeWifi |
| AForge.Video.DirectShow | LGPL-3.0 | https://github.com/andrewkirillov/AForge.NET |
| System.Management | MIT | https://github.com/dotnet/runtime |
