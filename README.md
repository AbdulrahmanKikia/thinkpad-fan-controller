# Library Fan

Library Fan is a Windows controller for one laptop: a ThinkPad P1 Gen 3, model `20TH000XUS`, BIOS `N2VET41W (1.26)`. It keeps the fans quieter while the machine is cool, shows both fans and the temperature registers live, and gives the fans back to firmware if a sensor reaches 80°C.

It is not a general ThinkPad utility. Other models use other ports and other registers. Writing the wrong byte can leave a fan stuck until reboot.

Read [POSTMORTEM.md](POSTMORTEM.md) before publishing or running this on hardware.

## Run

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) and the signed driver from [pawnio.eu](https://pawnio.eu). Then double-click:

```bat
Open Library Fan.cmd
```

That builds the Release output on first use and starts `LibraryFan.dll` under the signed `dotnet.exe`. There is no `LibraryFan.exe`: the project sets `UseAppHost=false` because Smart App Control blocks an unsigned apphost on this PC. The window opens without hardware access and offers **Restart as administrator**; approve the prompt. It starts in BIOS mode and does not command the fans until you choose Library or Manual.

```bat
dotnet exec src\LibraryFan\bin\Release\net8.0-windows\LibraryFan.dll --simulate
```

`--simulate` draws the same window against a fake controller and performs no port I/O. `--self-test` runs the 80°C handoff and the read-failure cases on that fake controller and exits with 0 when they pass.

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
