# Changelog

Each release's notes are its section here, taken word for word by the release pipeline: a tag
`x.y.z` publishes the `## x.y.z` section, and a tag with no section is refused. Write for the
operator: what they can now do, what changed that they will notice, what was fixed. See
[docs/releasing.md](docs/releasing.md).

## Unreleased

- **Replies go when the channel is clear, not ten seconds later.** On an FM radio with its squelch
  open, the station took the channel to be busy for about ten seconds after everything it heard,
  so a reply to a node could wait 5 to 18 seconds, or on a busy channel not go at all, and links
  retried and gave up. Carrier sense now listens to the receiver's audio as pdn-soundmodem means
  it to, at the sound card's own rate; on air, replies went within a second.
- **Settings say what saving will do, and do only that.** The button is Save, and beside it the
  dialog says what happens: the station starts, if it is not running; it restarts, dropping any
  connected sessions, if you changed the callsign or the interface; otherwise the changes (mode,
  TXDELAY, TXTAIL, the session options, the beacon) are made on the running station and nothing
  drops. A save made while the station was still starting is no longer lost.
- **Persistence and slot time are no longer settings.** They are the channel's, not the
  station's: they only work if every station on the channel uses the same, and one that shortens
  its slot or raises its persistence takes the channel from the rest. They are fixed at the usual
  63 and 100 ms, including where an earlier version saved something else. TXDELAY and TXTAIL,
  which suit your radio, stay.

## 0.2.0

pdn-win now runs on Linux too, where it is called **pdn-lin**: the same app, the same window, the
same station, from the packet-net apt repository (`sudo apt install pdn-lin`) or a `.deb` here.

- **pdn-lin for Linux**, amd64 and arm64 (a PC, or a Raspberry Pi 4 or 5 on a 64-bit OS). Plug in
  an AIOC or a CM108 interface and it is found as one thing (sound card, PTT and serial port
  together), its mixer is put right (nothing above 0 dB, AGC and mic boost off, the CM108 monitor
  path closed) and its levels are the sliders. The package lets you key the radio without root
  and keeps the desktop's sound server off the AIOC. If something still holds the card, pdn-lin
  says what, and goes through PipeWire when it is PipeWire. Anything it cannot open is named,
  with the fix, before the station starts. Tried on air to GB7RDG from Linux and from Windows.
- **Why a frame waited.** One of your frames that waited a second or more for the channel says so in
  the monitor, with how long and why ("HELD 6.5s", "held: 6.5s channel busy"): the usual reason a
  link retried.
- **Plug and unplug.** A station waiting for its interface comes up the moment it is plugged in,
  on Windows and Linux, and the settings dialog's list updates by itself. Unplugging a running
  interface stops the station and it waits, saying so once rather than every few seconds.
- **A new front-end, the same app.** pdn-win is rebuilt on Avalonia so that one app serves both
  platforms. It looks and works as before, and a few things are better:
  - the callsign box remembers the stations you connected to and connects on Enter;
  - the monitor and the transcripts have Copy and Select all on the right-click menu, and copy in
    the order the lines are in;
  - the mode list says what each mode is;
  - the settings dialog shows only the PTT fields for the PTT you chose;
  - session tabs scroll instead of wrapping onto new rows;
  - the title bar has a window menu on right-click;
  - a pane being dragged can be put back with Escape.
- **Windows**: the MSI now needs the .NET 10 Runtime rather than the Desktop Runtime (Windows still
  offers it on first start). The portable exe needs nothing, as before.

Settings carry over on Windows (`%APPDATA%\pdn-win`); on Linux they are kept in
`~/.config/pdn-lin`.

## 0.1.0

First release: a Windows packet radio terminal with pdn-soundmodem running inside it, for an FM
handheld on an AIOC or CM108 interface.

- **Plug in and go.** The interface is found by itself, audio and PTT together (an AIOC shows up
  as "AIOC Audio, HID PTT, COM8"). Set your callsign, pick a mode, and the station is on the air.
- **FM modes**: 1200 AFSK (plain, multi-decoder, IL2P+CRC, FX.25) and 3600 QPSK (NinoTNC-compatible
  IL2P+CRC). 3600 QPSK has held a full session with GB7RDG on air.
- **Sessions in tabs.** Connect to several stations at once, each in its own tab with a
  transcript, disconnect and reconnect buttons. A console tab takes `C CALLSIGN` to connect and
  `U DEST[,DIGI] text` for unproto. Stations that connect to you get their own tab and a welcome
  text.
- **Monitor**: every frame heard or sent, BPQ style, coloured by kind, with the modem's own
  diagnostics under each received frame (SNR, frequency offset, level, FEC corrections) and a badge
  when a frame arrived too loud or too quiet.
- **Heard list**: who is on the channel, most recent first.
- **Spectrum and waterfall** in the pdn-soundmodem station page's look, as two separate panes, with
  every decoded frame labelled on the waterfall with its callsign and SNR. The waterfall's colours
  follow the noise floor, so open-squelch hiss reads as a dark ground.
- **Levels helper**: sets a handheld's volume by its open-squelch hiss (aim for -18 to -9 dBFS, never
  clipping) and then transmit deviation by Bessel null with a test tone.
- **Windows audio put right on start**: nothing above 0 dB, audio enhancements off, hardware AGC off,
  the CM108 mic-to-speaker monitor path closed, "Listen to this device" off. Spatial sound is
  reported if it is on.
- **Beacon**, off by default: never sent at start-up and never more often than every 5 minutes.
- **Your layout**: drag any pane by its tab onto another pane's centre or edge, resize, close panes
  and bring them back from View. It is remembered, with the window.
- **Try it without a radio**: `pdn-win --simulate` runs against a simulated channel with a node,
  GB7SIM, that accepts connects and echoes what you type.

Known limits: connects are direct (no digipeater path yet); FM only; no floating windows.
