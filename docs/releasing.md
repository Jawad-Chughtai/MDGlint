# Releasing

Releases are built by the **Release** workflow (`.github/workflows/release.yml`) when a version
tag is pushed; the steps around it stay manual. Never publish a locally built installer.
`main` is protected, so release changes go through a pull request.

## Prerequisites

- The release's milestone is complete and the **CI** workflow passes on `main`.
- The repository settings from `decisions.md`, "Releases need approval and are attested": the
  `release` environment (you as required reviewer, `v*` tags only), the tag ruleset (only an
  admin creates `v*` tags; none is moved or deleted) and immutable releases. The environment
  and the attestation need the repository to be public on GitHub's free plan.
- The [GitHub CLI](https://cli.github.com/), to verify the attestation in step 7.
- To try the installer before tagging (optional): .NET 10 SDK and Inno Setup 6.3 or later
  (`ISCC.exe` on PATH or in its default folder).

## Steps

1. **Pick the version** using [Semantic Versioning](https://semver.org): patch for fixes, minor
   for features, major for breaking changes. A version with a suffix (`1.1.0-beta.1`) becomes a
   pre-release.
2. **Open a release PR** titled `Release vx.y.z` that:
   - sets `<Version>` in `Directory.Build.props`, the only place the version is set (the
     installer, the exe's file details and the toolbar all take it from there);
   - in `CHANGELOG.md`, renames **Unreleased** to `[x.y.z] - YYYY-MM-DD`, removes any planning
     note under it (the section becomes the release notes as written), adds a fresh empty
     **Unreleased** section, and updates the links at the bottom (`[x.y.z]` to the release tag,
     `[Unreleased]` to `compare/vx.y.z...HEAD`);
   - removes the shipped release's section from `docs/roadmap.md`;
   - adds `MDGlint.Core.Tests/Fixtures/settings/x.y.z.json`, unless the PR that added a setting
     already did: `settings.json` as this version writes it, with every setting changed from its
     default (copy the newest fixture and add any new settings). The tests fail without it (`testing.md`, "Update compatibility tests").
3. **Merge it**, then update your local `main`:

   ```powershell
   git checkout main
   git pull
   ```

4. **Tag the merged commit and push the tag**:

   ```powershell
   git tag vx.y.z
   git push origin vx.y.z
   ```

   The Release workflow's run then **waits for your approval**: open it under Actions, check
   that it is the tag you just pushed, and choose **Review deployments** → `release` →
   **Approve and deploy**. A run you didn't start is a warning sign: reject it and check the
   account. Once approved, it checks that the tag matches `<Version>`, runs the tests, builds
   the installer on a clean runner, attests its build provenance, and creates a **draft**
   GitHub Release titled `MDGlint x.y.z` with `MDGlintSetup.exe`, `MDGlintSetup.exe.sha256`,
   and the changelog section plus the SHA-256 as its notes. It stops without a release if any
   of these fail, including a missing or empty changelog section. To retry, fix the cause,
   delete the tag (and any draft; the tag ruleset lets an admin do this), and tag again.
5. **Smoke-test** the `MDGlintSetup.exe` from the draft on a clean machine or VM:
   - install the previous release, then run the draft's installer over it, and check that
     MDGlint opens with its settings and association kept (MDGlint's own **Install** button
     can only be tried once the release is published, since it reads the latest published
     release);
   - double-click a `.md` and a `.markdown` file (confirm MDGlint if Windows asks), toggle the
     theme and check the title bar follows it, edit the file and watch it reload;
   - open a file with the **Open** button and another by dragging it onto the toolbar (the
     automated tests can't do these, `testing.md`);
   - uninstall, choose **No** when asked to keep settings, and check that `%APPDATA%\MDGlint`
     and `%LOCALAPPDATA%\MDGlint` are gone;
   - then install the draft fresh for the current user with the association ticked, and check
     the file types open in MDGlint.

   Where you can, also install on a machine or VM without the WebView2 Runtime and check that
   setup and the app both link to Microsoft's download page, and on Windows older than 10
   version 1607 (for example 8.1) check that setup refuses to install.
6. **Capture the showcase**: light and dark screenshots or a short GIF of the release's headline
   feature, using a sample document with no personal files, paths or names visible
   (`docs/images/sample.md` is the one the README screenshots show). If the window looks
   different, retake `docs/images/screenshot-light.png` and `screenshot-dark.png` too.
7. **Publish the draft**: add the showcase images to its notes, check the notes and the hash,
   verify the downloaded installer's attestation
   (`gh attestation verify MDGlintSetup.exe -R Jawad-Chughtai/MDGlint`), and publish it.
   Publishing it is what offers it to every copy of MDGlint that checks for updates; a draft or
   pre-release is never offered. A published release is immutable: its files and tag can't
   change, so a mistake is fixed with a new version, and a version number is never reused.
8. **Close the milestone.**

## Keeping updates working

Every released version checks the latest release and can install it, so each release must
work for all of them, however old. These rules are why (docs/decisions.md, "Updates keep
working"); the tests that guard them are in `testing.md` ("Update compatibility tests" and
"Installer tests"), and CI runs them on every change.

1. **Never change the `AppId`** in `installer.iss`. Setup finds the installed copy by it; a new
   one installs a second MDGlint beside the first.
2. **Never rename or remove a setting** in `settings.json` without code that reads the old name.
   Every version must load the file any older one wrote; new settings need a default that
   suits users who never saw them.
3. **Let setup clean up.** `[InstallDelete]` in `installer.iss` clears the files setup puts in
   place before it installs, so files an older version left behind don't linger. When the
   release adds a new kind of file or folder, add it there too; `build-installer.ps1` stops
   until you do.
4. **Keep the release format**: tags `vx.y.z`, assets named `MDGlintSetup.exe` and
   `MDGlintSetup.exe.sha256` (the hash at the start of the file, as the workflow writes it), from
   this repository. Older
   versions look for exactly these; a release without them is offered only as a link to its
   page.
5. **A release older versions shouldn't install themselves** (it needs a newer Windows, a
   changed install location, or a step setup can't do) is published without the
   `MDGlintSetup.exe.sha256` asset: delete it from the draft before publishing and keep the hash
   in the notes. MDGlint then shows the update with a link to its page instead of an
   **Install** button, and the notes say what to do.
