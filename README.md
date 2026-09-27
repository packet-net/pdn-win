# pdn-win

A packet radio terminal for Windows and Linux (where it is called **pdn-lin**). Plug in a radio
interface, pick a mode, set the levels with the built-in helper, and you are on the air:
connected-mode sessions in tabs, a BPQ-style monitor, and the pdn-soundmodem spectrum and waterfall,
all in one window.

It runs [pdn-soundmodem](https://github.com/packet-net/pdn-soundmodem) inside the app (no separate
modem process, no KISS port) over [packet.net](https://github.com/packet-net/packet.net)'s AX.25
stack.

**Download** from [Releases](https://github.com/packet-net/pdn-win/releases): the MSI or the portable
exe for Windows, `.deb` packages (amd64, arm64) or tarballs for Linux. On Debian, Ubuntu and
Raspberry Pi OS it is also in the [packet-net apt repository](https://packet-net.github.io/apt):
`sudo apt install pdn-lin`. What is done and what is next: [docs/roadmap.md](docs/roadmap.md).

## What it does

- **Station**: an AIOC, CM108 dongle or similar USB interface, found automatically with its PTT
  (HID GPIO or serial). FM modes for now: 1200 AFSK (plain, multi, IL2P, FX.25) and 3600 QPSK.
- **Sessions**: several connections at once, one tab each, with a console tab for commands
  (`C GB7RDG` connects, `U DEST text` sends unproto). Incoming connections open their own tab and
  get a welcome text.
- **Monitor**: every frame heard or sent, coloured by kind, with the modem's receive diagnostics
  (SNR, offset, level, FEC) under each one.
- **Heard**: who is on the channel, most recent first.
- **Spectrum and waterfall**: the station page's look, each its own pane, with the callsign and
  SNR of every decoded frame tagged where it was heard.
- **Levels**: a guided setup for an open-squelch FM handheld: set the radio's volume by its hiss,
  then the transmit deviation by Bessel null.
- **Audio hygiene**: on start the interface is put right: nothing above 0 dB, AGC and mic boost
  off, the CM108 monitor path closed. On Windows also audio enhancements and "Listen to this
  device" off (spatial sound is reported); on Linux the card's ALSA mixer.
- **Plug and unplug**: a station waiting for its interface starts the moment it is plugged in, and
  the settings dialog's list updates itself.
- **Beacon**: optional, never more often than every 5 minutes, never on start-up.
- **Layout**: every pane can be dragged by its tab onto another pane's centre (as a tab) or edge
  (as a split), resized, closed and brought back from View. The layout and the window are
  remembered.

## On Linux

The package installs a udev rule that lets the logged-in user (and the `audio` group) key a CM108
or AIOC through its hidraw node and open the AIOC's serial port, and a WirePlumber rule that keeps
the desktop's sound server off the AIOC so the card can be opened directly. If something else holds
the card anyway, pdn-lin says what (for example "pipewire (pid 1234)"), and goes through PipeWire
when it is PipeWire. Anything it cannot open is named with the fix before the station starts.

## Build and run

Requires the .NET 10 SDK. pdn-soundmodem comes from NuGet (`pdn-soundmodem`,
`pdn-soundmodem-windows` and `pdn-soundmodem-linux`). To work on both at once, build against a
local checkout with `-p:PdnSoundModemRoot=C:\path\to\pdn-soundmodem`.

```
dotnet build
dotnet test tests/PdnWin.Core.Tests
dotnet test tests/PdnWin.Tests
dotnet run --project src/PdnWin
```

Settings live in `%APPDATA%\pdn-win\settings.json` on Windows and `~/.config/pdn-lin/settings.json`
on Linux; unhandled errors are appended to `errors.log` in `%LOCALAPPDATA%\pdn-win` or
`~/.local/share/pdn-lin`.

### Without a radio

`pdn-win --simulate` (or `pdn-lin --simulate`) runs against a simulated channel: a second
soundmodem station, GB7SIM, on the other end of a real-time audio link with hiss on it. It beacons,
sends some made-up traffic, and accepts connects (`C GB7SIM`), echoing what you type. Nothing
touches a real device. Add `--demo` for a scripted session: connect, two lines, disconnect.

### Releases

See [docs/releasing.md](docs/releasing.md). `./build/build-release.ps1 -Version x.y.z` builds the
Windows MSI and portable exe into `dist/`; `packaging/linux/build-deb.sh x.y.z amd64` builds the
Linux package and tarball.

### Development switches

- `PDNWIN_SETTINGS=path` uses a settings file of its own instead of the real one.
- `PDNWIN_SNAPSHOT=dir` makes every open window render itself to `dir/<title>.png` each second,
  for checking the UI from a script without taking focus.

## Layout of the code

- `src/PdnWin.Core` (no UI framework, no platform APIs, tested headless)
  - `Stations/`: the `IStationDevice` seam and its optional features; `SoundModem/SoundModemStation`
    runs the modem over any `ISoundCard`; `Simulation/` is the simulated channel.
  - `Sessions/`: `SessionManager`, many sessions over one packet.net listener.
  - `Monitoring/`, `Levels/`, `Beacons/`, `Settings/`, `AppIdentity` (pdn-win or pdn-lin).
- `src/PdnWin.Presentation`: the view models and the dock model, UI-neutral; `Hosting/` is what
  they need from outside (`IUiThread`, `IStationHardware`).
- `src/PdnWin.Windows`: the station hardware on Windows (WASAPI, HID and serial PTT, discovery by
  container ID, endpoint levels and hygiene, device notifications).
- `src/PdnWin.Linux`: the station hardware on Linux (ALSA, hidraw and serial PTT, discovery by USB
  device in sysfs, access checks, mixer levels and hygiene, the PipeWire fallback, hot-plug).
- `src/PdnWin`: the Avalonia app, the same on both.
  - `Docking/`: the dock host. `Controls/`: meter, lamps, spectrum, waterfall, the terminal-style
    list. `Views/`, `Styles/`.
- `packaging/linux`: the `.deb` (desktop entry, icons, udev and WirePlumber rules) and its checks.
- `tests/PdnWin.Core.Tests`: including two complete stations talking over simulated audio.
- `tests/PdnWin.Tests`: the windows themselves, driven headless.

## Other hardware

The station is an `IStationDevice`: a packet.net `IAx25Transport` for the session layer, a monitor
feed, and optional features (spectrum, input level, mode, TX test, channel access) that the UI
shows panes and controls for. A NinoTNC or a KISS TNC is another implementation of the same
interface; none is built yet.

## Licence

AGPL-3.0-or-later. See [LICENSE](LICENSE). The bundled Cascadia Mono font is under the SIL Open
Font License 1.1 (`src/PdnWin/Assets/Fonts/OFL.txt`).
