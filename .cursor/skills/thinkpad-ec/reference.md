# PawnIO calls that succeeded on this machine

Confirmed on the ThinkPad P1 Gen 3 `20TH000XUS`, BIOS `N2VET41W (1.26)`, with PawnIO 2.2.0 and `LpcACPIEC.bin` from PawnIO.Modules 0.2.11.

The elevated probe read both fans and the temperature bytes, then Library mode stepped from level 3 at 73°C on `0x78` to level 4 at 77°C. Closing the probe wrote `0x80` and handed the fans back to firmware.

## Device

Open `\\?\GLOBALROOT\Device\PawnIO` with read/write access. Hold `Global\Access_EC` across each transaction.

## IOCTLs

These values come from PawnIOLib and succeeded here:

- Load module: `0xA1B22084`
- Execute: `0xA1B22104`

The execute buffer is a 32-byte function name followed by little-endian 64-bit inputs.

## Module functions

`LpcACPIEC.bin` allows only ports `0x62` and `0x66`, which matches this machine's ECDT.

- `ioctl_pio_read`: one input, the port. One output word, the byte in the low 8 bits.
- `ioctl_pio_write`: two inputs, the port and the byte. No output words.

A live read on this laptop returned fan 1 at 2563 RPM, fan 2 at 2369 RPM, and `0x78` at 73°C.
