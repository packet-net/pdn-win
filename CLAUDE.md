# CLAUDE.md

Notes for anyone (human or agent) changing pdn-win. The README says what it does.

## Rules

- **Licence**: AGPL-3.0-or-later, everything. New dependencies must be compatible (MIT, Apache-2.0,
  BSD, LGPL, GPL, AGPL). Not MS-PL: that is why there is a dock host here and not AvalonDock.
- **House style** (as packet.net and pdn-soundmodem): no em dashes or en dashes anywhere, in code,
  comments, docs or commit messages. net10.0, nullable, warnings as errors, central package
  management. Tests: xUnit v3 + AwesomeAssertions, `Snake_case_sentence` names.
- **Nothing above 0 dB**, anywhere: Windows endpoint levels, hardware gain stages. Audio
  enhancements, spatial sound and AGC off on the interface. `EndpointHygiene` in
  pdn-soundmodem's Windows library enforces this at station start; keep it that way.
- **Open squelch.** FM stations run with the squelch open; the level helper and carrier sense
  assume it.
- **It transmits.** Anything that keys the radio must be something the operator asked for. The
  beacon never fires on start-up and never more often than every 5 minutes.
- **FM first.** The mode list is `FmModes`. HF (SSB, two-tone test, dial) comes later behind the
  same seams.

## Releasing and the roadmap

- **Releases follow [docs/releasing.md](docs/releasing.md) exactly**: a `## x.y.z` section in
  `CHANGELOG.md`, then a plain `x.y.z` tag on `main`; the workflow builds the MSI and the portable
  exe and publishes a release named `x.y.z`. Never change the MSI `UpgradeCode`.
- **[docs/roadmap.md](docs/roadmap.md) is the one list of done and outstanding work.** Move items
  to Done with the version they shipped in; add decisions as they are made.
- CI and releases run on GitHub-hosted `windows-latest` (public repo, WPF needs Windows, the org's
  self-hosted runners are Linux). pdn-soundmodem is checked out at the commit in
  `build/pdn-soundmodem.ref` until its Windows package is published.

## Where things are

- `PdnWin.Core` has no WPF and no Windows APIs. Keep it that way; it is tested headless,
  including end to end over simulated audio (`SoundModemStationTests`).
- The Windows audio and PTT code lives upstream in pdn-soundmodem, `src/Packet.SoundModem.Windows`,
  referenced from the sibling checkout (`PdnSoundModemRoot`). Fix Windows audio problems there.
- `SessionManager` routes everything by the peer's callsign, because packet.net's listener raises
  `SessionAccepted` for outbound connects too and can deliver data before `ConnectAsync` returns.

## Checking the UI

Run with `PDNWIN_SNAPSHOT=dir` and read the PNGs; drive it with UI Automation (buttons carry
`AutomationProperties.Name`, the input box is AutomationId `Line`). `--simulate` gives a channel
with traffic and a node (GB7SIM) that accepts connects. With the real AIOC attached, GB7RDG is
reachable on qpsk3600 (callsign M0LTE); any transmission is a real one.
