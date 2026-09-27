# Releasing

This is the procedure. Follow it as written; if it is wrong, change this file in the same PR as
the fix.

## What a release is

A tag `x.y.z` (plain numbers, no `v`) pushed to `main`. `.github/workflows/release.yml` then, on a
GitHub-hosted Windows runner:

1. checks that `CHANGELOG.md` has a `## x.y.z` section, and stops if it does not;
2. runs the tests;
3. builds both assets with `build/build-release.ps1 -Version x.y.z`:
   - `pdn-win-x.y.z-win-x64.msi`: installs to Program Files with a Start menu shortcut and
     upgrades an earlier version in place. It carries the framework-dependent single-file exe, so
     it needs the .NET 10 Desktop Runtime (x64), which Windows offers on first start if missing.
   - `pdn-win-x.y.z-win-x64-portable.exe`: one self-contained file (runtime included, about
     65 MB), no install;
4. writes the notes with `build/release-notes.ps1`: the `## x.y.z` section of `CHANGELOG.md`,
   then a Downloads section and a link to the changes since the previous tag;
5. publishes a GitHub Release **named `x.y.z`** with exactly those two files and those notes.

The version is stamped into the app from the tag (`-p:Version`, shown beside the wordmark) and
into the MSI (`ProductVersion`). Nothing in the repository holds a version number to bump.

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
5. **Watch the run** (`gh run watch`) and check the release page: name `x.y.z`, two assets, the
   notes as written.

If the run fails before publishing, fix forward on `main`, then move the tag:
`git tag -f x.y.z <commit> && git push -f origin x.y.z`. Never move a tag once its release is
published; release the next patch instead.

## Dry run

Actions > Release > Run workflow (or `gh workflow run release.yml -f version=x.y.z`) runs the whole
build without publishing and uploads the assets and `notes.md` as a workflow artifact. Use it after
changing anything in `build/`, `installer/` or the workflow. Locally, `./build/build-release.ps1
-Version x.y.z` produces the same files in `dist/`.

## When pdn-soundmodem changed too (the cascade)

pdn-win takes pdn-soundmodem from NuGet: `pdn-soundmodem` and `pdn-soundmodem-windows`, published
together on one version by pdn-soundmodem's own `v*` release, pinned in `Directory.Packages.props`.
When a pdn-win release needs a pdn-soundmodem change, the order is forced, because each link is a
real NuGet restore:

1. **pdn-soundmodem**: merge the change, check its `ci` run on the merge commit on `main` is green
   (investigate a red, do not re-run it away), then tag the next `v0.N.0` (list the tags with
   `sort -V`, never guess) and watch its `release` workflow push both packages.
2. **Wait for nuget.org to index** the new version of both packages (a restore against an
   unindexed version 404s): `https://api.nuget.org/v3-flatcontainer/pdn-soundmodem-windows/index.json`
   lists it when it is ready.
3. **Here**: bump both pins in `Directory.Packages.props` in one commit, build and test, push, and
   let CI go green. Then carry on from Step 2 above.

While developing across both repos, build here with `-p:PdnSoundModemRoot=C:\path\to\pdn-soundmodem`
to use the checkout instead of the packages; never commit anything that depends on that.

## Rules

- **Never change the MSI `UpgradeCode`** in `installer/Package.wxs`: it is how a new version finds
  and replaces the old one.
- Versions are numeric `x.y.z`; MSI versions cannot carry a pre-release suffix.
- The release has exactly the two assets above. Adding one means changing this file, the build
  script, the notes' Downloads section and the workflow together.
