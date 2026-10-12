---
name: thinkpad-ec
description: ThinkPad P1 Gen 3 embedded-controller map for fan RPM, temperatures, and fan commands. Use when reading or writing fan registers, RPM, temperatures, PawnIO, or the EC client.
---

# ThinkPad EC

This app runs on one machine: ThinkPad P1 Gen 3 `20TH000XUS`, BIOS `N2VET41W (1.26)`, i7-10750H, Intel UHD plus NVIDIA Quadro T1000 Max-Q.

Register offsets are copied from `nikolasgd/TPFanControl-dual-fan` `fancontrol/fanstuff.cpp`. Do not invent offsets.

## I/O ports

This machine's ECDT (`LENOVO TP-N2V`) places the ACPI embedded controller at data port `0x62` and control port `0x66`. Use those ports.

The dual-fan fork reaches the same command protocol through `0x1600` and `0x1604` on machines whose ports were relocated. That pair is not the port pair on this BIOS. PawnIO's signed `LpcACPIEC` module only allows `0x62` and `0x66`.

Commands, from the ACPI embedded-controller spec and the fork's `portio.cpp`:

- Control status: bit 0 output-buffer full, bit 1 input-buffer full.
- Read: wait until the input buffer is empty, write `0x80` to `0x66`, write the offset to `0x62`, wait until the output buffer is full, read `0x62`.
- Write: wait until the input buffer is empty, write `0x81` to `0x66`, write the offset to `0x62`, write the value to `0x62`.
- Hold the `Global\Access_EC` mutex across each transaction.

## Registers

- `0x31` selects the fan. `0` is fan 1. `1` is fan 2.
- `0x2F` commands the selected fan. `0x80` hands control to BIOS. `0` through `7` are manual levels.
- Never set bit 6 (`0x40`). That bit is disengaged full speed.
- RPM is a 16-bit word. Select the fan, read `0x84` first, then `0x85`. Values above `0x1FFF` are stale and do not count as a live reading. `0` is a real stopped fan.
- Temperatures are the bytes at `0x78`–`0x7F` and `0xC0`–`0xC3`. Bytes `0x00` and `0x80` are missing, not temperatures. 80°C is decimal 80 (`0x50`). On this machine `0x78`, `0x7A`, `0x7C`, `0x7D`, and `0x7E` read as temperatures; the rest read as missing, and `0xC2` once read 1°C, so a byte can pass the missing rule and still be nonsense.
- A level write goes to both fans: select, write `0x2F`, read it back, retry up to five times.

## Sharing the ports

The Windows ACPI driver uses the same two ports, so a single byte read can time out or come back stale. Treat that as normal, not as a disconnect:

- Settle before each transaction: wait for the input buffer to clear, and if the output buffer is still full, read the data port once to drain the stale byte.
- Retry each byte up to three times before giving up on that byte.
- A byte that still fails leaves that one register unread. The rest of the snapshot stands. Three consecutive failed registers end the snapshot early.
- A snapshot is readable when any register answered. Three unreadable snapshots in a row are the disconnect that hands control back.
- Poll once a second.

## Driver

PawnIO is the only driver. Do not reference WinRing0, TVicPort, or any other unsigned port driver. `--simulate` uses `SimulatedEc` and performs no port I/O.

PawnIO call details that have actually succeeded on this machine belong in `reference.md` next to this skill. Do not put ioctl numbers in this file.

## A profile for another machine

The same protocol can be aimed at another computer only by a file a person wrote for that computer. `hardware-profile.json` beside `LibraryFan.dll`, or in `%LocalAppData%\LibraryFan\`, is read before the built-in map. It is used only when its `product` matches the machine that is running. It can relocate `fanSelect`, the fan selectors, `fanCommand`, the speed word, and the temperature bytes. It cannot change the firmware command `0x80`, set bit 6, raise the 80°C handoff, or name ports other than `0x62` and `0x66`. Without a matching file, the setup menu can apply that same copied map only to a ThinkPad whose product type is already known: P1 Gen 3 (`20TH`, BIOS contains `N2VET41W`), X1 Extreme Gen 3 (`20TK`, `20TL`), P15 Gen 2 (`20YQ`, `20YR`), and P14s Gen 5 Intel (`21G2`, `21G3`). Dell, HP, and Framework are in the menu and are never given this map. Any other machine is left with firmware. Do not invent offsets for a machine that is not in that list; a file the person wrote is how a further machine is named. `hardware-profile.example.json` is this machine's map, written out so the fields are visible.
