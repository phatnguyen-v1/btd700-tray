# BTD 700 Tray

A tiny Windows tray app for the **Sennheiser BTD 700** Bluetooth USB dongle. It does the everyday things from the tray,
so you don't have to open Sennheiser Dongle Control:

- **Switch the audio link mode**: Standard, Gaming (low latency) or Broadcast, from the tray menu or the window.
- **Gaming while a game runs**: pick your games once. While any of them runs, the dongle switches to Gaming, and when
  the last one closes it goes back to the mode it was in before. No more lip-sync lag in games, no more Gaming mode
  left on for music.
- **Disconnect the headset from this PC** (so your phone gets it cleanly over multipoint) and **connect it again**.
- A small notification after every switch, and the current mode and connection in the tray tooltip.
- Start with Windows (optional), single 34 KB exe, no installer, no admin rights.

> Unofficial. Not affiliated with or endorsed by Sennheiser. "Sennheiser" and "BTD 700" are trademarks of their
> owners. Firmware updates and codec settings still need [Sennheiser Dongle Control](https://www.sennheiser-hearing.com/).

## Use it

1. Download `Btd700Tray.exe` from [Releases](../../releases), or build it (below).
2. Run it. A headphones icon appears in the tray. Right-click it for the menu, double-click (or run the exe again) for
   the window.
3. For Gaming-while-a-game-runs: start your game, then in the window click **Edit games... > Add from running apps...**
   and pick it, then tick **Switch to Gaming while one of your games runs**.

Settings are kept in `%APPDATA%\Btd700Tray\settings.ini`, with a log next to it (`log.txt`, **Open log** in the
window). "Start with Windows" adds `Btd700Tray` to `HKCU\...\CurrentVersion\Run`.

Notes:

- Gaming mode over BT Classic needs a headset with aptX Adaptive. Sennheiser Dongle Control greys the button out
  otherwise; this tool doesn't check, and the dongle just stays in its current mode (you get a warning).
- Switching modes or disconnecting drops the audio for a moment, the same as in Sennheiser's app.
- The dongle is only asked for its state when needed (window or menu open, a game starting or stopping), so Sennheiser
  Dongle Control can stay open at the same time.
- Tested on Windows 11 with BTD 700 firmware as of November 2026 and a BT Classic headset. A future firmware update
  could change the commands; if switching stops working after an update, please open an issue.

## Build

Needs nothing but Windows: the C# compiler that ships with .NET Framework 4.x.

```
build.cmd
```

## How it talks to the dongle

The BTD 700 (USB `VID_3542&PID_3001`) takes commands as HID **output report `0x34`** on its vendor collection (usage
page `0xFFA2`, 64-byte reports). A command is `34 FE <cmd> <length> <args...>` padded to 64 bytes; the reply comes
back as input report `34 FF <cmd> <length> <data...>`. The dongle also sends `34 FC ...` on its own when something
changes (for example `34 FC 02 03 <mode> ...` after a mode switch).

| Send | Reply | Meaning |
|---|---|---|
| `34 FE 01 00` | `34 FF 01 03 <mode> <transport> <connected>` | Get mode. Mode 0 Standard, 1 Gaming, 2 Broadcast; transport 1 BT Classic, 2 LE Audio, 3 both; connected = transport in use, 0 when none |
| `34 FE 02 02 <mode> <transport>` | `34 FF 02 01 00` | Set mode (keep the current transport, like Sennheiser's app) |
| `34 FE 03 00` | `34 FF 03 01 <codec bits>` | Supported codecs: 1 SBC, 2 aptX, 4 aptX Adaptive, 8 aptX Lossless, 0x20 LC3 |
| `34 FE 06 00` | `34 FF 06 01 <link>` | Link state: 0 in between (seen while reconnecting), 1 disconnected, 2 connected, 3 streaming audio, 4 streaming voice |
| `34 FE 14 01 <0/1>` | `34 FF 14 01 00` | Disconnect (0) / connect (1) the headset; the link state follows within about 2 s |

The "is Gaming available" query (`34 FE 17 00`) gets no reply on this firmware, as
[btd700ctl](https://github.com/sobalap/btd700ctl) also found.

The command layout was worked out for interoperability, by reading which HID reports Sennheiser Dongle Control sends.
This repository contains no Sennheiser code. On Linux, see [btd700ctl](https://github.com/sobalap/btd700ctl).

## Files

| File | What it is |
|---|---|
| `Btd700.cs` | Talks to the dongle over HID (Win32 `hid.dll` / `setupapi.dll`, no libraries) |
| `HeadsetMode.cs` | Mode switching, Gaming while a game runs, connect / disconnect, notifications |
| `HeadsetForm.cs` | The window |
| `Program.cs` | Tray icon and menu, settings, log, icon |

## License

MIT, see [LICENSE](LICENSE).
