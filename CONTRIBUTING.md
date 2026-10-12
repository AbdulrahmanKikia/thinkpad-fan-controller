# Contributing

The application code is MIT. `third_party/pawnio` is LGPL-2.1-or-later and stays that way. Read [README.md](README.md) for what the program does, and [DOCUMENTATION.md](DOCUMENTATION.md) for what it refuses.

This program writes bytes to a laptop's embedded controller. A change that looks small can leave a fan stopped or pinned. Keep the limits below, and prove a new machine on that machine.

## Build and test

Windows, with the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bat
dotnet test LibraryFan.sln
dotnet build LibraryFan.sln -c Release
```

The fan app has no `LibraryFan.exe`. Start it with:

```bat
dotnet exec src\LibraryFan\bin\Release\net8.0-windows\LibraryFan.dll --simulate
```

`--simulate` does no port I/O. Use it for the window. Do not heat a laptop to 80°C to try the handoff; that path is covered by `--self-test` on the fake controller.

Hardware mode needs administrator rights and the signed driver from [pawnio.eu](https://pawnio.eu). It starts in BIOS mode and does not command the fans until Library or Manual is chosen.

## Add a machine you have measured

The built-in map is the ThinkPad P1 Gen 3. The menu's other ThinkPads use that same register list only when the product code matches, because the upstream dual-fan tool named those models. Copying the P1 temperature list onto a marketing name is not a new map.

If you know another machine's registers:

1. Start from [hardware-profile.example.json](hardware-profile.example.json). Name the file `hardware-profile.json` and put it next to `LibraryFan.dll`, or under `%LocalAppData%\LibraryFan\`.
2. Set `product` to that computer's exact product string. The file is ignored when the product does not match, and a broken file is not replaced by the P1 map.
3. Change only registers you have read on that machine, or that you can cite from a project that already ran there. Say which, in the pull request.
4. Add a test in `src/LibraryFan.Tests/HardwareProfileTests.cs` that the profile loads for that product and is refused for a different one.

A profile may set the fan-select register, the fan selectors (one or two), the command register, the speed word, and the temperature bytes. It may name a BIOS substring. It may not:

- use any port other than data `0x62` and control `0x66`
- set `biosControl`, `disengagedFullSpeed`, `dangerCelsius`, or `maxLevel`
- raise `staleRpmAbove` above 8191
- set bit 6 (`0x40`) or change the firmware command `0x80`
- raise the 80°C handoff, or hold level 0 at or above 55°C

Dell, HP, and Framework stay in the menu with no map. Do not point them at the ThinkPad registers.

A catalog entry in `KnownMachines` is for a model whose product codes are public and whose map is the shared ThinkPad list, or a map you measured. An unknown product must keep its fans with firmware.

## What a pull request should include

- The reason, and the machine or the bug.
- `dotnet test LibraryFan.sln` passing.
- For a register change, the source of the bytes: a reading from that machine, or a link to the project that already used them. Do not invent offsets.
- No secrets, and no `hardware-profile.json` copied from a running machine unless its product is the one the change is for. The example file stays `hardware-profile.example.json`.
