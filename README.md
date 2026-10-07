# Library Fan

Library Fan is a Windows controller for one laptop: a ThinkPad P1 Gen 3, model `20TH000XUS`, BIOS `N2VET41W (1.26)`. It keeps the fans quieter while the machine is cool, shows both fans and the temperature registers live, and gives the fans back to firmware if a sensor reaches 80°C.

It is not a general ThinkPad utility. Other models use other ports and other registers. Writing the wrong byte can leave a fan stuck until reboot.

Read [POSTMORTEM.md](POSTMORTEM.md) before publishing or running this on hardware.

## Run

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) and the signed driver from [pawnio.eu](https://pawnio.eu). Then:

```bat
Open Library Fan.cmd
```

That starts `LibraryFan.dll` under `dotnet.exe`. Double-clicking `LibraryFan.exe` fails on this PC because Smart App Control blocks the unsigned apphost. Approve the administrator prompt. The window opens in BIOS mode and does not command the fans until you choose Library or Manual.

```bat
dotnet exec src\LibraryFan\bin\Debug\net8.0-windows\LibraryFan.dll --simulate
```

`--simulate` draws the same window against a fake controller. It does not touch the hardware. `--self-test` runs the 80°C handoff on that fake controller and exits.

## Safety

- Register `0x2F` accepts `0x80` (firmware) or levels `0` through `7`. Bit 6 (`0x40`) is never set.
- One valid sensor at 80°C writes `0x80` to both fans and latches BIOS until every valid sensor is cooler and you choose Library or Manual again.
- Bytes `0x00` and `0x80` on a temperature register are missing readings, not temperatures.
- Exit, shutdown, and sleep restore `0x80`. Killing the process from Task Manager can leave the last command in the controller.
