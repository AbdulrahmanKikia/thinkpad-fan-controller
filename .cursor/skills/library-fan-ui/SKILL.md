---
name: library-fan-ui
description: Instrument-panel window for ThinkPad Fan Controller Utility. Use when changing the WPF window, tray, fan graphics, temperature display, mode controls, or the reason sentence.
---

# ThinkPad Fan Controller Utility window

The utility was formerly called Library Fan. The visible name is ThinkPad Fan Controller Utility. The program file remains `LibraryFan.dll`.

Dark instrument panel. The window shows what the fans are doing, not only what was requested.

- Each fan is a 270° gauge on a fixed 0–6000 RPM scale, labelled `0` and `6k` at the ends. The arc fills with measured RPM and the dotted rotor inside turns at the measured speed. `0` RPM is stopped. Stale readings are not RPM.
- A speed that failed to read is shown from the previous poll for up to three polls, dimmed and captioned `LAST READ`. After that the gauge shows `—` and `NO READING`. Never hide a missed read behind a steady number.
- The commanded level is a separate label under each fan, and again next to `REGISTER 0x2F` in the mode card. BIOS shows `Firmware`, not a fake level. `Waiting` means a level is wanted but both fans have not been read yet.
- The primary sentence states the mode and why. Examples: "Library is holding level 2 because 0x78 is 64°C.", "Manual is holding level 1.", "BIOS is in control."
- Sensor labels are register addresses. Do not present an unconfirmed name as the sensor's identity. Each tile has a 4px heat bar from 30°C to 90°C. `…` is a byte that was not read this poll; `—` is a byte that read as missing (`0x00` or `0x80`).
- A thermal handoff replaces the sentence with "Handed control back to the machine because a sensor reached 80°C." and the badge reads `HANDED BACK`.
- The trace keeps fixed scales so the picture means the same thing every minute: 30–90°C on the left, 0–6k RPM on the right, one grid for both, a dashed coral line at 80°C, time marks from −3 min to now, and a dot on the newest sample of each line.
- The link pill in the header reads `LIVE`, `PARTIAL` (some registers did not answer), `NO ANSWER` (the whole poll failed), or `OFFLINE` (driver not open). Its dot beats once per poll.
- The mode card shows the loaded Library curve as chips, with the step Library is holding outlined. Library and Manual controls dim to half opacity while a sensor is still at 80°C; they stay clickable so the refusal sentence can appear.
- The app lives in the notification area. A normal launch shows no window and no taskbar button, whether Windows started it or the user did. The first hardware launch shows a setup window that asks which computer to command and whether to start with Windows. The menu lists ThinkPad P1 Gen 3, X1 Extreme Gen 3, P15 Gen 2, and P14s Gen 5 Intel, which share the copied fan map, plus Dell, HP, and Framework, whose fan firmware this program cannot command. A choice is used only when this computer's product code matches. Closing the window or pressing Continue saves that choice and does not open the panel. Later launches stay hidden and refresh the logon Run value from the saved choice. The tray row "Start with Windows" changes the same choice. One balloon says "ThinkPad Fan Controller Utility is in the tray. BIOS is in control." Double-click the icon, or choose Open panel, to open the panel. The panel keeps `ShowInTaskbar` false. The X, Alt+F4, and minimize hide the panel and leave the controller running. Tray Exit is the only quit. The window still opens on its own when the driver did not open, and for `--self-test` and `--snapshot`. `--simulate`, `--self-test`, and `--snapshot` skip setup. The tray tooltip uses the same sentence as the panel. While the panel is hidden the gauges do not animate and the window is not redrawn.
- The tray menu is the panel in small. Read-only rows: the sentence (wrapped onto a second row when long), both fan speeds, the hottest sensor, and the link state. Then BIOS, Library, and Manual with a level 0–7 submenu; the active one carries a teal dot and the held level in its text. Library and Manual are disabled while a sensor is at 80°C, and BIOS reads "BIOS (handed back)". Then "Start with Windows", Open panel, and "Exit and hand fans to BIOS". A refusal chosen from the menu while the panel is hidden shows as a balloon. The glyph takes the heat colour; grey means the driver is not open. The menu uses the panel palette through a WinForms renderer, and every row keeps one height: a taller row would stretch all of them.
- `--simulate` shows a `Fake 0x78` slider. Hardware mode does not show it.

## Colour

Teal, amber, and coral mean temperature, in that order: teal below 65°C, amber from 65°C, coral at 80°C. The gauges take the colour of the hottest valid sensor, the heat bars take their own, and a handoff turns the badge and gauges coral. Teal is also the colour of the chosen button and the hottest tile's border. Do not give amber a second meaning.

## Shape

Cards, buttons, sensor tiles, chips, pills, and the mode badge share one corner radius of 12 and one border. Buttons share one height of 40. Gauges stay circular and shrink with their captions when the window is short; they never upscale. Marks smaller than 20px use a radius of 4, or 2 for a 4px bar or chart stroke.

The window opens at 1120×760, shrinks to fit the work area, and is centred. Its caption is the app's own bar: the fan mark, the name, ThinkPad P1 Gen 3, the link pill, and the mode badge. The system buttons sit on that bar. The title bar uses the dark DWM style.

## Check

There is no `LibraryFan.exe`; the app is `LibraryFan.dll` under `dotnet exec`. `LibraryFanSetup.exe` is only the installer, and it downloads the DLL package from GitHub. Launch with `--simulate` or `--self-test`. Switch BIOS, Library, and Manual. Raise the fake temperature through 80°C in Library and in Manual. Confirm the handoff sentence, and confirm Library and Manual do not resume until the temperature drops and the mode is chosen again.

To look at the window, render it from inside the process: `--simulate --snapshot <path.png>` writes the client area and exits. Add `--snapshot-after <seconds>` to collect trace samples first, `--sim-temp <celsius>` to set the fake `0x78`, and `--mode library|manual` to start in that mode. `--sim-temp` and `--mode` do nothing without `--simulate`. Keep snapshot files outside the repository.

This is a desktop window. Browser tools are not the check. Screen capture from another process returns black once the app is elevated.
