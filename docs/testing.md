# Testing

Everything that ships is tested, and CI fails if coverage drops (`decisions.md`, Testing). There
are four kinds of tests:

| Tests | Where | What they cover |
|---|---|---|
| Unit tests (xUnit) | `MDGlint.Core.Tests`, `MDGlint.Desktop.Tests` | All of `MDGlint.Core` and the logic in `MDGlint.Desktop`, at 100% of lines and branches |
| Preview script tests (Node) | `MDGlint.Desktop.Tests/Preview` | `Assets/preview.js`, at 100% of lines, branches and functions |
| End-to-end tests (xUnit) | `MDGlint.EndToEnd.Tests` | The real app: window, WebView2 page, `app.js`, links, remote content, the update bar |
| Installer tests (PowerShell) | `.github/scripts/Test-Installer.ps1` | Updating from older versions and uninstalling, on CI only |

## Running them

```powershell
dotnet test MDGlint.slnx
node --test "MDGlint.Desktop.Tests/Preview/*.test.mjs"
```

`dotnet test` builds Debug by default, which the end-to-end tests need; in a Release build they
are skipped. They open MDGlint windows for a few seconds each and need the WebView2 Runtime.
They never touch your settings or your installed MDGlint (see "Test hooks"). GitHub's Windows
runners don't always have the runtime, so CI installs it first with
`.github/scripts/Install-WebView2.ps1` (Microsoft's Evergreen bootstrapper, signature checked;
CI only).

The preview tests need Node 22.8 or later (for the coverage thresholds); nothing is installed
from npm.

### Coverage

As CI checks it, from the repository root:

```powershell
foreach ($project in 'Core', 'Desktop') {
    dotnet test "MDGlint.$project.Tests" --settings coverage.runsettings --collect "XPlat Code Coverage" --results-directory "TestResults/$project"
}
node --test --experimental-test-coverage --test-coverage-include=MDGlint.Desktop/Assets/preview.js --test-reporter=spec --test-reporter-destination=stdout --test-reporter=lcov --test-reporter-destination=TestResults/preview.lcov "MDGlint.Desktop.Tests/Preview/*.test.mjs"
./.github/scripts/Test-Coverage.ps1
```

`Test-Coverage.ps1` lists any line or branch that isn't covered. Each assembly is judged by its
own test project, so `MDGlint.Desktop.Tests` can't make up for gaps in `MDGlint.Core.Tests`.
`coverage.runsettings` limits the reports to MDGlint's own assemblies. `TestResults/` is ignored
by git.

## What is left to the end-to-end tests

Code that only does something inside a real WPF window or WebView2 is marked
`[ExcludeFromCodeCoverage(Justification = "...")]`, and the justification says why and what
covers it: `App`, `MainWindow`, `PreviewHost`, and three one-line launchers,
`ProcessLauncher.ShowInExplorer`, `ProcessLauncher.Start` and `UpdateLauncher.StartInstaller`
(calling them from a unit test would start a program).
Their decisions are in plain classes with unit tests: `PreviewPolicy`, `RenderPlan`,
`UpdateCoordinator`, `RemoteContentPolicy`, `ThemeService`, `WindowPlacement`. `app.js` only wires `preview.js` to
the page, so it has no unit tests of its own.

Nothing else may be excluded. When logic grows in one of these files, move it into a class that
can be tested.

Not covered by any automated test, and checked by hand in the release smoke test
(`releasing.md`): the Open dialog, a drop on the toolbar (a drop on the preview is tested), the
title bar colour, and Windows asking which app opens `.md` files. SmartScreen and WebView2's
crash reporting being off are settings in `PreviewHost` and `MainWindow` with nothing a test can
observe; they are checked in code review.

## End-to-end tests

Each test starts the Debug build of `MDGlint.Desktop.exe` with a file and drives it:

- **The page**, over the DevTools protocol (`DevTools.cs`): WebView2 opens a debugging port when
  the tests pass `--remote-debugging-port=N` through `MDGLINT_DEBUG_BROWSER_ARGUMENTS` (below).
  Not through WebView2's own `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`: WebView2 ignores that
  variable when the app runs elevated, as everything does on CI runners. Tests read the
  DOM, run script, watch network responses, and click with real mouse events
  (`Input.dispatchMouseEvent`), because MDGlint only follows links the user clicked, and drop
  files with `Input.dispatchDragEvent`, which gives the page real files as Explorer does.
- **The window**, over UI Automation (`AppSession.cs`): buttons and text are found by their
  `x:Name`, which WPF exposes as the AutomationId. Give any new control the tests need an
  `x:Name`.
- **Remote servers** (an image host, GitHub) are a small HTTP server on 127.0.0.1
  (`LocalServer.cs`).

They check that a file's scripts never run (sanitizer and CSP), local images load and other
files don't, links go to the browser, Explorer or MDGlint and script-started navigation goes
nowhere, `#` links jump, the reader keeps their place when the file is saved, a Markdown file dropped on
the preview opens and others are ignored, the theme switches and is remembered, the window opens
maximized and then as it was left, remote content is blocked until allowed in each way the window offers, the
WebView2-missing notice shows, and the update bar: no request unless the user opted in,
**Install** only on request, a verified installer started with the right arguments, a hash
mismatch refused, **Check for updates now** and the automatic-check toggle.

A failed wait prints what the page showed (`DevTools.DescribeAsync`).

### Test hooks

Debug builds read four environment variables; Release builds don't contain the code that reads
them (`#if DEBUG`), so they can't change an installed MDGlint:

| Variable | Effect |
|---|---|
| `MDGLINT_DEBUG_DATA_FOLDER` | Settings and WebView2 data go under this folder (`Roaming\MDGlint`, `Local\MDGlint`) instead of `%APPDATA%` and `%LOCALAPPDATA%` |
| `MDGLINT_DEBUG_LAUNCH_LOG` | Programs MDGlint would start (browser, Explorer, setup) are written to this file as `file\|arguments` lines instead |
| `MDGLINT_DEBUG_BROWSER_ARGUMENTS` | Passed to WebView2 as additional browser arguments through its API (the tests' `--remote-debugging-port`); WebView2 honours these even when the app runs elevated |
| `MDGLINT_DEBUG_UPDATE_SERVER` | Update requests go to this `http://127.0.0.1:PORT` server, as `/host/path` (`api.github.com/repos/...`); only a loopback address is accepted |

The setup choice for update checks is still read from the registry, so tests that depend on
update checks set `CheckForUpdates` in their settings file.

## Update compatibility tests

Updates work only while setup, the release workflow and the app agree (`releasing.md`, "Keeping
updates working"). These tests read the real files:

- `ReleaseCompatibilityTests` (Desktop.Tests): the `AppId`, the installer and asset names, the
  hash file the workflow writes parsing with `UpdateService.ParseHashFile`, the tag format, the
  release job's `release` environment and the installer's build-provenance attestation,
  `/UPDATE=1`, the registry value setup writes for the update choice, the task names.
- `SettingsFixtureTests` (Core.Tests): `Fixtures/settings/<version>.json` holds `settings.json`
  as each release writes it, with every setting changed from its default. Each must load with
  all its values; there must be one for the current version, and the newest must change every
  setting, so a new setting gets a fixture before it ships. A released fixture is never edited.
- `build-installer.ps1` fails when the publish output has a file or folder that
  `[InstallDelete]` doesn't cover.

## Installer tests

`.github/scripts/Test-Installer.ps1` builds setup as two older versions and the current one, and
checks:

- **Updating** from the previous version and from one further back (a skipped version), with
  and without setup's tasks: run the new setup as MDGlint does (`/SILENT … /UPDATE=1`), then
  one MDGlint is installed, at the new version, files an older version had are gone, settings,
  the association and setup's update choice are kept, and MDGlint opens again.
- **Uninstalling** silently, answering **Yes** to keeping settings, and **No**: MDGlint, its
  registry entries and the association are gone; settings are kept, or with **No** both
  MDGlint folders are removed.

The older versions are today's code with a lower version number, so the tests prove setup
updates in place; they don't replay a particular old release. They use the real `AppId` and the
current user's install, so they replace any installed MDGlint and its settings: the script
refuses to run outside GitHub Actions unless given `-OnThisMachine`. CI runs them in their own
job.
