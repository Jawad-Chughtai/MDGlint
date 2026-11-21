# MDGlint — Project Index

Where things live. Keep this current when files are added, moved, or removed.

## Root
| Path | What |
|---|---|
| `README.md` | User-facing overview: features, install, requirements, build |
| `CONTRIBUTING.md` | Contributor setup and pull request process |
| `SECURITY.md` | Vulnerability reporting and the security model, with known issues |
| `CHANGELOG.md` | Release history (Keep a Changelog) |
| `THIRD-PARTY-NOTICES.md` | Licenses of dependencies and bundled assets |
| `LICENSE` | MIT |
| `.gitattributes` | Keeps `Assets/vendor/` files byte-for-byte as published |
| `AGENTS.md` | Agent rules, owner-maintained (`CLAUDE.md` points to it) |
| `MDGlint.slnx` | Solution |
| `Directory.Build.props` | The app version and product name, and the path map that keeps the build folder out of Release binaries; shared by every project |
| `.github/ISSUE_TEMPLATE/` | Bug and feature forms; security link (`config.yml`) |
| `.github/PULL_REQUEST_TEMPLATE.md` | PR checklist |
| `.github/dependabot.yml` | Weekly update PRs for the NuGet packages and the pinned GitHub Actions |
| `coverage.runsettings` | Coverage settings for `dotnet test`: which assemblies, what is excluded |
| `.github/workflows/ci.yml` | Every pull request and push to `main`: Release and Debug builds, unit, preview-script and end-to-end tests, the coverage check; installer tests in a job of their own |
| `.github/workflows/release.yml` | On a `v*` tag, after the maintainer approves the run: tests, builds and attests the installer, creates the draft GitHub Release |
| `.github/scripts/Get-ReleaseNotes.ps1` | Prints a version's `CHANGELOG.md` section for the release notes |
| `.github/scripts/Install-WebView2.ps1` | Installs the WebView2 Runtime on the CI runner if it is missing; CI only |
| `.github/scripts/Test-Coverage.ps1` | Fails unless Core, Desktop and `preview.js` are fully covered, listing what isn't |
| `.github/scripts/Test-Installer.ps1` | Installer tests: updates from older versions, uninstalling; CI only (replaces any installed MDGlint) |

## docs/
| Path | What |
|---|---|
| `docs/project-index.md` | This file |
| `docs/architecture.md` | Projects, rendering flow, runtime data locations |
| `docs/decisions.md` | Decision log with reasons |
| `docs/roadmap.md` | Planned releases in priority order |
| `docs/issue-tracking.md` | GitHub Issues: labels, milestones, workflow, agent permissions |
| `docs/releasing.md` | Steps to cut a release |
| `docs/images/` | README screenshots, and `sample.md`, the document they show |
| `docs/testing.md` | The kinds of tests, how to run them and check coverage, the Debug test hooks |

## MDGlint.Core (net10.0, no Windows dependencies)
| Path | What |
|---|---|
| `Contracts/IMarkdownFileReader.cs` | Reads a Markdown file's text |
| `Contracts/ISettingsStore.cs` | Loads and saves `AppSettings` |
| `Models/AppSettings.cs` | Persisted settings (theme, always allow remote content, update checks, window state) |
| `Models/AppVersion.cs` | Version text shown to users, without build metadata |
| `Models/SemanticVersion.cs` | Parses and orders versions and release tags (SemVer 2.0) |
| `Models/UpdateInfo.cs` | Update source and asset names, an available update, check results, the verified installer |
| `Models/UpdatePreference.cs` | Whether update checks are on (in-app choice, then setup's) and when one is due |
| `Models/PreviewUnavailableNotice.cs` | Texts and download link shown when WebView2 is missing or fails to start |
| `Models/ThemeKind.cs` | `Light`/`Dark` and their storage strings |
| `Models/RenderedDocument.cs` | Rendered HTML fragment plus the folder it resolves against |
| `Services/AppSettingsSerializer.cs` | `settings.json` text to `AppSettings` and back, with defaults for missing or damaged files |
| `Services/HtmlContentSanitizer.cs` | Allow-list sanitizer for the rendered HTML (HtmlSanitizer) |
| `Services/MarkdownRenderer.cs` | Markdig pipeline; Markdown to an HTML fragment, sanitized, or a message fragment on failure |
| `Services/RemoteContentPolicy.cs` | Remote-content choices (per file, always) and what counts as remote |
| `Services/RenderPlan.cs` | For each render: new document or the same one saved, remote content allowed, hide the remote bar |
| `Services/ThemeService.cs` | Current theme and toggle |
| `Services/UpdateCoordinator.cs` | The update bar's states and flow: checks (due, asked, timed out), download with progress, cancel, install, the automatic-check choice |
| `Services/UpdateService.cs` | Latest-release check against the GitHub API, installer download and SHA-256 check |

## MDGlint.Core.Tests (xUnit)
| Path | What |
|---|---|
| `AppSettingsTests.cs` | Settings files from older versions, damaged files, round trips |
| `AppVersionTests.cs` | Version display text; built assemblies match `Directory.Build.props` |
| `HtmlContentSanitizerTests.cs` | What the sanitizer keeps and removes |
| `PreviewUnavailableNoticeTests.cs` | The WebView2 notices' texts and link |
| `MarkdownRendererTests.cs` | Rendering, sanitizing, file read failures and messages |
| `RemoteContentPolicyTests.cs` | Remote-content choices and remote-address detection |
| `RenderPlanTests.cs` | New-document and remote-content decisions per render |
| `SemanticVersionTests.cs` | Version parsing and ordering |
| `SettingsFixtureTests.cs` | Every released version's `settings.json` still loads with its values |
| `Fixtures/settings/` | `settings.json` as each release writes it, one file per version, never edited once released |
| `ThemeServiceTests.cs` | Theme parsing and toggling |
| `UpdateCoordinatorTests.cs` | The update bar's flow, with a fake store and server |
| `UpdatePreferenceTests.cs` | Opt-in rules and the once-a-day check |
| `UpdateServiceTests.cs` | Release parsing, check results, download and hash checks (fake HTTP handler) |

## MDGlint.Desktop (net10.0-windows, WPF)
| Path | What |
|---|---|
| `App.xaml(.cs)` | Startup, command-line file argument, unhandled-error dialog and log |
| `MainWindow.xaml(.cs)` | Toolbar (version, Open, Reload, Remote, theme, ⋯ menu with update options), remote-content bar, update bar, WebView2 setup (crash reports kept local) and the notice when it can't start, drag and drop on the toolbar, window placement, theme chrome |
| `AssemblyInfo.cs` | WPF theme info; internals visible to `MDGlint.Desktop.Tests` |
| `Services/PreviewHost.cs` | Applies `PreviewPolicy` to WebView2: shell page, local file responses, remote-content blocking, render/theme messages, files dropped on the preview, navigation and links; SmartScreen off |
| `Services/FileMarkdownReader.cs` | `IMarkdownFileReader` over the file system |
| `Services/AppSettingsStore.cs` | `ISettingsStore` as JSON in `%APPDATA%\MDGlint` |
| `Services/FileWatcherService.cs` | Watches the open file; 250 ms debounced reload on the window's thread |
| `Services/UpdateLauncher.cs` | Setup's update-check choice from the registry, the download folder, running the installer, the HTTP client (and the Debug update-server redirect) |
| `Helpers/AppPaths.cs` | Where settings, WebView2 data, downloads and the error log go (and the Debug data-folder hook) |
| `Helpers/DocumentUrls.cs` | Local path ↔ `https://doc.mdglint.example/C/...` URL mapping |
| `Helpers/ErrorLog.cs` | Saves unhandled-error details; the error dialog's text |
| `Helpers/MarkdownFiles.cs` | `.md`/`.markdown` extension check |
| `Helpers/PreviewPolicy.cs` | The preview's rules: the shell page, which local files load and as what, blocked remote requests, accepted messages, what a clicked link does |
| `Helpers/ProcessLauncher.cs` | Starts every program MDGlint opens (browser, Explorer, setup); the Debug launch log |
| `Helpers/WindowPlacement.cs` | Where the window opens (maximized by default, else its saved bounds fitted to the screen) and what it saves on closing |
| `Assets/index.html` | Preview shell page: CSP, `<base>`, stylesheet links, scripts |
| `Assets/preview.js` | The page's decisions, tested in Node: messages, theme URLs, absolute-path rewriting, scroll anchoring, code highlighting, `#anchor` targets, file drags |
| `Assets/app.js` | Wires `preview.js` to the page: render in place, scroll, theme swap, `#anchor` clicks, dropped files to the app, the ready message |
| `Assets/app.css` | Page layout around the Markdown, colour scheme, code-block fixes |
| `Assets/vendor/github-markdown-css/` | GitHub's Markdown styles, light and dark (MIT, with licence) |
| `Assets/vendor/highlight.js/` | highlight.js common build, extra languages in `languages/`, GitHub themes (BSD-3, with licence) |
| `MDGlint.ico` | App and installer icon |
| `Install/installer.iss` | Inno Setup script (per-user or all-users install, file association, update-check choice, old-file cleanup, reopening after an update, WebView2 check; the version is passed in) |
| `Install/build-installer.ps1` | Reads the version (or takes `-Version`), publishes into a clean folder and checks it (files present, all covered by `[InstallDelete]`, no build paths), and compiles the installer into `Install/Output` |

## MDGlint.Desktop.Tests (xUnit, and Node for the preview script)
| Path | What |
|---|---|
| `DocumentUrlsTests.cs` | Path ↔ URL mapping, refused paths |
| `PreviewPolicyTests.cs` | Shell page, local media types, remote blocking, messages, link actions |
| `HelpersTests.cs` | `MarkdownFiles`, `AppPaths`, `ProcessLauncher`, `ErrorLog`, `FileMarkdownReader` |
| `ServicesTests.cs` | `AppSettingsStore`, `UpdateLauncher` (registry, downloads, starting setup, HTTP client) |
| `FileWatcherServiceTests.cs` | Saves, renames, deletes, debouncing, stopping, the window's thread |
| `WindowPlacementTests.cs` | Default maximized, restoring and fitting saved bounds, what closing saves |
| `ReleaseCompatibilityTests.cs` | `installer.iss`, `release.yml` and the app agree on the names and formats updates rely on |
| `Preview/preview.test.mjs` | `Assets/preview.js`, with Node's built-in test runner |

## MDGlint.EndToEnd.Tests (xUnit, the real app)
| Path | What |
|---|---|
| `AppSession.cs` | Starts the Debug build with the test hooks; UI Automation helpers |
| `DevTools.cs` | DevTools protocol client for the preview page |
| `LocalServer.cs` | HTTP server on 127.0.0.1 standing in for remote hosts and GitHub |
| `PreviewTests.cs` | Scripts blocked, local images and refused files, links, files dropped on the preview, `#` jumps, keeping the reader's place |
| `WindowTests.cs` | Theme, remote content, opening maximized and where it was left, the WebView2-missing notice |
| `UpdateFlowTests.cs` | Opt-in respected, install only on request, hash mismatch, the ⋯ menu |
