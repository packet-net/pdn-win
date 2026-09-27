# Roadmap

Where pdn-win is and where it is going. Keep it current: when something lands, move it to Done
with the version it shipped in; when something is decided, write it here.

## Done

### 0.2.0 (pdn-lin: the same app on Linux)

- **One front-end for both platforms.** The WPF app is retired; the app is Avalonia 12 over view
  models that know nothing of a UI framework (`PdnWin.Presentation`, with `IUiThread` and
  `IStationHardware` as its only outside needs). It is pdn-win on Windows and pdn-lin on Linux
  (`AppIdentity`), with the same look, panes, docking and behaviour, checked against the WPF app
  side by side and then file by file (window placement, crash handling, the settings dialog's PTT
  fields, recent callsigns, copy menus and the rest came across). Cascadia Mono travels with it.
- **Linux hardware** (`PdnWin.Linux`), over `pdn-soundmodem-linux` (packet-net/pdn-soundmodem#539,
  from v0.82.0): interfaces found by USB device in sysfs (an AIOC is one interface: card, hidraw
  node, serial port), what the operator cannot open named with the fix before anything is opened,
  mixer hygiene and levels on the ALSA mixer (nothing above 0 dB, AGC and boost off, the CM108
  monitor path closed without muting capture), ALSA audio with CM108 or serial PTT, and going
  through PipeWire with a note when the desktop holds the card. Proven on a real AIOC on Debian 13:
  discovered, levels at 0 dB, the station live on qpsk3600 receiving.
- **Hot-plug, done and exercised.** The station reacts to interfaces arriving (inotify on `/dev`
  on Linux, configuration manager notifications on Windows) rather than polling, and the settings
  dialog rescans itself. Unplugged and replugged under a running station (usbip): on Linux it went
  from live to "not plugged in; waiting" and back to live on its own; on Windows a station waiting
  for its AIOC came up within seconds of it arriving. A waiting station now says so once.
- **Packaging and release.** One tag builds everything: the MSI and portable exe on Windows, and
  `pdn-lin` `.deb`s (amd64, arm64) and tarballs on Linux, with a desktop entry, icons, a udev rule
  (CM108 and AIOC PTT, the AIOC's serial port, to the logged-in user and the `audio` group) and
  a WirePlumber rule keeping the desktop off the AIOC. The `.deb`s are served by packet-net/apt
  (`sudo apt install pdn-lin`); the release tells it to reindex. Tried on Debian 13: installs with
  apt, the rule applies, the app runs.
- **CI on both platforms**, including headless UI tests of the real windows (`tests/PdnWin.Tests`)
  and a build and check of the Linux package.
- Upstream while doing it: pdn-soundmodem's daemon `.deb` now installs a udev rule for C-Media and
  AIOC PTT too, instead of asking for one written by hand.

### 0.1.0 (first release)

- **In-process soundmodem station.** pdn-soundmodem runs inside the app over any `ISoundCard`:
  one receive thread (metering, decimation, waterfall, modem), the channel's own transmitter
  (p-persistent CSMA, PTT, drain before unkey), mode changes without dropping sessions. Proven
  end to end in tests over simulated real-time audio, and on air.
- **Windows audio and PTT**, upstream in pdn-soundmodem as `Packet.SoundModem.Windows`
  (packet-net/pdn-soundmodem#536), published to NuGet as `pdn-soundmodem-windows` from
  pdn-soundmodem v0.81.0 and consumed here as a package: WASAPI capture and render, CM108/AIOC HID PTT (serial PTT via
  the core's `SerialPtt`), radio interface discovery by container ID, endpoint levels capped at
  0 dB, and endpoint hygiene (enhancements, AGC, monitor path, Listen, spatial sound).
- **FM modes** for a handheld on mic and speaker: afsk1200 (plain, multi, IL2P+CRC, FX.25) and
  qpsk3600. **qpsk3600 proven on air** against GB7RDG, both directions (2026-09-26), its first
  on-air validation.
- **Sessions**: many at once over one packet.net listener, a tab each; incoming connects with a
  welcome text; console commands (`C`, `U`); PACLEN splitting; line reassembly across frames.
- **Monitor** (BPQ style, coloured, with receive diagnostics and level badges), **Heard** list,
  **spectrum** and **waterfall** (inferno map, auto-ranged to the noise floor, callsign and SNR tags
  on decoded frames), each a dockable pane.
- **Docking**: drag panes by their tabs onto centres (tab) or edges (split), resize, close,
  restore from View, layout remembered. Our own, because AvalonDock's MS-PL is GPL-incompatible.
- **Levels helper** for open-squelch FM: volume by hiss, TX deviation by Bessel null.
- **Beacon** (never at start-up, at least 5 minutes apart), **settings** dialog, first-run flow.
- **Simulator** (`--simulate`): a node, GB7SIM, and made-up traffic over simulated audio.
- **The station device seam** (`IStationDevice` plus optional features) that other hardware
  plugs into.
- **Release pipeline**: tag `x.y.z`, MSI plus portable exe, notes from the changelog
  (docs/releasing.md).

## Outstanding

### Hardware and radios

- **NinoTNC** (FM first, then HF, below). An `IStationDevice` over packet.net's
  `Packet.Kiss.NinoTnc` (`NinoTncSerialPort`: port discovery by USB ID 04D8:00DD, `SetModeAsync`),
  with the mode picker driving the TNC's mode switches. It has no spectrum or input level, so those
  panes stay empty and say why. The monitor comes from a tee on the KISS transport.
- **Tait (FM).** A Tait mobile (TM8100 family) on a sound card interface, with the radio's control
  link (packet.net's `Packet.Ax25.Radio.Tait`, CCDI) supplying carrier sense and signal strength
  instead of the audio-derived detector, and channel selection from the app. pdn-soundmodem
  already takes a radio busy source (`IChannelBusySource`); the station needs the Tait link opened
  beside the card and passed in. Also a Tait-plus-NinoTNC pairing.
- **FlexRadio.** A Flex 6000-series slice as the station: DAX audio in and out, PTT and tuning
  over the Flex API (`M0LTE.Flex`), no sound card. pdn-soundmodem supports `flex:` devices for the
  daemon and node; the app needs the same, plus slice and frequency selection in settings.
- **SSB (Flex and NinoTNC).** HF operation on both: the HF modes (afsk300 family, bpsk300 and
  bpsk1200, qpsk600 and qpsk2400, and later the OFDM and MIL-STD modes), a dial frequency with
  USB/LSB and RF-scaled rulers on the spectrum and waterfall (as the station page does), the
  two-tone TX test for ALC, and HF-appropriate defaults (TXDELAY, persistence, PACLEN). The
  `FmModes` list becomes a per-station mode set.
- **Other CM108 interfaces** (Digirig, DRA boards, homebrew): discovery and PTT should already
  work; confirm on real units and note any that need a different GPIO.
- **Unplugging on Windows under a running station**: the Linux side was exercised both ways and
  Windows for arrival; Windows departure (WASAPI reporting the device gone) needs the interface
  taken away while in use, which usbip can only do with `usbipd bind --force`, or a hand on the
  cable.

### APRS

- **APRS messaging**: send and receive APRS messages with acknowledgements and retries, a
  conversation view per station, bulletins, and a station list with last position and status. Built
  on packet.net's `Packet.Aprs`. Likely a new pane (or pair of panes) beside Sessions. Later:
  position beaconing with a fixed position, and a map.

### Links

- **The Links view from pdn-soundmodem's station page**: packet.net's passive link observer
  (`Ax25LinkObserver`) interpreting everything heard into one card per pair of stations, showing
  connections as they are made, used and dropped, with each card's recent lines in classic monitor
  decode. Filters for UI frames (beacons, idents, other unconnected traffic) and Mine (links this
  station is one end of), and a transcript export per card as markdown. Our own lines carry the
  station page's `HELD` tag when a frame waited for the channel, with what the wait was (`busy`,
  `our tx`): in process the channel reports this directly (`FrameTransmittedWithReport`), which is
  exactly what a KISS host cannot see and why a run of repeated polls is a channel-access story
  rather than a failing link. A pane of its own, beside or instead of the Monitor.

### Sessions and terminal

- **Digipeated connects** (`C GB7XX V GB7YY`): needs a digipeater path on packet.net's
  `Ax25Listener.ConnectAsync`; the UI refuses it with a reason until then.
- **Session logging** to files, per station and date.
- **YAPP** file transfer, QtTermTCP style.
- **KISS TCP server**: offer the in-process modem to other programs (BPQ, Winlink Express, APRS
  clients) as the daemon does; pdn-soundmodem's `KissTcpServer` is in the library.
- **Notifications**: a sound and a flash on an incoming connect or a message.

### Display

- **Floating panes** (tear a pane out into its own window; multi-monitor).
- Waterfall controls: span, speed, a manual level range beside Auto, and a visible line for our
  own transmissions.
- Constellation pane for the PSK modes (pdn-soundmodem has `ConstellationSource`).

### pdn-lin, and other platforms

- **On air on Linux, and with the new front-end on Windows.** qpsk3600 was proven on air with the
  WPF front-end; the Avalonia one runs the same station code, and on Linux the station has been
  live on the real AIOC receiving, but neither has yet made a connect to GB7RDG. Do it before
  calling 0.2.0 proven, and record it here.
- **Raspberry Pi.** The arm64 `.deb` is built and checked but not yet run on a Pi: the waterfall
  at 30 lines a second plus qpsk3600 on a Pi 4 or 5, possibly with software rendering, is the
  question.
- **PipeWire on a real desktop.** The WirePlumber rule (0.5 `.conf`, 0.4 `.lua`) and the fallback
  through `pipewire:NODE=` are written against PipeWire's documented properties but have not met a
  desktop that holds an AIOC; the test machine's Linux has no PipeWire running. Check on a GNOME
  or KDE desktop: the rule leaves the AIOC out of the sound settings, and with the rule removed,
  the station goes through PipeWire and says so.
- **A per-device "leave this interface alone" for CM108 dongles.** The packaged rule covers the
  AIOC only, because a C-Media chip is as likely to be someone's headset; the app could write a
  WirePlumber rule for the one interface the operator chose.
- **Wayland.** Avalonia runs through XWayland on a Wayland desktop, which works; native Wayland
  when Avalonia has it.
- **macOS.** Nothing in the way above the hardware layer: Avalonia, the view models, the core and
  the modem all run there. What it needs is a third `IStationHardware`: CoreAudio capture and
  playback (as `pdn-soundmodem-windows` does WASAPI), CM108 PTT through IOKit HID, serial through
  the core's `SerialPtt` as it is, discovery grouping them by USB device, and levels on the
  CoreAudio device. Then an `.app` bundle, universal (arm64 and x64), and signing and
  notarisation with an Apple Developer ID, without which Gatekeeper refuses it. Testing needs a
  Mac with the interface on it.
- **`--list-interfaces` for pdn-soundmodem's daemon**, from the same discovery: held on licence, not
  effort (the library is AGPL, the daemon GPL); see pdn-soundmodem's roadmap.

### Platform

- **Code signing** for the MSI and exe, so SmartScreen does not warn.
- **Update check** against the GitHub releases.

### Upstream rough edges found by the first in-process use

- packet.net `Ax25Listener`: `SessionAccepted` also fires for our own outbound connects, contrary
  to its doc comment, and data can arrive before `ConnectAsync` returns. Worked around here by
  routing on the peer's callsign; worth fixing or documenting at source.
- packet.net `Ax25Listener`: no digipeater path on connects or on `SendUiAsync` (we build UI frames
  ourselves).
- pdn-soundmodem `UpsamplingAudioOutput.Dispose` disposes the output it wraps, so a pipeline
  rebuilt on a mode change must not dispose it.
- packet.net node `SoundModemFrameTransport` appears (from reading only) to pass monitor-only
  frames up to its session layer.
- Keep an off-air qpsk3600 capture from the next GB7RDG session as a pdn-soundmodem fixture.
