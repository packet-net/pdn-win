# CLAUDE.md

Notes for anyone (human or agent) changing pdn-win. The README says what it does.

## Rules

- **Licence**: AGPL-3.0-or-later, everything. New dependencies must be compatible (MIT, Apache-2.0,
  BSD, LGPL, GPL, AGPL). Not MS-PL: that is why there is a dock host here and not AvalonDock.
- **House style** (as packet.net and pdn-soundmodem): no em dashes or en dashes anywhere, in code,
  comments, docs or commit messages. net10.0, nullable, warnings as errors, central package
  management. Tests: xUnit v3 + AwesomeAssertions, `Snake_case_sentence` names.
- **Nothing above 0 dB**, anywhere: Windows endpoint levels, ALSA mixer levels, hardware gain
  stages. Audio enhancements, spatial sound, AGC and mic boost off on the interface, and the
  CM108 monitor path closed. `EndpointHygiene` (pdn-soundmodem-windows) and `MixerHygiene`
  (pdn-soundmodem-linux) enforce this at station start; keep it that way.
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
- CI and releases run on GitHub-hosted runners: `windows-latest` for the Windows assets,
  `ubuntu-latest` for the Linux ones and for publishing. This is a public repository (hosted
  runners are free) and CI runs on pull requests, which must not run on the org's self-hosted
  machines.
- One release carries both platforms: the MSI and portable exe (pdn-win), and the amd64 and arm64
  `.deb`s and tarballs (pdn-lin). The release then dispatches `release-published` to
  packet-net/apt, which serves the `.deb`s; a failed dispatch fails the release.
- pdn-soundmodem is a NuGet dependency (`pdn-soundmodem`, `pdn-soundmodem-windows`,
  `pdn-soundmodem-linux`, one version). A change needing both repos is a cascade: pdn-soundmodem
  release first, wait for indexing, then bump the pins here (docs/releasing.md).

## Where things are

- **One app, two names.** `src/PdnWin` is an Avalonia app that runs on Windows as pdn-win and on
  Linux as pdn-lin (`AppIdentity` sets the name, title, wordmark and settings folder). There is no
  separate Linux front-end; a change to a view is a change on both.
- `PdnWin.Core` has no UI framework and no platform APIs, and `PdnWin.Presentation` (view models,
  dock model) no UI framework. Keep it that way; Core is tested headless, including end to end
  over simulated audio (`SoundModemStationTests`), and what a view model needs from outside comes
  through `Hosting/` (`IUiThread`, `IStationHardware`).
- `PdnWin.Windows` and `PdnWin.Linux` are the two `IStationHardware`s, chosen at start-up by OS.
  Both build everywhere; each marks its platform.
- The audio, PTT and discovery code lives upstream in pdn-soundmodem: the core's ALSA, CM108 and
  serial classes, `src/Packet.SoundModem.Windows` (`pdn-soundmodem-windows`) and
  `src/Packet.SoundModem.Linux` (`pdn-soundmodem-linux`). Fix audio problems there; build this
  repo with `-p:PdnSoundModemRoot=...` (or `PDN_SOUNDMODEM_ROOT` for `build-deb.sh`) to try a fix
  before it is released.
- `SessionManager` routes everything by the peer's callsign, because packet.net's listener raises
  `SessionAccepted` for outbound connects too and can deliver data before `ConnectAsync` returns.

## Checking the UI

`tests/PdnWin.Tests` drives the real windows headless (Avalonia.Headless); add to it when a view
changes. To look at the app, run with `PDNWIN_SNAPSHOT=dir` and read the PNGs, or capture the
window; drive it on Windows with UI Automation (buttons carry `AutomationProperties.Name`, the
input box is AutomationId `Line`, settings fields `Call`, `PttBox`, `GpioBox`). `--simulate` gives
a channel with traffic and a node (GB7SIM) that accepts connects; `--demo` adds a scripted session.

Linux is tried on this machine under WSL (Debian 13, WSLg shows the window): publish for
`linux-x64` or build the `.deb` from a copy of the tree in the WSL home (not `/mnt/c`, whose
`obj/` the Windows build shares), and drive it with `xdotool`. The AIOC can be moved into WSL
with `usbipd attach --wsl --busid <id>` (it is then gone from Windows until detached) and appears
as `AllInOneCable`, `/dev/hidraw0` and `/dev/ttyACM0`. With the real AIOC attached, GB7RDG is
reachable on qpsk3600 (callsign M0LTE); any transmission is a real one.
