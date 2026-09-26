# Roadmap

Where pdn-win is and where it is going. Keep it current: when something lands, move it to Done
with the version it shipped in; when something is decided, write it here.

## Done

### 0.1.0 (first release)

- **In-process soundmodem station.** pdn-soundmodem runs inside the app over any `ISoundCard`:
  one receive thread (metering, decimation, waterfall, modem), the channel's own transmitter
  (p-persistent CSMA, PTT, drain before unkey), mode changes without dropping sessions. Proven
  end to end in tests over simulated real-time audio, and on air.
- **Windows audio and PTT**, upstream in pdn-soundmodem as `Packet.SoundModem.Windows`
  (packet-net/pdn-soundmodem#536): WASAPI capture and render, CM108/AIOC HID PTT (serial PTT via
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
- **Hot-plug**: the station retries when its interface is missing or fails, which is written but
  not yet exercised by unplugging a live interface. Test it, and react to device arrival rather
  than polling.

### APRS

- **APRS messaging**: send and receive APRS messages with acknowledgements and retries, a
  conversation view per station, bulletins, and a station list with last position and status. Built
  on packet.net's `Packet.Aprs`. Likely a new pane (or pair of panes) beside Sessions. Later:
  position beaconing with a fixed position, and a map.

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

### Platform

- **Publish `pdn-soundmodem-windows`** from pdn-soundmodem's release pipeline, then reference the
  package here instead of a pinned source checkout.
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
