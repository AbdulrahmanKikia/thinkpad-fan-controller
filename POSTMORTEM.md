# ThinkPad Fan Controller Utility — development record

6 October 2026. Written so this repository can be read, and only then published, with the failures still attached.

This record uses the name Library Fan. That was the name of the project on the days it describes. The name was later changed to ThinkPad Fan Controller Utility.

The later account, including work done after this file, is [DOCUMENTATION.md](DOCUMENTATION.md). Logon startup, the tray, the computer menu, and packaging now exist. Statements below that say they do not are the state of 6–7 October.

## Result

A .NET 8 WPF app in this folder reads and commands the embedded controller on one ThinkPad P1 Gen 3. Both fans share one level. The window shows measured RPM, the temperature registers, a short trace, and a sentence that says why the current level was chosen. Library mode follows a fixed curve. Manual mode offers levels 0–7. Either mode returns the fans to firmware at 80°C.

The repository has no commits. `cursor/library-fan-controller` is checked out, the first implementation is staged, and later edits are unstaged. There is no `user.name` or `user.email`, so `git commit` cannot succeed until the author sets those. There is no license for the application code. `third_party/pawnio/LpcACPIEC.bin` is covered by the LGPL-2.1 text in `third_party/pawnio/COPYING`. Publishing the binary means complying with that license and pointing at the PawnIO.Modules sources. Publishing the rest means choosing a license first.

## What the project was for

The machine is loud in a library. The goal was a controller the owner could see and trust: measured fan speed on screen, a quiet curve while the laptop is merely warm, and an immediate handoff if a sensor reaches a dangerous temperature. The protocol comes from [nikolasgd/TPFanControl-dual-fan](https://github.com/nikolasgd/TPFanControl-dual-fan), which had been exercised on this class of machine. The old Win32 dialog and the TVicPort driver were left behind.

## Machine

| | |
|---|---|
| Model | ThinkPad P1 Gen 3, `20TH000XUS` |
| BIOS | `N2VET41W (1.26)`, 2022-10-26 |
| CPU | Intel i7-10750H |
| Graphics | Intel UHD and NVIDIA Quadro T1000 Max-Q |
| OS | Windows 11 Pro, build 26300 |
| EC | ECDT `LENOVO TP-N2V`, data port `0x62`, control port `0x66`, path `\_SB.PCI0.LPCB.EC` |

The dual-fan fork also documents a relocated pair, `0x1600` / `0x1604`. That pair is the same command protocol on other machines. It is the wrong pair on this BIOS. PawnIO's `LpcACPIEC` module only allows `0x62` and `0x66`, which matches the ECDT. Treating the fork's ports as universal would have made the app silent on this laptop and dangerous on another.

## What shipped

One process. A `net8.0` library owns the session, the curve, the embedded-controller protocol, the simulator, and the PawnIO client. A `net8.0-windows` WPF project is the window and the tray. The controller thread polls every 2 seconds. The window updates on the dispatcher. The app starts in BIOS and writes a level only after the user chooses Library or Manual.

Registers, copied from `fancontrol/fanstuff.cpp` and not invented:

- `0x31` selects the fan. `0` is fan 1. `1` is fan 2.
- `0x2F` is the command. `0x80` returns the fan to firmware. `0` through `7` are manual levels.
- Bit 6 (`0x40`) is disengaged full speed. The app never sets it.
- RPM is a 16-bit word: select the fan, read `0x84`, then `0x85`. Values above `0x1FFF` are stale. `0` is a stopped fan.
- Temperatures are `0x78`–`0x7F` and `0xC0`–`0xC3`. `0x00` and `0x80` mean the byte is missing. 80°C is decimal 80 (`0x50`).

A level write selects each fan, writes `0x2F`, reads it back, and retries up to five times. Both fans receive the same level. The `Global\Access_EC` mutex is held across each transaction.

The Library curve lives in `library-curve.json`: fans off only while the hottest valid sensor is under 55°C, then level 1 at 55, 2 at 62, 3 at 68, and 4 at 74. The level steps down only after the temperature falls 3°C. The 80°C ceiling is a constant in code, outside the JSON, and it cancels the curve.

The window is a dark instrument panel. Each fan is a ring whose motion follows measured RPM. The commanded level is a separate label. Firmware control is labeled Firmware, not a fake level. Sensor tiles are labeled with register addresses. The tray tooltip uses the same sentence as the window. `--simulate` adds a temperature slider and performs no port I/O.

## What was proven

On the simulator, with `--self-test` and the UI test:

- Library at 64°C holds level 2 because `0x78` is 64°C.
- 80°C in Library, and again in Manual, writes the handoff and latches.
- Choosing Library or Manual while a sensor is still at 80°C is refused.
- After the fake temperature drops, an explicit choice can leave BIOS.
- A raw sensor byte of `0x80` does not count as 80°C.
- Three unreadable polls latch BIOS. One does not.
- Level writes wait until both fan speeds have been read in the session.
- `0x40` is rejected.
- RPM is assembled low byte first. A word above `0x1FFF` is not a live speed.
- Disposing the controller restores `0x80` without touching ports in the simulator.

Ten safety tests pass. The window self-test passes.

On the metal, one elevated probe through PawnIO 2.2.0 and `LpcACPIEC.bin` from PawnIO.Modules 0.2.11:

- Fan 1 read 2563 RPM. Fan 2 read 2369 RPM.
- `0x78` read 73°C, then 77°C under load. Library commanded level 3, then level 4.
- The probe wrote `0x80` on the way out.

The laptop was already warm. Library did not command the fans off. The 80°C path was not run on the hardware, on purpose.

Sensors on that probe: `0x78` 73, `0x79` missing, `0x7A` 61, `0x7B` missing, `0x7C` 55, `0x7D` 49, `0x7E` 54, `0x7F` missing, `0xC0` missing, `0xC1` missing, `0xC2` 1, `0xC3` missing. `0xC2` at 1°C passes the missing-byte rule, so a later spike in that byte can become the "hottest" sensor and move the curve. The names of these registers were never confirmed. The UI keeps the addresses.

## Incidents

### The reference ports were not this BIOS

The first useful document was the dual-fan fork. Its relocated port pair would not open through the only signed module that was acceptable here. The ECDT on `N2VET41W` settled it: `0x62` and `0x66`. The skill and the client both record that, so a later edit does not "correct" them back to `0x1600`.

### Unsigned port drivers are not available

WinRing0 and TVicPort are what older fan tools ship. Memory Integrity and the vulnerable-driver blocklist are why they were refused. PawnIO is the driver. The user installs the signed build from pawnio.eu and approves the prompt. The repo vendors `LpcACPIEC.bin` and its LGPL copying file, and no other PawnIO module. Calls that actually succeeded are in `.cursor/skills/thinkpad-ec/reference.md`.

### Smart App Control blocks the exe

`LibraryFan.exe` is an unsigned apphost. Launching it returns `0x800711C7` (`ERROR_SYSTEM_INTEGRITY_POLICY_VIOLATION`). Removing a mark-of-the-web stream did not help; the file had none. `dotnet.exe` is signed and can host the DLL:

```bat
dotnet exec src\LibraryFan\bin\Debug\net8.0-windows\LibraryFan.dll
```

`Open Library Fan.cmd` does that. Elevation has to relaunch `dotnet.exe exec "<dll>"`. Relaunching the blocked exe, or relaunching `dotnet.exe` with no arguments, drops the program. There is no per-app allow button for Smart App Control on this build. Turning the feature off is often one-way, so the launcher is the workaround that was kept.

### Readings skip and drop out

The tachometer in the embedded controller is the only live RPM source found on this laptop. `Win32_Fan` is empty. Lenovo Intelligent Thermal Solution and Intel DPTF (`dptf_acpi`, `dptf_cpu`) are running and they own thermal policy. They do not publish a fan-speed stream an app can follow.

Windows Thermal Zone Information does publish `\_TZ.THM0`. A sample during this work read about 38°C. That series is steadier than the controller bytes. It is a chassis zone, well below the CPU register `0x78` that the curve follows, so it was left as a documented alternative for a temperature trace and was not wired into the curve.

The dropouts have a mechanical cause. Each poll performs many port transactions on `0x62` / `0x66`, the same ports the ACPI driver is using. One failed byte fails the whole snapshot. The UI then shows no reading, and three failures in a row latch BIOS. Samples also arrive only every 2 seconds, and the tachometer itself moves in coarse steps and reports an unusable word while a fan changes speed. Those words are discarded, so the number on screen jumps. This was reported by the person using the app and was not fixed in this pass. A fix has to keep the 80°C handoff: hold the last good RPM across a single missed poll, and stop failing the entire sample because one temperature byte timed out. It must not move the curve onto the chassis zone.

### The first window did not fit the screen

The display in use is 1536×864. An early layout used a window taller than that, so the mode buttons, the `0x2F` levels, and the fan captions were clipped. The window is now 1120×740 and shrinks to the work area. Cards, buttons, sensor tiles, and the mode badge share a 12px corner radius. Fan rings stay circular. A capture taken from a non-elevated process of the elevated window came back black. That is a capture limit, not a blank UI. The simulator render is the one that was reviewed.

### Git is initialized and unpublished

Git 2.55 was installed during the project. The branch is `cursor/library-fan-controller`. Nothing has been committed, and nothing has been pushed. A commit was attempted and refused because no author identity is configured. That configuration was left untouched. `Open Library Fan.cmd` and the elevation fix in `App.xaml.cs` are still unstaged on top of the index. A publisher needs an author, a license decision, and a second look at the staged `LpcACPIEC.bin` before `git commit` or `git push`.

## How a level reaches the fan

1. The window asks for BIOS, Library, or a manual level.
2. `FanSession` refuses Library and Manual while any valid sensor is at or above 80°C, or while a handoff latch is still held and the user has not made a fresh choice after cooling.
3. Library asks `LibraryCurve` for a level. Manual uses the button that was pressed.
4. Levels 0–7 are withheld until both RPM values have been read live in this session. `0x80` is allowed whenever the driver is open, including exit, shutdown, sleep, the thermal trip, and three bad polls.
5. `ThinkPadEc` writes the byte to both fans and requires both read-backs to match.
6. Steady BIOS mode does not keep writing. Lenovo Vantage is left alone.

The sentence on screen is the decision, for example "Library is holding level 2 because 0x78 is 64°C.", "Manual is holding level 1.", "BIOS is in control.", or "Handed control back to the machine because a sensor reached 80°C."

## Residual risk

A hard kill of the process skips the exit restore. The last level can remain in the controller until reboot or until something else writes `0x80`. That is why Library will not command level 0 once any valid sensor is at or above 55°C, and why BIOS is the recovery mode. Sleep and shutdown are hooked. They are not a substitute for a driver that reverts the fans if the process disappears.

`0xC2` reading 1°C shows the missing-byte rule is incomplete. A register can be present and still be nonsense. The curve trusts the hottest valid byte.

The poll shares the embedded-controller ports with firmware. A long enough streak of collisions looks like a disconnect and latches BIOS, which is the safe failure, and it also makes the panel look broken, which is what the user reported.

Independent left and right levels were deferred until a read-only probe showed fan 2 changing speed on its own. The probe read two different RPM values at one moment. It did not show fan 2 moving while fan 1 was held. Both fans still receive one level.

## Left undone

- Smoother RPM display that survives one missed poll without hiding a real stop or a real 80°C reading.
- A decision about `0xC2` and the other bytes that never move.
- A code-signing certificate so `LibraryFan.exe` survives Smart App Control.
- Logon startup.
- A license for the application, and an explicit source offer for the LGPL module if the binary stays in the tree.
- A git commit, which needs an author name and email supplied by the owner.
- Confirmation of sensor names under a real CPU and GPU load. Do not heat the machine to 80°C to get that data.

## Publish checklist

- [ ] Add a license for everything that is not `third_party/pawnio`.
- [ ] Keep `COPYING` next to `LpcACPIEC.bin`, and link the PawnIO.Modules release the file came from (0.2.11), or stop vendoring the binary and document where to get it.
- [x] Set the git author locally, review the staged set, and include `Open Library Fan.cmd` plus the elevation change. (Committed with a one-shot author and pushed to a private repository on 7 October.)
- [ ] Say in the repository description that this is one BIOS on one P1 Gen 3.
- [ ] Do not document bit 6 as a feature.

## Rebuild, 7 October 2026

After the first build was published, the whole tree was rewritten with what the incidents above had taught. The register map, the 80°C rule, and PawnIO stayed. Everything else was written again rather than patched.

### What changed in the reader

- Each byte is read on its own. `EcBus` settles the controller first, waits for the input buffer, drains a stale output byte if one is there, and retries a byte three times before giving up on it.
- A snapshot survives a failed byte. `SensorSample.Raw` is nullable: a byte that was not read is `…` on screen, a byte that read as `0x00` or `0x80` is `—`. The snapshot is readable when any register answered, and three consecutive failed registers end it early so a dead controller does not cost 14 timeouts per poll.
- The poll runs every second instead of every two.
- A fan speed that fails to read is held from the previous poll for up to three polls, marked `LAST READ`. The hold is display only. It never counts as a live reading, so it never unlocks level writes.
- A level is written when it changes and refreshed every 30 seconds. `0x80` is written once per transition. The old loop rewrote the same byte every poll.

### What changed around it

- `UseAppHost=false`. There is no exe to block. `Open Library Fan.cmd` builds Release when needed and starts `dotnet exec`. Elevation relaunches `dotnet.exe exec "<dll>"`.
- `--snapshot <path>` renders the client area from inside the process, which is the only capture that works once the app is elevated. `--snapshot-after`, `--sim-temp`, and `--mode` set up a scene on the simulator; the last two are ignored without `--simulate`, so nothing on the command line can make the hardware build take over at start.
- Tests: ten safety, eight protocol, nine reading, one hardware probe that returns early unless elevated, and one window test. Twenty-eight pass without elevation; the probe passed elevated.

### Hardware result

The elevated probe read all 14 registers on both passes: fan 1 at 2561 then 2489 RPM, fan 2 at 2360 then 2284 RPM, `0x78` at 42–43°C, five temperature bytes valid and seven missing. Library wrote `0x00` at 43°C, which is the curve's choice below 55°C, and the probe restored `0x80`. The window then ran for several minutes with steady speeds and no dropouts. `0xC2`, which read 1°C on the first build, read as missing this time.

### The window

The second pass on the window kept the one radius, one border, and one button height, and made the panel say more:

- Fans are 270° gauges on a fixed 0–6000 RPM scale with the rotor turning inside. The scale is printed at both ends.
- The trace has fixed axes, 30–90°C and 0–6k RPM on one grid, a dashed line at 80°C, time marks, and a dot on the newest sample.
- Sensor tiles carry a heat bar. Colour means temperature everywhere: teal, amber from 65°C, coral at 80°C.
- A link pill reads `LIVE`, `PARTIAL`, `NO ANSWER`, or `OFFLINE` and beats once per poll, so a missed poll is visible without reading the footer.
- The Library curve is shown as chips with the held step outlined, and the register label says what `0x2F` holds.
- The gauges shrink with their captions when the window is short. The first layout clipped the command label once the mode card grew; the fix was a down-only viewbox, not a taller minimum.

### Still open

- A plausibility rule for temperature bytes beyond `0x00` and `0x80`.
- Independent left and right levels, pending evidence that fan 2 moves on its own.
- A license, and the LGPL source offer for the vendored module.
- Logon startup and a driver-side fallback if the process is killed.
