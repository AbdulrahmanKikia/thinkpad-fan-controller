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
- Temperatures are the bytes at `0x78`–`0x7F` and `0xC0`–`0xC3`. Bytes `0x00` and `0x80` are missing, not temperatures. 80°C is decimal 80 (`0x50`).
- A level write goes to both fans: select, write `0x2F`, read it back, retry up to five times.

## Driver

PawnIO is the only driver. Do not reference WinRing0, TVicPort, or any other unsigned port driver. `--simulate` uses `SimulatedEc` and performs no port I/O.

PawnIO call details that have actually succeeded on this machine belong in `reference.md` next to this skill. Do not put ioctl numbers in this file.
