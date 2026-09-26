# Changelog

Each release's notes are its section here, taken word for word by the release pipeline: a tag
`x.y.z` publishes the `## x.y.z` section, and a tag with no section is refused. Write for the
operator: what they can now do, what changed that they will notice, what was fixed. See
[docs/releasing.md](docs/releasing.md).

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
