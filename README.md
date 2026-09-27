# pdn-win

A Windows packet radio terminal. Plug in a radio interface, pick a mode, set the levels with the
built-in helper, and you are on the air: connected-mode sessions in tabs, a BPQ-style monitor, and
the pdn-soundmodem spectrum and waterfall, all in one window.

It runs [pdn-soundmodem](https://github.com/packet-net/pdn-soundmodem) inside the app (no separate
modem process, no KISS port) over [packet.net](https://github.com/packet-net/packet.net)'s AX.25
stack.

**Download** the MSI or the portable exe from
[Releases](https://github.com/packet-net/pdn-win/releases). What is done and what is next:
[docs/roadmap.md](docs/roadmap.md).

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
- **Windows audio hygiene**: on start the interface's endpoints are put right: nothing above
  0 dB, enhancements off, AGC off, monitor paths closed, "Listen to this device" off. Spatial sound
  is reported if it is on.
- **Beacon**: optional, never more often than every 5 minutes, never on start-up.
- **Layout**: every pane can be dragged by its tab onto another pane's centre (as a tab) or edge
  (as a split), resized, closed and brought back from View. The layout is remembered.

## Build and run

Requires the .NET 10 SDK. pdn-soundmodem comes from NuGet (`pdn-soundmodem` and
`pdn-soundmodem-windows`). To work on both at once, build against a local checkout with
`-p:PdnSoundModemRoot=C:\path\to\pdn-soundmodem`.

```
dotnet build
dotnet test
dotnet run --project src/PdnWin
```

Settings live in `%APPDATA%\pdn-win\settings.json`; unhandled errors are appended to
`%LOCALAPPDATA%\pdn-win\errors.log`.

### Without a radio

`pdn-win --simulate` runs against a simulated channel: a second soundmodem station, GB7SIM, on
the other end of a real-time audio link with hiss on it. It beacons, sends some made-up traffic,
and accepts connects (`C GB7SIM`), echoing what you type. Nothing touches a real device.

### Releases

See [docs/releasing.md](docs/releasing.md). `./build/build-release.ps1 -Version x.y.z` builds the
MSI and the portable exe into `dist/`.

### Development switches

- `PDNWIN_SETTINGS=path` uses a settings file of its own instead of the real one.
- `PDNWIN_SNAPSHOT=dir` makes every open window render itself to `dir\<title>.png` each second,
  for checking the UI from a script without taking focus.

## Layout of the code

- `src/PdnWin.Core` (no WPF, no Windows APIs, tested headless)
  - `Stations/`: the `IStationDevice` seam and its optional features; `SoundModem/SoundModemStation`
    runs the modem over any `ISoundCard`; `Simulation/` is the simulated channel.
  - `Sessions/`: `SessionManager`, many sessions over one packet.net listener.
  - `Monitoring/`, `Levels/`, `Beacons/`, `Settings/`.
- `src/PdnWin` (WPF)
  - `Hardware/`: the Windows sound card (WASAPI + PTT) and interface resolution.
  - `Docking/`: the dock host.
  - `Controls/`: meter, lamps, spectrum, waterfall, the terminal-style list.
  - `ViewModels/`, `Views/`, `Themes/`.
- `tests/PdnWin.Core.Tests`: including two complete stations talking over simulated audio.

## Other hardware

The station is an `IStationDevice`: a packet.net `IAx25Transport` for the session layer, a monitor
feed, and optional features (spectrum, input level, mode, TX test, channel access) that the UI
shows panes and controls for. A NinoTNC or a KISS TNC is another implementation of the same
interface; none is built yet.

## Licence

AGPL-3.0-or-later. See [LICENSE](LICENSE).
