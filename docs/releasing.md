# Releasing

This is the procedure. Follow it as written; if it is wrong, change this file in the same PR as
the fix.

## What a release is

A tag `x.y.z` (plain numbers, no `v`) pushed to `main`. `.github/workflows/release.yml` then runs
three jobs on GitHub-hosted runners:

1. **windows** (`windows-latest`): checks that `CHANGELOG.md` has a `## x.y.z` section and stops if
   it does not; runs both test projects; builds with `build/build-release.ps1 -Version x.y.z`:
   - `pdn-win-x.y.z-win-x64.msi`: installs to Program Files with a Start menu shortcut and
     upgrades an earlier version in place. It carries the framework-dependent single-file exe
     (Avalonia's native libraries inside it), so it needs the .NET 10 Runtime (x64), which
     Windows offers on first start if missing.
   - `pdn-win-x.y.z-win-x64-portable.exe`: one self-contained file (runtime included, about
     50 MB), no install;

   and writes the notes with `build/release-notes.ps1`: the `## x.y.z` section of `CHANGELOG.md`,
   then a Downloads section (Windows, then Linux with the apt lines) and a link to the changes
   since the previous tag.
2. **linux** (`ubuntu-latest`): runs both test projects and builds with
   `packaging/linux/build-deb.sh x.y.z amd64|arm64`, checking each with `check-deb.sh`:
   - `pdn-lin_x.y.z_amd64.deb`, `pdn-lin_x.y.z_arm64.deb`: self-contained, into
     `/usr/lib/pdn-lin` with `/usr/bin/pdn-lin`, a desktop entry and icons, the udev rule (CM108
     and AIOC PTT, the AIOC's serial port) and the WirePlumber 0.5 rule (0.4's is an example in
     `/usr/share/doc/pdn-lin/examples`, because 0.5 warns about Lua files). Their libc6
     and libstdc++6 floors are read from the binaries.
   - `pdn-lin-x.y.z-linux-x64.tar.gz`, `pdn-lin-x.y.z-linux-arm64.tar.gz`: the same payload, the
     rules in `rules/`.
3. **publish** (`ubuntu-latest`, tags only, after both): writes `SHA256SUMS`, publishes a GitHub
   Release **named `x.y.z`** with exactly those files and the notes, then dispatches
   `release-published` to packet-net/apt, which rebuilds its index from every tracked repo's
   latest release. That dispatch needs the org secret `APT_DISPATCH_TOKEN` (pdn-win is on its
   repository list), and a failed dispatch fails the release rather than leaving apt a version
   behind until its daily rebuild.

The version is stamped into the app from the tag (`-p:Version`, shown beside the wordmark), into
the MSI (`ProductVersion`) and into the `.deb`s. Nothing in the repository holds a version number
to bump.

## Steps

1. **Everything for the release is on `main`**, CI green.
2. **Write the notes.** Add a `## x.y.z` section at the top of `CHANGELOG.md` (below the intro,
   above the previous release). Write for the operator: what they can now do, what they will
   notice has changed, what was fixed; the reason where it is not obvious. Not a commit list. Keep
   the house style (no em or en dashes). Commit it to `main` (directly or by PR).
3. **Choose the number.** Semver as the operator sees it: a patch for fixes only, a minor for
   anything new, a major only for something that breaks settings or behaviour people rely on.
   While we are at 0.x, a minor may also change behaviour.
4. **Tag and push:**
   ```
   git tag x.y.z
   git push origin x.y.z
   ```
5. **Watch the run** (`gh run watch`) and check the release page: name `x.y.z`, the nine files
   (MSI, portable exe, two `.deb`s, two tarballs, `SHA256SUMS`), the notes as written. A minute
   later `https://packet-net.github.io/apt/Packages` lists `pdn-lin` at `x.y.z`.

If the run fails before publishing, fix forward on `main`, then move the tag:
`git tag -f x.y.z <commit> && git push -f origin x.y.z`. Never move a tag once its release is
published; release the next patch instead.

## Dry run

Actions > Release > Run workflow (or `gh workflow run release.yml -f version=x.y.z`) runs both
builds without publishing and uploads the assets and `notes.md` as workflow artifacts. Use it after
changing anything in `build/`, `installer/`, `packaging/` or the workflow. Locally,
`./build/build-release.ps1 -Version x.y.z` produces the Windows files in `dist/`, and
`packaging/linux/build-deb.sh x.y.z amd64` the Linux ones (on a Debian-family machine or WSL, with
`dpkg-deb` and `readelf`).

## When pdn-soundmodem changed too (the cascade)

pdn-win takes pdn-soundmodem from NuGet: `pdn-soundmodem`, `pdn-soundmodem-windows` and
`pdn-soundmodem-linux`, published together on one version by pdn-soundmodem's own `v*` release,
pinned in `Directory.Packages.props`.
When a pdn-win release needs a pdn-soundmodem change, the order is forced, because each link is a
real NuGet restore:

1. **pdn-soundmodem**: merge the change, check its `ci` run on the merge commit on `main` is green
   (investigate a red, do not re-run it away), then tag the next `v0.N.0` (list the tags with
   `sort -V`, never guess) and watch its `release` workflow push the three packages. If its
   tests fail there on something flaky that `ci` passed on the same commit, the standing
   instruction is to quarantine it (`[Trait("Category", "Quarantine")]` and an issue naming the
   runs), merge that, and move the tag: nothing has been published by a run that failed its
   tests.
2. **Wait for nuget.org to index** the new version of all three packages (a restore against an
   unindexed version 404s): `https://api.nuget.org/v3-flatcontainer/pdn-soundmodem-linux/index.json`
   (and the same for the other two) lists it when it is ready.
3. **Here**: bump the three pins in `Directory.Packages.props` in one commit, build and test,
   push, and let CI go green. Then carry on from Step 2 above.

While developing across both repos, build here with `-p:PdnSoundModemRoot=C:\path\to\pdn-soundmodem`
to use the checkout instead of the packages; never commit anything that depends on that.

## Rules

- **Never change the MSI `UpgradeCode`** in `installer/Package.wxs`: it is how a new version finds
  and replaces the old one.
- Versions are numeric `x.y.z`; MSI versions cannot carry a pre-release suffix.
- The release has exactly the assets above. Adding one means changing this file, the build
  scripts, the notes' Downloads section and the workflow together.
- The Linux package is called `pdn-lin` and its payload lives in `/usr/lib/pdn-lin`; renaming
  either strands every installed copy, so do not.
