# Decision Log — MDGlint

> Maintenance note (for the working agent): this file is an append-only-style
> log. When a new architectural or product decision is made, add an entry here
> in the same change. Don't rewrite history — if a past decision is reversed,
> add a new entry noting the change and reference the old one.

## Product

- **Viewer, not editor.** *(Superseded 2026-11-19, see below.)* MDGlint
  only previews. Users keep their own editor; live reload covers the
  edit-and-look loop.
- **2026-11-19: Grow beyond a viewer, release by release.** After daily use
  on two machines, the viewer-only scope proved too narrow: losing the
  scroll position on reload, no export, no quick inline edits, no diagrams,
  no navigation or tabs. Features are now planned in priority order in
  `roadmap.md`, one small release at a time, so each release has something
  to show. Constraint kept from the original goal: opening a file must stay
  instant.
- **2026-11-22: Background updates (#17) move out of 1.1.** As written, #17
  installs when MDGlint closes, which reverses the rejected option in
  "Update checks are opt-in" (the user should choose when the app closes).
  MDGlint also starts a process per open file, so "when it closes" has no
  clear meaning with several windows open. 1.1 should be a stable release,
  so #17 waits for its own design (`roadmap.md`, Exploring). The issue
  stays open with no milestone.
- **2026-11-22: The app design (#19) moves to 1.2.** 1.1 already brings
  export, print, copy and beta updates; a new look on top would make it too
  big to test in a week. The redesign changes nothing users rely on, so it
  stays a minor release rather than a 2.0. Three directions were mocked up
  and are kept in `docs/design/` for the choice.
- **Open source, MIT.** Published on GitHub so anyone can use it. MIT keeps
  reuse simple and matches the dependencies' permissive licenses.
- **GitHub-inspired look, own stylesheets.** *(Superseded 2026-11-19 by
  "Bundle github-markdown-css" under Stack.)* The themes are small
  project-specific CSS files rather than GitHub's own CSS, to stay small
  and avoid implying affiliation. Exact GitHub fidelity is not a goal.
- **Windows only, x64.** The target audience double-clicks `.md` files in
  Explorer. Mac and Linux stay open for later (see "Keep WebView2; Electron
  stays open" under Packaging and distribution).
- **2026-11-21: The window opens maximized, then as it was left.** It saves
  whether it was maximized and its normal bounds on closing, and fits saved
  bounds to the screens present at start. Reason: a reader wants the room;
  someone who restores it has chosen a size, and a removed monitor must not
  leave the window out of reach.

## Stack

- **WPF + WebView2.** WPF for a light native window; WebView2 because
  rendering HTML and CSS faithfully needs a real browser engine. The
  WebView2 runtime ships with Windows 11 and is not bundled.
- **Markdig** for Markdown, with advanced extensions, for GitHub-flavoured
  syntax coverage in pure .NET.
- **Core/Desktop split.** Rendering, themes and settings live in a
  platform-neutral `MDGlint.Core` behind small interfaces, so they can be
  tested without UI and reused by another front end.
- **No third-party syntax highlighter.** *(Superseded 2026-11-19: real
  highlighting is a 1.0.0 fix, see Security below.)* A tiny inline regex
  highlighter keeps the app small and offline. Accuracy is limited.
- **2026-11-19: Assets from a virtual host, content by messaging.** The
  preview loads one shell page; bundled CSS and JS are served from a
  WebView2 virtual host mapping, and the CSP allows scripts only from that
  host. Rendered Markdown is pushed to the page by WebView2 messaging
  instead of `NavigateToString`. This is what lets inline scripts be
  blocked while still running trusted libraries, and keeps the scroll
  position on reload.
- **2026-11-19: Document folder as a second virtual host.** *(Superseded
  the same day by "Local files answered in code" below.)* The open file's
  folder is mapped to `https://doc.mdglint.example/` (DenyCors: images and
  media load, script can't read files) and the page's `<base>` points
  there. WebView2 only applies a new mapping to pages loaded after it, so
  the startup folder is mapped before the first load and a change of
  folder reloads the shell. Trade-offs accepted: absolute `file:` image
  paths no longer load from the `https` shell, and plain `http:` images
  are subject to Chromium's mixed-content upgrade (this last one still
  applies).
- **2026-11-19: Local links never run anything.** Relative links to
  Markdown files open in MDGlint; links to other local files are shown in
  Explorer (`/select`) rather than launched, so a document can't start a
  program with one click.
- **2026-11-19: Local files answered in code.** Mapping the file's folder
  broke `../` paths, absolute paths and other drives, and needed a page
  reload on every folder change. Instead, `https://doc.mdglint.example/`
  URLs spell out the full path (`/C/...`, `/UNC/server/share/...`) and
  `PreviewHost` answers them through `WebResourceRequested`, serving only
  image, audio and video types (404 otherwise, no CORS headers). `app.js`
  rewrites absolute paths and `file:` URLs to these addresses. Result: no
  reloads, `../` and absolute paths work on any drive or share. Accepted:
  any image or media file on a local drive can be displayed, the same reach
  the original `file:` page had; scripts still can't run or read files.
  Rejected: mapping each drive root (still reloads on drive change, no
  cross-drive paths) and embedding images as `data:` URLs (every save
  re-sends every image; no video). Video seeking needs HTTP range support,
  which is not implemented yet.
- **2026-11-19: New file starts at the top.** Content is swapped in place
  for every render; the page scrolls to the top when a different file is
  shown and keeps its position only when the same file is saved again.
- **2026-11-19: Anchor to the block at the top of the viewport.** On a
  save, `app.js` records the top-level block at the viewport top by the
  heading it follows (id, then text, then position), its index in that
  section and its text, and scrolls the new content so that block keeps its
  offset. If the heading can't be found, the old scroll offset is kept.
  Rejected: keeping only the raw scroll offset (jumps when content above
  changes) and anchoring to the heading alone (drifts in long sections).
  Size changes after the swap, such as images loading, are left to the
  browser's built-in scroll anchoring.
- **2026-11-19: Bundle github-markdown-css and highlight.js.** Replaces the
  AI-generated stylesheets (clear MIT licence, closer GitHub fidelity) and
  the regex highlighter (BSD-3, GitHub light/dark themes). Both are bundled
  in `Assets/`, never loaded from a CDN: previews work offline, a CDN
  request would reveal when a file is opened, and the CSP would block it.
  Cost: updates are manual, checked before each release.
- **2026-11-19: How the libraries are bundled.** Unmodified files from the
  npm packages `github-markdown-css` and `@highlightjs/cdn-assets` live in
  `Assets/vendor/<library>/` with their licence files, so an update is a
  plain file copy and the versions are listed in `THIRD-PARTY-NOTICES.md`.
  highlight.js uses its common build plus PowerShell, batch and Dockerfile
  grammars, which a Windows audience needs. Only code blocks that name a
  known language are highlighted; no automatic language detection, which
  matches GitHub, costs nothing on large blocks and avoids wrong guesses.
  MDGlint's own layout and the fixes needed to combine the two libraries
  are in `Assets/app.css`.
- **2026-11-21: Drops on the preview go through the page.** WebView2 is a
  window of its own, so the WPF window only sees drops over the toolbar. The
  page takes file drops and sends the File objects to the app
  (`postMessageWithAdditionalObjects`), which opens the first Markdown path.
  Reason: only a real drop gives a File with a path, so the page can't make
  the app open a file of its choosing; and no native drop hook is needed.
- **2026-11-22: The no-file page's Open button is the shell's, and it only
  asks for the dialog.** The button lives in `index.html`, not in the
  rendered HTML, and the app honours its `{ type: "open" }` message only while
  the no-file page is shown. The user still picks the file in the system
  dialog, so the page can never choose what opens. Rejected: rendering the
  button as part of the message HTML, which would make it content any file
  could imitate.
- **2026-11-22: Print and PDF go through WebView2, in the light theme by
  default.** Print uses the system dialog (`ShowPrintUI`), and PDF export uses
  `PrintToPdfAsync`. Both are built in, so no library or extra download is
  needed and nothing leaves the PC. They print what the preview shows, so
  images follow the same remote-content choice. Printouts use the light
  theme unless **Print in light theme** is turned off, since dark pages waste
  ink and read poorly on paper. The switch is made with print-only
  stylesheets (`media="print"`), so the screen never changes. Rejected:
  switching the page to light while printing (the dialog gives no reliable
  moment to switch back), and WebView2's own print preview (the issue asks
  for the system dialog). *(The system dialog was superseded 2026-11-22 by
  "Print uses WebView2's own dialog" below.)*
- **2026-11-22: Print uses WebView2's own dialog.** The system dialog says
  the app doesn't support print preview, and after a debug session it left
  the WebView2 browser process running, so later windows couldn't start.
  WebView2's dialog (`CoreWebView2PrintDialogKind.Browser`) previews the
  page, can still open the system dialog, and closes with the window.
- **2026-11-22: A leftover preview process is found and can be ended.** A
  browser process left by a closed window blocks every new window, and
  WebView2 only times out after a long wait. MDGlint recognises it by its
  command line (MDGlint's data folder, no `--type=`) and a parent that is
  gone. It says so at once, and ends it only when the user presses **End it
  and try again**. It never ends a process another open window uses.
  Rejected: Restart Manager, which didn't report the lock file's holder, and
  ending it without asking.
- **2026-11-22: Printouts have MDGlint's footer, not the browser's.** WebView2's
  dialog adds the date, title, the shell page's internal address and the
  page number. Instead, every printed and PDF page has "MDGlint" and
  "page / pages" in its footer, so readers know where it came from and how
  long it is. `@page` margin boxes draw it, and empty ones fill the other
  places, which keeps the browser's text off and the page's margins as set
  in the dialog. Rejected: `@page { margin: 0 }`, which also hides the
  browser's text but prints to the paper's edge.
- **2026-11-22: Copying (#15).** A code block's Copy button copies in the
  page, with `navigator.clipboard.writeText`. The page is a secure context
  and the click is the user's, so no permission or app code is needed. The
  button comes from the shell (`index.html`), and `app.js` acts only on
  buttons it made and only on trusted clicks, so a file can't add or aim
  one. It copies the code as written, highlighting aside. **Copy Markdown**
  (⋯ menu, Ctrl+Shift+C) is done by the app: it reads the shown file from
  disk and puts its text on the clipboard, so the copy is the file itself,
  not the rendered page. Rejected: a message asking the app to copy page
  text, which would let the page put any text on the clipboard through the
  app.
- **2026-11-22: HTML export (#11) is one file that loads nothing.** The
  content is taken from the preview as shown, so the code is highlighted
  as on screen without running highlight.js in the exported file. Styles
  are inlined with their licenses, and images are embedded as `data:` URLs.
  Images follow the Remote content choice, like the preview and PDF: local
  images are included, and remote ones only when allowed for the file.
  Those are downloaded at export time, so the file works offline and when
  sent on. Video and audio are left out rather than linked, and the page's
  Content Security Policy blocks anything else, so opening the file never
  contacts anyone. AngleSharp, already shipped for HtmlSanitizer, does the
  HTML work, so no new library is needed. Rejected: linking remote images,
  which would load them from the internet whenever the file is opened.
- **2026-11-22: Word export (#13) uses the Open XML SDK and starts from
  the preview's HTML.** Microsoft's Open XML SDK (`DocumentFormat.OpenXml`,
  MIT) writes the `.docx`; MDGlint's own `DocxWriter` turns the content into
  Word's paragraphs, lists, tables and footnotes. The content is the same
  sanitized HTML the HTML export takes from the preview, so raw HTML in the
  file has already been cleaned and image addresses are already resolved,
  and the document matches the screen. Images follow the same rules as the
  HTML export and are stored in the document, never linked, since a linked
  image would be fetched each time Word opens the file. Only web and mail
  links stay links, and a `.docx` can't hold macros. The document is always
  light: Word documents are mostly printed or sent on. Code keeps its font
  and shading but not its syntax colours, for now. The SDK adds about 8 MB
  to the app (its assemblies are part of the single-file exe), and .NET
  loads them only when a Word export runs, so opening a file stays as fast.
  Rejected: HtmlToOpenXml (MIT), less code but untrusted HTML handled by a
  smaller third-party project; Pandoc, a separate GPL program and a large
  download; Word automation, which needs Word installed; walking Markdig's
  syntax tree, which would duplicate the preview's image paths and
  sanitizing.
- **2026-11-22: Reopening the file after an update (#16) is the app's
  job.** Before starting setup, MDGlint writes the open file's path to
  `%LOCALAPPDATA%\MDGlint\reopen.txt`; the next start deletes the note and,
  with no file given, opens the path if the note is at most five minutes
  old and the file still exists. Setup doesn't change, so an installer
  already out there keeps working, and only updates started from 1.1 on
  reopen the file. The age limit keeps a note left by an update that never
  finished from opening a file days later. Rejected: passing the file to
  setup for its `[Run]` entry, which needs a setup change and puts a path
  on setup's command line.

## Packaging and distribution

- **Self-contained single-file publish.** Users don't need to install .NET.
  Reaffirmed 2026-11-19 after using it on two machines: it avoids .NET
  runtime problems and stays the default. It does not cover the WebView2
  runtime; see the WebView2 entries below.
- **2026-11-19: WebView2 via the Evergreen bootstrapper.** *(Superseded
  2026-11-19 by "Don't ship the WebView2 bootstrapper" below.)* The installer
  bundles Microsoft's Evergreen bootstrapper (about 2 MB) and runs it
  silently only when the runtime is missing; Windows then keeps WebView2
  updated. Rejected: Fixed Version runtime (no dependency, but a few
  hundred MB and never self-updates — every Chromium security fix would
  need an MDGlint release, too risky for an app that renders untrusted
  files); bundling the standalone Evergreen installer (offline, but well
  over 100 MB added for every user, most of whom already have it).
- **2026-11-19: Don't ship the WebView2 bootstrapper.** Setup neither
  bundles nor downloads it. Shipping it means redistributing Microsoft code
  under the WebView2 Runtime licence, which requires end users to accept
  terms protecting Microsoft and the distributor to indemnify Microsoft;
  that conflicts with the permissive-licence rule and is too much for a
  small open-source project. The runtime ships with Windows 11 and nearly
  every Windows 10 PC, and Windows keeps it updated. When it is missing,
  setup says so (registry check, Microsoft's documented method) and the app
  shows a notice in place of the preview, both linking to Microsoft's
  download page, which is one of the two approaches Microsoft's
  distribution guide recommends. Using WebView2 is unchanged: the Evergreen
  runtime stays, and the Fixed Version stays rejected. Rejected: bundling
  the bootstrapper, and downloading it during setup (no redistribution, but
  the setup needs a download, a signature check and error handling for the
  few PCs without the runtime).
- **2026-11-19: Keep WebView2; Electron stays open for Mac and Linux.**
  Considered dropping WebView2 over its licence. Rejected for 1.0.0:
  Electron and CefSharp bundle their own Chromium (100 MB or more, slower
  start, and every Chromium security fix would need an MDGlint release and
  an updater); native WPF rendering loses HTML, CSS and the GitHub look;
  Tauri uses WebView2 on Windows anyway. Electron is kept as an option if
  Mac and Linux support becomes a goal (`roadmap.md`, Exploring).
- **2026-11-19: Releases built by GitHub Actions.** Every PR is built; a
  `v*` tag builds the app and installer on a clean runner, hashes it, and
  publishes the GitHub Release (as a draft, see the next entry) with notes
  from `CHANGELOG.md`. Choosing the version, the changelog PR, pushing the
  tag and showcase images stay manual. Rejected: release-please, because
  it generates the changelog from commits and would replace the
  hand-written Keep a Changelog style.
- **2026-11-19: How the release workflow works.** It creates a **draft**
  release rather than publishing it, so the maintainer smoke-tests the
  installer, adds the showcase images and publishes by hand; nothing goes
  public from a tag alone. It refuses a tag that doesn't match
  `<Version>` or has no changelog section. Besides the installer it
  attaches `MDGlintSetup.exe.sha256`, so the hash can be read without
  parsing the notes (the update check, roadmap item 10, can use it).
  Actions are pinned to commit SHAs, not tags, so a moved tag can't change
  what runs with write access. Inno Setup is installed with Chocolatey only
  if the runner doesn't have it.
- **2026-11-21: Releases need approval and are attested.** The release job
  runs in a `release` environment whose required reviewer is the
  maintainer and which only `v*` tags may use, so a pushed tag, mistaken or
  from a misused account, builds nothing until approved. The installer gets
  a signed build-provenance attestation (`actions/attest-build-provenance`)
  that anyone can verify with `gh attestation verify`, tying it to the
  workflow run and commit. A tag ruleset lets only an admin create `v*`
  tags and blocks moving or deleting them, and releases are immutable once
  published. Reason: every copy of MDGlint installs what this repository
  publishes, and until code signing these are what stand between the
  account and users. On the free plan, environments and attestations need a
  public repository, so the first release is tagged after the repository
  goes public. The app still checks only the SHA-256; checking the
  attestation in the app would need Sigstore verification in .NET and is
  left for later.
- **2026-11-21: Dependabot for NuGet and Actions.** Weekly version-update
  PRs for the NuGet packages and the pinned actions (minor and patch
  grouped), each going through CI and review. The vendored front-end
  libraries stay a manual check before each release.
- **2026-11-19: No local paths in binaries.** .NET embeds the debug-symbol
  path in builds, which for a local build includes the user name. Release
  builds come from CI, and `ContinuousIntegrationBuild` with a path map is
  set so no build carries a local path. 2026-11-19: the path map is set in
  `Directory.Build.props` for every Release build, local or CI (Debug
  builds keep real paths for the debugger and never ship), and
  `build-installer.ps1` fails if any shipped exe or DLL still contains the
  build folder's path.
- **2026-11-19: One version, in `Directory.Build.props`.** `<Version>` is
  set there and nowhere else: the assemblies, the toolbar and the installer
  (passed to ISCC by `build-installer.ps1`) all read it, so a release
  changes one line. `installer.iss` refuses to build without it rather
  than fall back to a stale number. The toolbar drops the `+commit` build
  metadata the SDK adds. Rejected: `<Version>` in the Desktop csproj (Core
  would be left unversioned) and a git-tag-derived version (needs a tool or
  script, and local builds without tags would show a wrong number).
- **Inno Setup installer**, distributed through GitHub Releases.
  2026-11-19: it runs in 64-bit mode and only on x64 Windows, matching the
  x64-only app (`ArchitecturesAllowed` and
  `ArchitecturesInstallIn64BitMode` set to `x64compatible`); it needs
  Inno Setup 6.3 or later.
- **File association is opt-in** (unchecked by default during setup) and
  written per user under `HKCU`; the app is always registered for
  "Open with". 2026-11-19: written where the install goes instead (`HKCU`
  per user, `HKLM` for all users), see the next entry. 2026-11-19: the
  task covers `.md` and `.markdown` (one `MDGlint.md` ProgID for both,
  and the task keeps its `associate_md` name so upgrades remember the
  choice). The ProgID and `OpenWithProgids` entries are always written so
  Windows offers MDGlint when it asks which app to use; the task only sets
  the default. Windows 10 and 11 don't let setup take the default silently,
  so the user may confirm it once per extension.
- **2026-11-19: Install per user by default.** Setup installs for the
  current user without an administrator prompt
  (`PrivilegesRequired=lowest`), and offers all users (Program Files) as
  an option. Registry entries use the `HKA` root, so they follow that
  choice. Reason: an administrator install that wrote to `HKCU` put the
  entries in the wrong profile whenever a standard user approved the prompt
  with another account (Inno Setup's `UsedUserAreasWarning`). Rejected:
  administrator only with `HKLM` entries (always a prompt, and the
  association changes for every user of the PC).
- **No telemetry, no auto-update, no network calls of its own.** Neither
  setup nor the app downloads anything; remote images load only when the
  user allows them. (The WebView2 Runtime's own data collection is under
  Privacy.) 2026-11-19: the "no auto-update" part is superseded by
  opt-in update checks (see the next entries); there is still no
  telemetry, and with update checks off the app makes no requests of its
  own.
- **2026-11-19: Update checks are opt-in, installs are never automatic.**
  Setup has a **Check for updates once a day** task, unchecked by default,
  next to the association one; it writes `CheckForUpdates` (1 or 0) under
  `HKA\Software\MDGlint`. The app's **⋯** menu turns checks on or off
  (saved in `settings.json`, which then wins over setup's choice) and has
  **Check for updates now**, which works with checks off. With neither
  choice made, MDGlint never checks. When on, it asks the GitHub API for
  the latest release at most once a day, at startup or when turned on. A
  newer release shows a bar; **Install** downloads `MDGlintSetup.exe`, refuses it unless its
  SHA-256 matches the release's `MDGlintSetup.exe.sha256`, runs it with
  `/SILENT /UPDATE=1` and closes; setup opens MDGlint again. Off by
  default because each check shows GitHub the user's IP address. Rejected:
  installing in the background (the user should choose when the app
  closes) and notify-only (users would have to download and run setup by
  hand). The hash proves the file is the one the release published, not
  who published it; code signing (issue #36) would add that. *(Extended
  2026-11-22 by "Beta versions are opt-in" under Packaging and
  distribution.)*
- **2026-11-19: Uninstall asks whether to keep settings.** As most apps
  do, the uninstaller asks whether to keep the settings (Yes is the
  default); **No** removes `%APPDATA%\MDGlint` and
  `%LOCALAPPDATA%\MDGlint` (settings, WebView2 cache, error log, any
  downloaded update) for the user running it. A silent uninstall keeps
  them, so scripted removals never delete data unasked. Other users'
  profiles are never touched, so after an all-users install their settings
  stay.
- **2026-11-19: Checks at most once a day, not every start.** MDGlint
  starts a new process for each file opened, so a check on every start
  could mean dozens of requests a day, each showing GitHub the user's IP
  address and counting against GitHub's 60-per-hour limit for
  unauthenticated requests, which users behind one network share. Releases
  are weeks apart, so a daily check finds them just as soon in practice;
  **Check for updates now** covers the rest.
- **2026-11-19: How the update check decides.** It reads GitHub's
  `releases/latest`, which never returns drafts or pre-releases; a tag that
  isn't a stable version, or a page or asset outside this repository's
  release URLs, is ignored. It offers the latest release whatever the
  running version, so a user several versions behind goes straight to the
  newest (each installer holds the whole app, and setup installs over any
  older version). The downloaded installer is held open with only read
  sharing from its hash check until setup starts, so nothing else can swap
  it in between. The only thing requests say about the user's copy is
  the user agent, `MDGlint/<version>`. Debug builds can redirect update requests to a local server
  (`MDGLINT_DEBUG_UPDATE_SERVER`) for testing; Release builds don't
  contain that code. *(Extended 2026-11-22 by "Beta versions are opt-in"
  below.)*
- **2026-11-22: Beta versions are opt-in (#48).** **Get beta versions** in
  the **⋯** menu, off by default and for older settings files, lets testers
  get pre-releases. With it on, the check reads the ten newest releases
  (`releases?per_page=10`, since `releases/latest` skips pre-releases) and
  offers the highest version among them. Under Semantic Versioning a
  stable release outranks its own betas (`1.2.0` over `1.2.0-beta.2`), so
  a beta is offered only while it is for a newer version than the latest
  stable release. Ten is enough
  because releases come newest first and a beta is always of a newer
  version than the stable releases before it. Betas get the same rules:
  drafts and anything outside this repository are ignored, the SHA-256
  must match, and nothing installs until **Install**. Only a newer version
  is ever offered, so turning it off while on a beta waits for the next
  stable version rather than downgrading. It is part of the update check,
  so it does nothing unless checks are on or the user asks. Copies before
  1.1 can't see betas. Rejected: a separate beta channel or feed, which
  would need more release tooling for the same result.
- **2026-11-19: Updates keep working.** Every released version can install
  the latest release, so five rules hold for every release
  (`releasing.md`, "Keeping updates working"): never change the `AppId`;
  every version reads any older `settings.json` (settings are added, never
  renamed or removed without migration); setup's `[InstallDelete]` clears
  files older versions left; the release format (`vx.y.z` tags,
  `MDGlintSetup.exe` and `MDGlintSetup.exe.sha256`) never changes; and a
  release older versions shouldn't install themselves is published without
  the `.sha256` asset, so they offer only its page. Tests guarding these
  are part of roadmap item 11.
- **2026-11-20: Windows 10 or later.** MDGlint supports Windows 10
  version 1607 or later and Windows 11, x64 only, because .NET 10 and the
  WebView2 Runtime don't run on anything older (WebView2 dropped Windows
  7, 8 and 8.1 in 2023). Setup sets `MinVersion=10.0.14393`, so on older
  Windows it refuses to install instead of installing an app that can't
  start. Raising the minimum later is a release older versions shouldn't
  install themselves (`releasing.md`, rule 5).
- **2026-11-21: Named MDGlint.** The working name, MDGlance, was already
  used by several other Markdown viewers on GitHub, one of them for
  Windows. MDGlint keeps the same feel and initials and was free on
  GitHub, winget, Chocolatey and the common package registries. Renamed
  before the first release, because the repository name, the installer's
  asset names and the data folders can't change once a release is out
  (see "Updates keep working"); the installer's `AppId` is unchanged.

## Provenance

- **2026-11-19: Theme stylesheets are AI-generated.** `github-light.css`
  and `github-dark.css` were generated with an AI assistant rather than
  taken from GitHub's stylesheets, and are shipped under MDGlint's MIT
  License. Their exact origin can't be verified line by line, so
  `THIRD-PARTY-NOTICES.md` says so plainly. If a passage is ever found to match a third-party stylesheet,
  replace it or switch to a licensed source (for example
  `github-markdown-css`, MIT) with attribution. *(Resolved by the decision
  to bundle `github-markdown-css` in 1.0.0, see Stack; the AI-generated
  files were removed then.)*

## Privacy

- **2026-11-19: No personal information in the public repo.** Standing
  rule in `AGENTS.md`: no personal emails, names beyond the GitHub handle,
  user paths, or other identifying data in files, commits or GitHub posts;
  commits use the GitHub noreply email.
- **2026-11-21: The owner's public name.** The copyright holder in
  `LICENSE` and the commit author name are `Jawad Chughtai`, the owner's
  real name, which is the GitHub handle `Jawad-Chughtai` without the
  hyphen and so reveals nothing the handle doesn't. A licence's copyright
  line is clearer with a person's name than a handle. No other personal
  details are allowed (`AGENTS.md`).
- **2026-11-20: The repository is fixed.** MDGlint is published as
  `Jawad-Chughtai/MDGlint`, the maintainer's own account. Every released
  copy checks this repository for updates, compared exactly
  (`UpdateSource.Repository`), so neither the owner nor the name can change
  afterwards. The owning account must use two-factor authentication before
  any release.
- **2026-11-21: SmartScreen and crash reports off; WebView2's diagnostic data stated.** The
  preview sets `IsReputationCheckingRequired = false` before its first
  navigation. Reason: SmartScreen sends page addresses to Microsoft, and the
  preview only shows MDGlint's own page (links open outside it), so it
  protects nothing here. WebView2's required diagnostic data can't be turned
  off by an app (Windows' Diagnostic data setting governs the rest), so
  `README.md` and `SECURITY.md` say so rather than claim no data leaves the
  PC. WebView2's crash reports are kept local too
  (`IsCustomCrashReportingEnabled`): MDGlint doesn't collect them, and
  Windows no longer sends them to Microsoft.

## Issue tracking

- **2026-11-19: GitHub Issues, not Jira.** MDGlint is open source: users
  report problems where the code is, the issue forms are already set up,
  milestones map onto roadmap releases, `Fixes #n` links PRs to issues, and
  a public tracker shows the work for the portfolio. Jira would be invisible to users or need public
  access, and two trackers would drift. See `issue-tracking.md`.

## Security

- **2026-11-19: Known rendering gaps documented, fix deferred.** Raw HTML
  and inline scripts in Markdown run, and remote images load. Decided to
  publish the gap in `SECURITY.md` and fix it later rather than block the
  docs work. Tracked in the backlog, since retired.
- **2026-11-19: Security and necessary fixes ship in 1.0.0.** Supersedes
  the deferral above. Blocking scripts and raw HTML, opt-in remote images,
  keeping the scroll position on reload, real syntax highlighting,
  `.markdown` association, the WebView2 check and a single version source
  are all required before the first public release, not a later one.
  Feature releases start at 1.1 (`roadmap.md`).
- **2026-11-19: Sanitize raw HTML, don't drop it.** Raw HTML in Markdown
  passes through an HTML sanitizer (for example HtmlSanitizer, MIT) that
  keeps safe tags (`<details>`, `<br>`, `<img width>`, `<kbd>`) and removes
  scripts, event handlers and other active content. Rejected: Markdig's
  `DisableHtml()`, which would break many real READMEs. The CSP still
  blocks inline script as a second layer.
- **2026-11-19: Sanitizer allow-list.** HtmlSanitizer's defaults, adjusted
  for Markdown: `id` and `class` are kept (heading anchors, footnotes, code
  languages, task lists); `<video>`, `<audio>`, `<source>` and `<picture>`
  are added; form controls and form attributes are removed, except task
  list checkboxes, which are forced to `disabled`; `target` and
  `accesskey` are removed; `mailto:` and `file:` links are allowed, drive
  paths such as `C:\x.png` are kept for `app.js` to rewrite, and `data:`
  URLs only as image sources. Inline `style` stays (filtered to safe CSS
  properties); remote URLs in it are left to the remote-content switch.
  `name` is removed from `<img>` and similar elements so a file can't
  overwrite `document` properties the page script uses.
- **2026-11-19: Remote images blocked by default, with "always allow".**
  A bar offers loading them for the current file or always; "always allow"
  is a saved setting the user can turn off again. Default stays blocked so
  no file can track the user without their choice.
- **2026-11-19: One switch for all remote content.** The remote-images
  setting also covers remote video and audio and remote URLs in inline
  styles (`background-image`), so every way a file can reach the network
  follows the same choice. Before this, remote video and audio were always
  blocked while remote images always loaded.
- **2026-11-19: Check for updates in 1.0.0.** The app will check GitHub
  Releases for a newer version. Whether it is on by default and whether it
  only notifies or also installs are still open (`roadmap.md`, item 10).
  When built, this supersedes the "no auto-update" part of "No telemetry,
  no auto-update, no network calls of its own". 2026-11-19: built; opt-in,
  and it installs only when the user presses **Install** (see "Update
  checks are opt-in" under Packaging and distribution).
- **2026-11-19: How remote content is blocked.** In code, not the CSP: the
  CSP allows `http:`/`https:` images and media, and `PreviewHost` sees
  every image and media request (`WebResourceRequested`) and answers 403
  unless remote content is allowed for the shown file. One place decides
  for `<img>`, `<picture>`, video, audio, posters and inline-style URLs,
  and allowing takes effect without reloading the page. The bar is native
  WPF so page content can't imitate or click it. "Load for this file" lasts
  until MDGlint closes; "Always allow" is saved and toggled by a toolbar
  button. Turning it off clears per-file choices and reloads the shell,
  because a page shows images it already loaded from memory without a new
  request. Rejected: switching between two CSPs (needs a page reload for
  every change and can't be decided per request).

## Testing

- **2026-11-19: Full test coverage before the 1.0.0 release.** Everything
  that ships is covered before the first public release, and from then on
  CI fails if coverage drops. Unit tests (xUnit, coverlet) cover
  `MDGlint.Core` and the desktop logic at 100% line and branch; logic is
  moved out of WPF and WebView2 event handlers into plain classes so it can
  be tested. `app.js` logic uses Node's built-in test runner (no npm
  dependencies). End-to-end tests drive the real app over the DevTools
  protocol and UI Automation, and cover the window and WebView2 glue, the
  only code excluded from the unit number, each exclusion marked with its
  reason. Coverage is done as one step after the 1.0.0 fixes and features
  (roadmap item 11), before the docs update and smoke test; the sanitizer
  still gets its tests in item 2. Reason: the app renders untrusted files,
  and the security behaviour (CSP, sanitizer, file-request and link policy)
  must be proven, not just tried by hand.
- **2026-11-20: Debug-only test hooks.** The end-to-end tests run the Debug
  build, which reads four environment variables: one moves settings and
  WebView2 data into a test folder, one writes programs MDGlint would start
  (browser, Explorer, setup) to a log instead, one sends update requests
  to a server on 127.0.0.1, and one passes browser arguments (the DevTools
  port) to WebView2 through its API. *(The fourth was added 2026-11-21:
  WebView2 ignores `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` when the app runs
  elevated, as it does on CI runners.)* The code that reads them is compiled only into
  Debug builds (`#if DEBUG`), so an installed MDGlint can't be redirected by
  its environment. Reason: the tests must never touch the user's settings,
  open browser windows or run setup, and a hook in a shipped build would be
  a way to point the app at another update server.
- **2026-11-20: The page script is split for testing.** `preview.js` holds the
  page's decisions as functions of plain values and element-like objects;
  `app.js` only wires them to the page. Node tests `preview.js` with small
  stand-ins for elements, and the end-to-end tests run `app.js` in WebView2.
  Reason: Node has no DOM, and a DOM library from npm would be the project's
  first JavaScript dependency.
- **2026-11-20: Coverage is checked per assembly.** CI fails unless
  `MDGlint.Core` is fully covered by `MDGlint.Core.Tests`, `MDGlint.Desktop`
  by `MDGlint.Desktop.Tests`, and `preview.js` by its Node tests
  (`.github/scripts/Test-Coverage.ps1`, reading coverlet's and Node's
  reports). No report-merging tool is added. Reason: each project's tests
  should prove its own code, and a combined number could hide a gap.
- **2026-11-20: Installer tests run on CI only.** Installing, updating and
  uninstalling use the real `AppId` and replace whatever MDGlint the machine
  has, so the script refuses to run outside GitHub Actions unless told to.
  The "older versions" it updates from are today's code built with a lower
  version, since no older release exists yet; once releases exist they may
  be added from their published installers. Reason: a contributor running
  the tests must not lose their own install or settings.
