---
name: library-fan-ui
description: Instrument-panel window for Library Fan. Use when changing the WPF window, tray, fan graphics, temperature display, mode controls, or the reason sentence.
---

# Library Fan window

Dark instrument panel. The window shows what the fans are doing, not only what was requested.

- Each fan is a ring whose motion follows measured RPM. `0` RPM is stopped. Stale readings are not RPM.
- The commanded level is a separate label. BIOS shows that firmware has the fans, not a fake level.
- The primary sentence states the mode and why. Examples: "Library is holding level 2 because 0x78 is 64°C.", "Manual is holding level 1.", "BIOS is in control."
- Sensor labels are register addresses. Do not present an unconfirmed name as the sensor's identity.
- A thermal handoff replaces the sentence with "Handed control back to the machine because a sensor reached 80°C."
- A short trace shows temperature and RPM for the last few minutes.
- The tray uses the same sentence.
- `--simulate` shows controls that raise the fake temperature. Hardware mode does not show those controls.

## Shape

Cards, buttons, sensor tiles, and the mode badge share one corner radius of 12 and one border. Buttons share one height. Fan rings stay circular. Marks smaller than 20px use a radius of 4, or 2 for a 4px chart stroke.

## Check

Launch the app with `--simulate` or `--self-test`. Switch BIOS, Library, and Manual. Raise the fake temperature through 80°C in Library and in Manual. Confirm the handoff sentence, and confirm Library and Manual do not resume until the temperature drops and the mode is chosen again.

This is a desktop window. Browser tools are not the check.
