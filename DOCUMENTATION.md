# ThinkPad Fan Controller Utility

9 October 2026. This is the account for someone opening the repository: what the program stands on, what it can do, what it refuses to do, and the difficulties in the order they showed up.

The utility was formerly called Library Fan. The name was changed to ThinkPad Fan Controller Utility. The program file remains `LibraryFan.dll`.

[POSTMORTEM.md](POSTMORTEM.md) is the lab record from 6–7 October. A few lines in it were written before logon startup, the tray, the computer menu, and packaging existed. Where the two files disagree, this one is the later account.

## This stands on other people's work

The fan protocol in this repository was not discovered here. The register offsets in `src/LibraryFan.Core/Registers.cs` are copied from `fancontrol/fanstuff.cpp` in [nikolasgd/TPFanControl-dual-fan](https://github.com/nikolasgd/TPFanControl-dual-fan). Read that project before treating anything in this one as original. Its own README says the program is confirmed on a ThinkPad P1 Gen 3, an X1 Extreme Gen 3, a P15 Gen 2, and a P14s Gen 5 Intel. Those four names are why they appear in this app's first-launch menu. The hardware proof collected in this repository is the P1 Gen 3 only.

That dual-fan project is itself a fork. It says it descends from [byrnes/TPFanControl](https://github.com/byrnes/TPFanControl), and it points at the original [TPFanControl on SourceForge](https://sourceforge.net/projects/tp4xfancontrol/) for the build that older instructions install. The Win32 dialog and the TVicPort driver from that line were left behind here. The map of the controller was not.

Port access is [PawnIO](https://pawnio.eu), by namazso. The only module vendored here is `LpcACPIEC.bin` from [PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) release 0.2.11. The source of that module is `LpcACPIEC.p` in the same repository. It is LGPL-2.1-or-later. `third_party/pawnio/COPYING` is that license, and a copy is placed next to the binary in the try-it folder. The application code is under the MIT License in [LICENSE](LICENSE). That grant does not cover the PawnIO module.

The same ThinkPad embedded-controller fan interface is described, independently of this repository, by the Linux `thinkpad_acpi` driver and by [ThinkWiki's fan-speed page](https://www.thinkwiki.org/wiki/How_to_control_fan_speed). Those pages are worth reading. The bytes in this tree were copied from the dual-fan fork, not re-derived from the wiki.

## What it does

On a matching ThinkPad, as administrator, with the PawnIO driver installed:

- It reads both fans and the temperature bytes, once a second, and shows measured RPM on a 0–6000 scale. `0` is a stopped fan.
- It starts with firmware in control. Library mode follows `library-curve.json`: off below 55°C, then levels 1, 2, 3, and 4 at 55, 62, 68, and 74°C. The level steps down only after a 3°C fall. Manual mode offers levels 0–7. Both fans receive the same level.
- One valid sensor at decimal 80°C writes `0x80` to both fans, returns the app to firmware, and stays there until every valid sensor is cooler and the user chooses Library or Manual again. Bytes `0x00` and `0x80` are missing readings, not 80°C.
- Exit, shutdown, and sleep write `0x80`. Hiding the window does not.
- The first hardware launch asks which computer this is, and whether to start with Windows. After that the app stays in the notification area. The panel opens from the tray.
- `--simulate` draws the same window against a fake controller and performs no port I/O.

The built-in map is the ThinkPad P1 Gen 3 `20TH000XUS`, BIOS containing `N2VET41W`. The menu can also select an X1 Extreme Gen 3 (`20TK`, `20TL`), a P15 Gen 2 (`20YQ`, `20YR`), or a P14s Gen 5 Intel (`21G2`, `21G3`). A selection is used only when this computer's product code matches. A person who knows a further machine's map can add `hardware-profile.json` next to `LibraryFan.dll`. The file is used only when its product matches the machine that is running.

## What it does not do

- It does not command Dell, HP, or Framework machines. Those names are in the menu so the different controller is visible. Choosing one leaves the fans with firmware.
- It does not cover every ThinkPad, and it does not cover every laptop. An unknown product keeps its fans with firmware. A profile cannot move the ports off `0x62` and `0x66`, cannot set bit 6 (`0x40`, disengaged full speed), and cannot raise the 80°C handoff.
- It does not give the two fans different levels.
- It does not name the temperature registers. The tiles show addresses. On this P1, `0x78`, `0x7A`, `0x7C`, `0x7D`, and `0x7E` have read as temperatures. `0xC2` once read 1°C, which passes the missing-byte rule and can still be nonsense.
- It does not prove the 80°C handoff on hardware. That case is tested on the simulator. The laptop was not heated to 80°C.
- It does not survive Task Manager killing the process. The last level can remain in the controller until reboot or until something else writes `0x80`.
- It does not ship `LibraryFan.exe`. Smart App Control on this PC blocks that unsigned host. The fan app is `LibraryFan.dll` under the signed `dotnet.exe`.
- It does not install the PawnIO driver or the .NET runtime. The person running it installs both.
- The setup program `LibraryFanSetup.exe` downloads `LibraryFan-try.zip` from the latest release of this repository.

## How it was built

### 1. A quiet curve for one loud laptop

The machine is a ThinkPad P1 Gen 3, `20TH000XUS`, BIOS `N2VET41W (1.26)`, i7-10750H, Intel UHD plus an NVIDIA Quadro T1000 Max-Q, on Windows 11. It is loud in a library. The aim was a controller the owner could see: measured fan speed, a quiet curve while the machine is only warm, and an immediate return to firmware if a sensor reaches 80°C.

The first useful document was the dual-fan fork, which had already been exercised on this class of machine. Copying its register map was the whole of the protocol work. Inventing offsets was refused, and that refusal held for the rest of the project.

### 2. The fork's ports were the wrong pair on this BIOS

The fork reaches the controller through `0x1600` and `0x1604`. On this BIOS that pair is wrong. The ECDT, `LENOVO TP-N2V`, places the ACPI embedded controller at data port `0x62` and control port `0x66`, path `\_SB.PCI0.LPCB.EC`. PawnIO's `LpcACPIEC` module only opens those two ports, which matches the ECDT. Using the fork's pair here would have made the app silent. Using this pair on a machine whose controller really lives elsewhere would have written the wrong device. The skill and the client both record `0x62` and `0x66` so a later edit does not "correct" them back.

The command style is the one in the fork's `portio.cpp` and in the ACPI embedded-controller spec. Read with `0x80` on the control port, write with `0x81`. Hold `Global\Access_EC` across each transaction. Fan select is `0x31` (`0` then `1`). The command byte is `0x2F`: `0x80` returns the fan to firmware, `0` through `7` are levels. Bit 6 is never set. RPM is `0x84` then `0x85`. A word above `0x1FFF` is stale. Temperatures are `0x78`–`0x7F` and `0xC0`–`0xC3`.

### 3. The usual Windows fan drivers were not acceptable

WinRing0 and TVicPort are what the older tools ship. Memory Integrity and the vulnerable-driver blocklist are why they were refused. The owner installs the signed PawnIO driver from pawnio.eu and approves its prompt. This repository vendors one module, `LpcACPIEC.bin` from PawnIO.Modules 0.2.11, and the LGPL `COPYING` next to it. Calls that succeeded on this laptop are written down in `.cursor/skills/thinkpad-ec/reference.md`. The first elevated probe, through PawnIO 2.2.0, read fan 1 at 2563 RPM, fan 2 at 2369 RPM, and `0x78` at 73°C, then 77°C under load. Library commanded level 3, then level 4, and the probe wrote `0x80` on the way out.

### 4. An unsigned exe would not start

`LibraryFan.exe` is an unsigned apphost. Launching it returned `0x800711C7` (`ERROR_SYSTEM_INTEGRITY_POLICY_VIOLATION`). Removing a mark-of-the-web stream did nothing; the file had none. There is no per-app allow button for Smart App Control on this build, and turning the feature off is often one-way. The project sets `UseAppHost=false`. `Open ThinkPad Fan Controller Utility.cmd` builds Release when needed and starts `dotnet exec LibraryFan.dll`, because `dotnet.exe` is signed. Elevation has to relaunch that same command. Relaunching the blocked exe, or relaunching `dotnet.exe` with no arguments, drops the program.

### 5. The controller and Windows share two ports

`Win32_Fan` is empty on this laptop. Lenovo Intelligent Thermal Solution and Intel DPTF are running and own thermal policy. They do not publish a fan-speed stream. Windows Thermal Zone `\_TZ.THM0` did publish a chassis temperature around 38°C during this work, well below the CPU byte the curve follows, so it was left unwired.

Each poll does many transactions on `0x62` and `0x66`, the same ports the ACPI driver is using. Bytes time out or come back stale. The tachometer also reports an unusable word while a fan changes speed. The first reader treated one failed byte as a failed snapshot, polled every two seconds, and latched firmware after three failures. The panel looked broken. That was reported by the person using the app.

The reader was rewritten rather than patched. Each byte settles the controller, drains a stale output byte, and retries three times. A failed byte leaves that register unread. The snapshot stands if any register answered. Three consecutive failed registers end the snapshot early. The poll is once a second. A missed RPM is shown as `LAST READ` for up to three polls and never counts as a live reading, so it cannot unlock a level write. A level is written when it changes and refreshed every 30 seconds. `0x80` is written once per transition. The old loop rewrote the same byte every poll.

A second elevated probe then read all 14 registers on both passes: fan 1 at 2561 then 2489 RPM, fan 2 at 2360 then 2284 RPM, `0x78` at 42–43°C. Library wrote level 0 at 43°C, which is the curve below 55°C, and the probe restored `0x80`. The window ran for several minutes with steady speeds. `0xC2`, which had read 1°C on the first probe, read as missing.

### 6. The window had to be readable on a short screen

The display in use is 1536×864. An early layout was taller than that, so the mode buttons and the level row were clipped. The window became 1120×760 and shrinks to the work area. A capture taken from another process of the elevated window came back black. That is a capture limit. `--snapshot` renders the client area from inside the process.

The panel is an instrument, not a settings form. Gauges, a sentence that says why the level was chosen, a three-minute trace, and sensor tiles labelled by address. After that, the stock title bar still made it look like a prototype, so the caption is drawn by the app: the mark, the name, the machine, and the link pill. Colour means temperature: teal below 65°C, amber from 65°C, coral at 80°C.

### 7. It had to live in the tray

A normal launch, whether Windows started it or the user did, shows no window and no taskbar button. The first hardware launch asks the startup question and does not open the panel. The X, Alt+F4, and minimize hide the window and leave the controller running. Tray Exit is the quit, and it writes `0x80`. `--simulate`, `--self-test`, and `--snapshot` skip that setup and do not write the logon Run key.

While the panel is hidden the gauge rotors stop and the window is not redrawn. The EC client reuses its ioctl buffers so a poll does not allocate under the mutex. A review of the curve loader found that a JSON `offBelowCelsius` above 55 could hold the fans off through temperatures where Library is supposed to be running. The loader now caps that value at 55.

### 8. More machines, without inventing a map

The question was whether this could cover more laptops, or all of them. All of them, no. Dell, HP, and Framework use other fan controllers. Some ThinkPads use other registers, and on some models `0x2F` can be read and must not be written. Copying this P1's temperature list onto a marketing name and calling it that model's map would command the wrong bytes.

What was added instead:

- A file, `hardware-profile.json`, for a person who already knows that machine's embedded-controller map. It is used only when the product string matches. A broken file is not replaced by the P1 map. Ports other than `0x62` and `0x66` are refused. The firmware command, bit 6, and the 80°C handoff are not fields the file can set.
- A first-launch menu. The ThinkPad entries are the four the dual-fan tool lists, and only where a Lenovo machine type was found in public product pages: P1 Gen 3 `20TH` with BIOS `N2VET41W`, X1 Extreme Gen 3 `20TK`/`20TL`, P15 Gen 2 `20YQ`/`20YR`, P14s Gen 5 `21G2`/`21G3`. Dell, HP, and Framework are listed and have no map. A wrong ThinkPad selection on this P1 falls through to the built-in map. A selection that matches nothing leaves the fans with firmware.

`hardware-profile.example.json` is this P1's map, under the example name, so copying the folder onto another model does not silently replace the built-in map.

### 9. A folder other people can run, then an installer

Other people should not need this repository or an unsigned exe. `Package ThinkPad Fan Controller Utility.cmd` publishes a framework-dependent folder and `dist\LibraryFan-try.zip`: the DLL, `Open ThinkPad Fan Controller Utility.cmd`, `LpcACPIEC.bin`, `COPYING`, the example profile, and a short read-me. The person needs the .NET 8 Desktop Runtime and the PawnIO driver.

`LibraryFanSetup.exe` is a separate program, the one executable, because an installer has to be something a person can download and run. It does not contain the fan app. It downloads `LibraryFan-try.zip` from the latest release of this GitHub repository and installs the DLL under `%LocalAppData%\LibraryFan\App`, with a Start menu item that still calls `dotnet exec`. The download refuses a redirect off GitHub, refuses `LibraryFan.exe` inside the zip, and does not install a `hardware-profile.json`. It takes `LibraryFan-try.zip` from the latest release. The setup exe is unsigned, so Smart App Control can block it on this PC too.

### 10. Publishing was deliberately left unfinished

Git was installed during the work. The first commit attempt failed because no author identity was configured, and that configuration was left untouched. On 7 October the tree was committed with a one-shot author and pushed to a private repository. The source is now public under the MIT License, so other people can build it and add a machine they have measured. [CONTRIBUTING.md](CONTRIBUTING.md) states the limits. The installer downloads `LibraryFan-try.zip` from a tagged release. The application code is under the MIT License. The LGPL module already has its license and its source link. The 80°C path still has not been run on the hardware, on purpose.

## What is still open

- Someone other than the author running the folder package on a second ThinkPad.
- A code-signing certificate, if the setup exe is going to start on a machine with Smart App Control.
- A plausibility rule for temperature bytes beyond `0x00` and `0x80`.
- Independent fan levels, which wait on evidence that fan 2 changes speed on its own.
- A driver-side fallback if the process is killed.
- Sensor names, which require a real load. They do not require heating the machine to 80°C.
