---
name: fan-safety
description: Stops Library Fan from commanding the ThinkPad fans when that is unsafe. Use when changing Library mode, Manual mode, the safety guard, temperature thresholds, exit or sleep handling, or any write to register 0x2F.
---

# Fan safety

The safety guard sits in front of every write to `0x2F`. Library mode and Manual mode both go through it.

## Thermal handoff

One valid sensor at decimal 80°C is enough. Do not wait for another sample. The Library curve's 3°C step-down delay does not apply.

On that reading, before any other command:

1. Write `0x80` to both fans.
2. Switch the app to BIOS.
3. Latch there.

The sentence is "Handed control back to the machine because a sensor reached 80°C."

This is a firmware handoff. Do not set bit 6.

A sensor byte of `0x00` or `0x80` is missing. It is not 80°C.

Library and Manual stay refused while any valid sensor is still at or above 80°C. After every valid sensor is under 80°C, the user must explicitly choose Library or Manual again. Choosing BIOS is always accepted.

## Other stops

- Three unreadable polls in a row write `0x80` and latch BIOS. A single good reading at 80°C does not wait for that streak.
- Exit, shutdown, and sleep write `0x80` to both fans.
- Steady BIOS mode does not keep writing. Lenovo Vantage is left alone.
- Library never commands level 0 once any valid sensor is at or above 55°C. Manual may command a low level. The 80°C handoff takes that choice away.
- Level commands `0`–`7` stay disabled until both fan RPM values have been read live in this session. `0x80` is the restore command and is not a level.
- A held fan speed, shown from an earlier poll because the latest read failed, is display only. It never satisfies the both-fans gate and never counts as live.
- A level is written when it changes and refreshed every 30 seconds while it is held. `0x80` is written once per transition. A write the controller does not accept is retried on the next poll and the sentence says so.
- On hardware the app always starts in BIOS and takes over only when a mode is chosen in the window. `--mode` and `--sim-temp` act only with `--simulate`.
- `--simulate` performs no port I/O. Prove the cases below on the simulator. Do not heat the laptop to 80°C to test the trip.

## Cases the simulator must pass

- Manual, then one sensor at 80°C, commands `0x80` and latches.
- Library, then one sensor at 80°C, does the same.
- Choosing Library or Manual while a sensor is still at 80°C is refused.
- After the fake temperature drops, an explicit choice can leave BIOS.
- A sensor byte of `0x80` does not trip.
- Three bad polls latch BIOS. One bad poll does not.
