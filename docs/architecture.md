# Architecture

MDGlint is two projects, plus tests:

- **MDGlint.Core** (`net10.0`): turns Markdown into an HTML fragment. It knows nothing about
  Windows, WPF or WebView2; anything it needs from the platform comes through the interfaces in
  `Contracts/`.
- **MDGlint.Desktop** (`net10.0-windows`, WPF): the window, the WebView2 control that displays the
  page, and the Windows implementations of Core's interfaces.
- **MDGlint.Core.Tests**, **MDGlint.Desktop.Tests** (xUnit, plus Node tests for the preview
  script) and **MDGlint.EndToEnd.Tests** (the real app, driven over DevTools and UI
  Automation). `testing.md` describes them.

The desktop app keeps its decisions out of WPF and WebView2 event handlers: `PreviewPolicy`
(which page, files, remote requests, messages and links are allowed), `RenderPlan`,
`UpdateCoordinator`, `WindowPlacement`, `AppPaths` and `ProcessLauncher` are plain classes with unit tests, and `MainWindow`, `App` and
`PreviewHost` only wire them to the window. The same goes for the page: `preview.js` decides and
`app.js` applies.

The split keeps the rendering logic testable without a UI and leaves room for another front end
later (see `decisions.md`).

## From file to screen

1. **Launch.** `App.OnStartup` takes the first command-line argument as the file path. Windows
   passes it when a `.md` file is double-clicked (`"MDGlint.Desktop.exe" "%1"` in the registry).
2. **WebView2 starts.** `MainWindow_Loaded` creates a WebView2 environment with its user data in
   `%LOCALAPPDATA%\MDGlint\WebView2`, with custom crash reporting on so WebView2's crash reports
   stay in that folder instead of going to Microsoft. If that fails, the window shows a notice in place of the
   preview and stops: one for a missing runtime (`WebView2RuntimeNotFoundException`) and one for
   any other start failure, with the error. Both link to Microsoft's WebView2 download page
   (texts in `PreviewUnavailableNotice`).
   While it starts, the window shows "Starting the preview…". All MDGlint windows share one
   WebView2 browser process, since they use the same data folder. A closed window can leave that
   process running: a `msedgewebview2.exe` with no `--type=`, MDGlint's `--user-data-dir`, and a
   parent that is gone (`PreviewProcess`, `LeftoverPreview`). A new window then can't start its
   own, and WebView2 only times out (`0x800705B4`) after a long wait. So when no other MDGlint
   window is open and such a process exists, the window says so at once; a timeout says which
   case it is. **End it and try again** ends only that process and its children, then starts the
   preview in a fresh control.
3. **Shell page.** `PreviewHost.Attach` turns off what the preview doesn't need (host objects,
   autofill, password saving, DevTools in Release builds, and SmartScreen, which would send page
   addresses to Microsoft), sets up two hosts and loads the shell page once:
   - `https://app.mdglint.example/` is a WebView2 virtual host mapped to the `Assets/` folder
     next to the exe. It serves `index.html` (the shell), `preview.js`, `app.js`, `app.css` and
     the bundled libraries in `vendor/` (github-markdown-css and highlight.js).
   - `https://doc.mdglint.example/` serves local files. Its URL path spells out the full path
     (`C:\docs\a.png` → `/C/docs/a.png`, `\\server\share\a.png` → `/UNC/server/share/a.png`;
     see `DocumentUrls`). `PreviewHost` answers these requests itself through
     `WebResourceRequested`, returning only image, audio and video files and a 404 for anything
     else (`PreviewPolicy.TryGetLocalMedia`). No folder is mapped, so opening a file anywhere never reloads the page, and `../`
     paths work up to the drive or share root.

   The shell's Content Security Policy allows scripts only from the app host, so no script in a
   Markdown file runs (inline `<script>` or event-handler attributes). When `app.js` has loaded
   it posts `{ type: "ready" }`, and the window renders the current file.
4. **Watch.** `OpenMarkdownFile` points `FileWatcherService` at the file. Changes, renames and
   deletes restart a 250 ms timer; when it fires the file is rendered again, on the window's
   thread. `RenderPlan` decides whether it counts as a new document (scroll to the top) and
   whether remote content is allowed for it.
5. **Render.** `MarkdownRenderer.RenderFileAsync` reads the file and converts it with a Markdig
   pipeline (advanced extensions, autolinks, pipe tables, task lists, footnotes) into an HTML
   fragment, then `HtmlContentSanitizer` cleans any raw HTML from the file (an allow-list of
   passive elements and attributes, built on HtmlSanitizer) and the result becomes a
   `RenderedDocument`. Missing files, bad paths and other failures produce a short
   message fragment instead of throwing.
6. **Display.** `PreviewHost.Show` posts `{ type: "render", html, base, theme, resetScroll }` to
   the page. `app.js` (with the decisions in `preview.js`) sets the `<base>` to the file's
   folder on the document host, parses the
   HTML into an inert template, rewrites absolute local paths (`C:\x.png`, `C:/x.png`,
   `\\server\share\x.png`, `file:///C:/x.png`) to document-host URLs, swaps the content in (no
   page reload) and highlights code blocks that name a language highlight.js knows (others stay
   plain, as on GitHub). `resetScroll` is true when a different file is shown, so it starts at
   the top. When the same file is saved, `app.js` records the block at the top of the viewport
   (the heading it follows, its place in that section, its text and offset) and scrolls the new
   content so that block sits in the same spot, even if content above it changed. Theme changes
   post `{ type: "theme" }` and only swap the stylesheets.
7. **Links.** The shell is the only page the preview may show; `PreviewHost` cancels every other
   navigation. If the user clicked it (`PreviewPolicy.ForLink` decides; programs are started
   through `ProcessLauncher`):
   - `#heading` links scroll within the page (handled in `app.js`);
   - `http`, `https` and `mailto` links open in the default app;
   - links to local `.md`/`.markdown` files (relative or absolute) open in MDGlint;
   - links to any other local file are shown in Explorer, never run.

   Navigations the content starts on its own are ignored.
8. **Drag and drop.** WebView2 is a window of its own, so the WPF window's `Drop` handler only
   sees drops over the toolbar. Over the preview, `app.js` takes file drops (`isFileDrag` in
   `preview.js`) and posts `{ type: "drop" }` with the dropped File objects
   (`postMessageWithAdditionalObjects`); `PreviewHost` reads their paths and opens the first
   Markdown file (`PreviewPolicy.DroppedMarkdownFile`, which the toolbar drop uses too). Only a
   real drop carries a path, so a page can't name a file for the app to open.
9. **Open on the no-file page.** With no file open, `MarkdownRenderer` marks its message
   `OffersOpen`, and the render message says so. `app.js` then adds the shell's own prompt (a
   `<template>` in `index.html`: an **Open…** button and a line about dropping a file) below the
   message. Clicking it posts `{ type: "open" }`. `PreviewHost` raises `OpenRequested` only
   while the no-file page is shown, and the window opens the same dialog as the toolbar's
   **Open**. The page can only ask for the dialog: the user picks the file. The sanitizer
   removes buttons from files, so a file can't add one of its own.

## Themes

`ThemeService` holds the current `ThemeKind`. The toolbar button toggles it, saves it through
`AppSettingsStore`, recolours the WPF toolbar and title bar (`DwmSetWindowAttribute` with the
immersive dark mode attribute), sets the WebView2 background colour to match, and tells the page
to switch stylesheets: the github-markdown-css and highlight.js theme for the new colour.

## Printing and PDF export

**Print…** (⋯ menu or Ctrl+P) opens WebView2's print dialog, with a preview
(`ShowPrintUI(CoreWebView2PrintDialogKind.Browser)`). The system dialog can't preview a WebView2
page, and it could leave the browser process running after the window closed. The page title is
the file's name (the render message's `title`), so a PDF is titled with it. Every page has
MDGlint's own footer, from `@page` margin boxes in `app.css`: "MDGlint" bottom left and
"page / pages" bottom right. The other four places hold empty boxes, so the dialog's own header
and footer stay off the page: Chromium leaves out its text wherever a page fills one. **Export ▸ PDF…**
saves the file with WebView2's `PrintToPdfAsync` beside the open file by default
(`ExportFiles`), with backgrounds and no browser header or footer. Both print the page as it is
shown, so a PDF has the same images, and the same remote-content choice, as the preview. Both
are offered only while a file is shown.

With **Print in light theme** on (`PrintInLightTheme`, the default) and the dark theme showing,
render and theme messages carry `printLight`. `app.js` then limits the dark stylesheets to
`screen` and turns on the light ones in `index.html` for `print` (`printPlan` in
`preview.js`). `app.css` undoes its own dark rules under `@media print`. The screen never
changes, and nothing has to switch back after printing.

## HTML export

**Export ▸ HTML…** saves one page that needs nothing else, in the current theme:

1. `PreviewHost.ExportContentAsync` runs the shell's `mdglintExport()` (`app.js`). This returns the
   shown content as it reads on screen, highlighting included, without the Copy buttons or the
   no-file prompt, and with every `src` made absolute.
2. `HtmlExporter` (Core) parses it with AngleSharp. It leaves out video, audio and `<source>`, and
   drops `srcset`. It turns each image's address into a `data:` URL through `ExportImages`, or
   leaves the image out when that fails. It writes a page with a Content Security Policy
   allowing only inline styles and `data:` images, the file's name as its title, and
   `data-theme` for the theme.
3. `ExportImages` follows the preview's rules. A document-host address is read from disk only if
   `PreviewPolicy.TryGetLocalMedia` would serve it as an image. A remote image is downloaded
   only when remote content is allowed for the file. Anything over 25 MB, or not an image, is
   left out.
4. `ExportStyles` inlines the theme's github-markdown-css and highlight.js stylesheets, each after
   its license, then `export.css` (the page layout from `app.css`).

## Copying

After each render, `app.js` wraps every `<pre>` in a `div.code-block` with a Copy button
cloned from the shell's `#mdglint-copy-button` template. A trusted click on one of those
buttons copies `codeText` (`preview.js`): the block's text without its closing newline. The
copy goes through `navigator.clipboard.writeText`, and the button shows a tick, or "Couldn't
copy", for two seconds. The buttons are hidden when printing.

**Copy Markdown** (⋯ menu or Ctrl+Shift+C, offered only while a file is shown) reads the shown
file from disk and puts its text on the clipboard with WPF's `Clipboard.SetText`. If the file
can't be read or the clipboard is busy, it shows an error.

## Remote content

The CSP lets images and media come from any `http:`/`https:` address, and `PreviewHost` decides:
it filters every image and media request through `WebResourceRequested`, lets remote ones through
only when `AllowRemoteContent` is set for the document being shown, and otherwise answers 403
and raises `RemoteContentBlocked` once per render. `MainWindow` then shows a bar under the toolbar
("Load for this file", "Always allow"). `RemoteContentPolicy` (Core) holds the choices: per-file
for the session, or always (saved as `AllowRemoteContent` in settings, toggled by the Remote
toolbar button). Turning it off forgets per-file choices and reloads the shell page, because the
page would otherwise show images it already loaded from memory.

## Updates

Off unless the user turns them on. `UpdatePreference` (Core) decides: the in-app choice
(`CheckForUpdates` in settings, set from the **⋯** menu) wins, then setup's choice
(`CheckForUpdates` under `Software\MDGlint` in `HKCU`, then `HKLM`, read by `UpdateLauncher`),
and with neither MDGlint never checks. When checks are on and the last one
(`LastUpdateCheck`) was a day or more ago, MDGlint checks once the window has loaded; **Check
for updates now** checks whatever the setting. `UpdateCoordinator` (Core) runs the flow and
says what the update bar shows (checking, the result, download progress, errors, **Install**,
**Cancel** or **Try again**), with a 30-second limit on a check; `MainWindow` only shows that
state and forwards the clicks.

`UpdateService` (Core, given an `HttpClient`) asks `api.github.com` for the repository's latest
release and compares its tag with the running version (`SemanticVersion`). It ignores drafts,
pre-releases and anything not under this repository's release URLs. With **Get beta versions**
on (`GetBetaVersions` in settings, off by default), it reads the ten newest releases instead
(`releases?per_page=10`) and takes the highest version among them, pre-releases included;
turning it on checks at once when checks are on, and turning it off hides a beta the bar
offers. Either way only a newer version is offered, so nothing is downgraded. It offers the installer
only when the release has both `MDGlintSetup.exe` and `MDGlintSetup.exe.sha256` (names in
`UpdateSource`); otherwise the bar only links to the release page. **Install** downloads the
`.sha256` file and then the installer into `%LOCALAPPDATA%\MDGlint\Updates\`, checking the size
against the release, and hashes the file while holding it open with read-only sharing; a
mismatch deletes it. `UpdateLauncher.StartInstaller` runs the still-locked file with
`/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /UPDATE=1`, and
the app shuts down. Setup's `[Run]` entry opens MDGlint again when `/UPDATE=1` is passed, as
the user who started setup. The next start deletes the `Updates` folder.

Setup opens MDGlint with no file, so just before it starts setup `ReopenNote` writes the open
file's path to `%LOCALAPPDATA%\MDGlint\reopen.txt` (nothing when no file is open), and removes
it again if setup didn't start. Every start takes the note (`App.OnStartup`): it deletes it,
and a start with no file opens the path it names if the note is at most five minutes old and
the file is still there. Setup itself doesn't change.

## Settings and the window

Settings are read and written by `AppSettingsSerializer` (Core), which gives defaults for a
missing or damaged file and for settings an older file doesn't have, so a file from any earlier
version still loads. `AppSettingsStore` saves by writing `settings.json.tmp` and swapping it in, so
a reader never sees half a file. While another program has the file open (an antivirus scan, a
sync tool) it retries for about half a second, and if it still can't save, the change lasts for
this session only; a save never throws.

The window opens maximized until the user restores it. On closing it saves whether it was
maximized and its normal size and position (`WindowPlacement`), and the next start restores
them, moved back on screen if they no longer fit the monitors there are now. They are
`WindowMaximized`, `WindowLeft`, `WindowTop`, `WindowWidth` and `WindowHeight` in settings, in
device-independent pixels; closing while minimized keeps the previous maximized choice.

## Runtime data

| What | Location | Written by |
|---|---|---|
| Settings | `%APPDATA%\MDGlint\settings.json` | `AppSettingsStore` |
| Setup's update-check choice | `Software\MDGlint\CheckForUpdates` in `HKCU` or `HKLM` | Setup |
| A downloaded update | `%LOCALAPPDATA%\MDGlint\Updates\` (deleted at the next start) | `UpdateService` |
| The file to reopen after an update | `%LOCALAPPDATA%\MDGlint\reopen.txt` (deleted at the next start) | `ReopenNote` |
| WebView2 profile, cache and crash reports | `%LOCALAPPDATA%\MDGlint\WebView2\` | WebView2 |
| Unhandled error details | `%LOCALAPPDATA%\MDGlint\startup-error.txt` | `App.OnDispatcherUnhandledException` (`ErrorLog`) |

The paths come from `AppPaths`. Debug builds can move them, and log programs instead of starting
them, for the end-to-end tests (`testing.md`, "Test hooks"); Release builds don't contain that
code.

The uninstaller always removes setup's registry value. It then asks whether to keep the
settings (`CurUninstallStepChanged` in `installer.iss`); **No** deletes `%APPDATA%\MDGlint` and
`%LOCALAPPDATA%\MDGlint` for the user running it. A silent uninstall keeps them.

## Packaging

`MDGlint.Desktop.csproj` publishes a self-contained, single-file `win-x64` build; the files in
`Assets/` are copied next to the exe and read at runtime. The version is set once, in
`Directory.Build.props` at the repository root, which every project picks up; the exe's file
details and the toolbar show it. `Install/build-installer.ps1` reads it with
`dotnet msbuild -getProperty:Version` and passes it to Inno Setup as `/DMyAppVersion=`;
`installer.iss` has no version of its own and stops with an error when built without one.
`build-installer.ps1` deletes the publish folder before publishing, so setup never packs files
left from an earlier build, and stops if the result is missing the exe, WPF's native libraries
(which sit next to the single-file exe), `WebView2Loader.dll` or the page assets, or if any
exe or DLL contains the build folder's path, or if the publish output has a file or folder that
`[InstallDelete]` doesn't cover. Release builds map that folder to `/_/`
(`PathMap` in `Directory.Build.props`), because it usually includes a user name.

`Install/installer.iss` packs the publish folder into `MDGlintSetup.exe`, leaving out `.pdb`
and `.xml` files. Setup runs only on x64 Windows and in 64-bit mode. By default it installs for
the current user, without an administrator prompt, to `%LOCALAPPDATA%\Programs\MDGlint`; the
user can choose all users instead (Program Files, needs administrator). It always registers
the app under `Software\Classes\Applications` (with `.md` and `.markdown` as supported types)
and the `MDGlint.md` ProgID, listed in both extensions' `OpenWithProgids`, so MDGlint shows
in "Open with" for both. The opt-in association task also makes `MDGlint.md` both extensions'
default, though Windows 10 and 11 may still ask the user once to confirm it (setup can't take
the default silently). These entries use Inno Setup's `HKA` root, so they go to `HKCU` for a
per-user install and `HKLM` for an all-users one, matching where the app went. The opt-in
update-check task writes its choice there too, as 1 or 0. Before installing, `[InstallDelete]`
removes the `Assets` and `runtimes` folders and the `.dll`, `.pdb` and `.xml` files from the
install folder, so an update leaves nothing from the older version behind. The `AppId` must
never change, or an update would install a second copy (`releasing.md`, "Keeping updates
working").

GitHub Actions runs two workflows. `ci.yml` builds and tests every pull request and push to
`main`: one job builds Release and Debug, runs the unit, preview-script and end-to-end tests
and checks coverage (`Test-Coverage.ps1`); another installs, updates and uninstalls MDGlint on
the runner (`Test-Installer.ps1`). `release.yml` runs on a `v*` tag, in the `release` environment, so it waits for the
maintainer's approval: it checks the tag against `<Version>`, tests, builds the installer with
`build-installer.ps1`, attests its build provenance, and creates a draft release with the
installer, its `.sha256` file and the notes from `.github/scripts/Get-ReleaseNotes.ps1` (the
version's `CHANGELOG.md` section) plus the hash.

The WebView2 runtime is neither bundled nor installed by setup. After copying the files, setup
checks the registry for it (`pv` under its EdgeUpdate client key, per machine or per user, as
Microsoft documents) and, when it is missing, says so with the link to Microsoft's download page.

## Security notes

The preview is a browser page showing untrusted input. See `SECURITY.md` for the model and the
remote-content switch ("Remote content" above). Raw HTML is sanitized in Core, and the CSP
blocks inline script as a second layer. Local images and media load from any drive, as they did with the earlier `file:` page;
the page's CSP blocks `fetch` and the responses carry no CORS headers, so script could not
read them even if it ran.
