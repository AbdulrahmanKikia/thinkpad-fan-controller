# ThinkPad Fan Controller Utility

ThinkPad Fan Controller Utility is a Windows fan controller for a ThinkPad whose embedded-controller map is already known. The project was formerly called Library Fan. The name was changed so the purpose is clear to anyone reading the repository.

The map in this repository was copied from other people's work, then read on one ThinkPad P1 Gen 3 (`20TH000XUS`, BIOS `N2VET41W`). It can hold both fans to one level, show the measured speeds and the temperature registers, and hand the fans back to firmware at 80°C. It cannot command an unknown laptop, and it cannot command Dell, HP, or Framework.

Read [DOCUMENTATION.md](DOCUMENTATION.md) for what it does, what it refuses, and the difficulties in order. [POSTMORTEM.md](POSTMORTEM.md) is the lab record from the first two days.

## Built on other people's work

The register map is copied from [nikolasgd/TPFanControl-dual-fan](https://github.com/nikolasgd/TPFanControl-dual-fan) `fancontrol/fanstuff.cpp`. That project is a fork of [byrnes/TPFanControl](https://github.com/byrnes/TPFanControl) and points at the original [TPFanControl](https://sourceforge.net/projects/tp4xfancontrol/). Port access is the signed driver from [pawnio.eu](https://pawnio.eu). The vendored `LpcACPIEC.bin` is from [namazso/PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) 0.2.11, LGPL-2.1, with `third_party/pawnio/COPYING` beside it. Those projects are the source of the protocol. This repository did not invent it. The application code is under the MIT License in [LICENSE](LICENSE). `third_party/pawnio` remains LGPL-2.1-or-later.

## Run

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) and the signed driver from [pawnio.eu](https://pawnio.eu). Then double-click:

```bat
Open ThinkPad Fan Controller Utility.cmd
```

That builds the Release output on first use and starts `LibraryFan.dll` under the signed `dotnet.exe`. There is no `LibraryFan.exe`: the project sets `UseAppHost=false` because Smart App Control blocks an unsigned apphost on this PC. The first launch asks which computer to command and whether to start with Windows. Either choice leaves the panel hidden in the notification area; open it from the tray icon. Without administrator rights the panel opens on its own and offers **Restart as administrator**. It starts in BIOS mode and does not command the fans until you choose Library or Manual.

## Give someone a folder

```bat
Package ThinkPad Fan Controller Utility.cmd
```

That publishes into `dist\LibraryFan`, zips `dist\LibraryFan-try.zip`, and builds `dist\LibraryFanSetup.exe`. The folder contains `LibraryFan.dll`, `Open ThinkPad Fan Controller Utility.cmd`, `LpcACPIEC.bin`, the LGPL `COPYING` for that module, `hardware-profile.example.json`, and `Read this first.txt`. The person needs the .NET 8 Desktop Runtime and the PawnIO driver from [pawnio.eu](https://pawnio.eu). They do not need this repository.

`LibraryFanSetup.exe` is the file to download. It fetches `LibraryFan-try.zip` from the latest release of [AbdulrahmanKikia/library-fan](https://github.com/AbdulrahmanKikia/library-fan) and installs the DLL folder under `%LocalAppData%\LibraryFan\App`, with a Start menu item that still starts `dotnet exec LibraryFan.dll`. The repository is private and has no release yet, so that download returns 404 until a public release contains the zip. Smart App Control can block the unsigned setup exe on this PC.

```bat
dotnet exec src\LibraryFan\bin\Release\net8.0-windows\LibraryFan.dll --simulate
```

`--simulate` draws the same window against a fake controller and performs no port I/O. `--self-test` runs the 80°C handoff and the read-failure cases on that fake controller and exits with 0 when they pass.

On this P1 Gen 3 with BIOS `N2VET41W`, no extra file is required. Any other machine keeps its fans with firmware until someone who knows that machine's embedded-controller map adds `hardware-profile.json` next to `LibraryFan.dll` (or under `%LocalAppData%\LibraryFan\`). The file has to name that machine's product. It can move the fan-select register, the fan selectors, the command register, the speed word, and the temperature bytes. It cannot change the firmware byte `0x80`, set bit 6, raise the 80°C handoff, or use ports other than `0x62` and `0x66`, which are the only ports PawnIO opens. [hardware-profile.example.json](hardware-profile.example.json) is this laptop's map, for the shape of the file. Copying it onto a different model unchanged does nothing, because the product will not match.

To look at the window without a screen capture, which returns black once the app is elevated, let the app render itself:

```bat
dotnet exec src\LibraryFan\bin\Release\net8.0-windows\LibraryFan.dll --simulate --snapshot %TEMP%\library-fan.png --snapshot-after 10 --mode library --sim-temp 66
```

`--snapshot-after` waits that many seconds of samples first. `--sim-temp` sets the fake `0x78`. `--mode library|manual` starts in that mode. The last two do nothing without `--simulate`.

## The window

- Two gauges on a fixed 0–6000 RPM scale, one per fan, filled with measured speed. The rotor inside turns at the measured speed. `0` is a stopped fan. A speed that failed to read is shown dimmed as `LAST READ` for three polls, then as `—`.
- A sentence that says what is happening and why: "Library is holding level 2 because 0x78 is 64°C.", "Manual is holding level 1.", "BIOS is in control.", or "Handed control back to the machine because a sensor reached 80°C."
- A three-minute trace with fixed scales: hottest sensor on the left axis, faster fan on the right, a dashed line at 80°C.
- Twelve sensor tiles labelled by register address with a heat bar each. Teal below 65°C, amber from 65°C, coral at 80°C.
- Mode buttons, the eight `0x2F` levels, and the Library curve as chips with the held step outlined.
- A link pill: `LIVE`, `PARTIAL`, `NO ANSWER`, or `OFFLINE`, beating once per one-second poll. The footer says how many of the 14 registers answered.

## Safety

- Register `0x2F` accepts `0x80` (firmware) or levels `0` through `7`. Bit 6 (`0x40`) is never set.
- One valid sensor at 80°C writes `0x80` to both fans and latches BIOS until every valid sensor is cooler and you choose Library or Manual again.
- Bytes `0x00` and `0x80` on a temperature register are missing readings, not temperatures.
- Three unreadable polls in a row write `0x80` and latch BIOS. One failed byte does not fail the poll.
- Levels are written only after both fan speeds have been read live in this session. A held speed does not count.
- Exit, shutdown, and sleep restore `0x80`. Killing the process from Task Manager can leave the last command in the controller.

## Layout

| Path | What |
|---|---|
| `src/LibraryFan.Core` | Registers, the retrying EC bus, snapshots, the session guard, the curve, the controller, the simulator, the PawnIO client |
| `src/LibraryFan` | WPF window, gauges, tray, self-test, in-process snapshot |
| `src/LibraryFan.Tests` | Safety, protocol, reading, and hardware-probe tests (the probe runs only when elevated) |
| `src/LibraryFan.UiTests` | Opens the window on a fake controller and runs the self-test |
| `third_party/pawnio` | `LpcACPIEC.bin` from PawnIO.Modules 0.2.11 and its LGPL-2.1 `COPYING` |
| `.cursor/skills` | The rules this code follows: `thinkpad-ec`, `fan-safety`, `library-fan-ui` |

```bat
dotnet test LibraryFan.sln
```
