# Changelog

All notable changes to MDGlint are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- An **Open…** button on the page shown when no file is open, with a line saying a Markdown file
  can be dropped there (#18).
- **Print…** in the **⋯** menu, also with Ctrl+P, through WebView2's print dialog, which shows a
  preview (#14).
- **Export ▸ PDF…** in the **⋯** menu, saved beside the open file by default (#12).
- **Export ▸ HTML…** in the **⋯** menu: one self-contained `.html` file in the current theme, with
  the styles inlined and the images embedded (#11). Remote images are included only when
  they're allowed for the file. Video and audio are left out.
- **Print in light theme** in the **⋯** menu, on by default: printouts and PDFs use the light
  theme while the window shows the dark one.
- A **Copy** button on each code block, shown when the pointer is over it, which copies the
  code as written (#15).
- **Copy Markdown** in the **⋯** menu, also with Ctrl+Shift+C, which copies the whole file's
  Markdown as plain text (#15).
- PDFs are titled with the file's name.
- Printouts and PDFs have a footer with "MDGlint" and the page number ("1 / 4"), in place of
  the print dialog's header and footer (date, title, the preview's internal address).
- "Starting the preview…" shows while the preview starts.
- When a closed MDGlint window has left its preview process running, a new window says so at
  once, instead of failing after a long wait, and offers **End it and try again**, which ends
  only that leftover process and starts the preview.

## [1.0.0] - 2026-11-22

### Added

- Markdown preview rendered by Markdig with advanced extensions (tables, task lists, footnotes,
  autolinks, alerts) and styled with github-markdown-css, in light and dark themes.
- Syntax highlighting with highlight.js and its GitHub themes, for code blocks that name a
  language: the common highlight.js set (about 35 languages) plus PowerShell, batch files and
  Dockerfiles.
- Open files from the command line, the Open button, or by dropping them anywhere on the window
  (`.md`, `.markdown`).
- The window opens maximized the first time, and then as you left it: maximized or not, and its
  last size and position (moved back on screen if a monitor is gone).
- Live reload when the open file changes on disk, updating in place so you keep your place,
  even when text above it was added or removed; opening a different file starts at the top.
- Theme choice saved between launches; switching theme doesn't reload the document. Saving
  settings never shows an error: if another program has the file open, MDGlint tries again,
  and otherwise keeps the change until it closes.
- Images from relative paths (including `../`) and absolute paths on any drive or network share.
- Links to other Markdown files open in MDGlint; links to other local files show them in
  Explorer; web links open in the default browser.
- A bar when a file tries to load images, video or audio from the internet, with "Load for this
  file" and "Always allow", and a Remote button in the toolbar to turn "always allow" on or off.
- The version shows in the toolbar and in the exe's file details.
- Startup errors are shown and saved to `%LOCALAPPDATA%\MDGlint\startup-error.txt`.
- Inno Setup installer for x64 Windows, with optional `.md` and `.markdown` file association;
  MDGlint is listed under "Open with" for both either way. It installs for the current user
  without an administrator prompt, or for all users if you choose. It says so when the Microsoft
  Edge WebView2 Runtime is missing, with a link to Microsoft's download page, and refuses
  Windows older than Windows 10 version 1607, where MDGlint can't run.
- A clear message with a link to Microsoft's download page when the WebView2 Runtime is missing
  or fails to start.
- Releases are built by GitHub Actions, never on a personal machine, and list the installer's
  SHA-256 in the notes and in an `MDGlintSetup.exe.sha256` file.
- Optional update checks, off unless you turn them on in setup or the new **⋯** menu, which
  also has **Check for updates now**. MDGlint checks GitHub at most once a day and shows a bar
  when a newer version is out; **Install** downloads it, checks its SHA-256, and installs it
  and reopens MDGlint. Nothing is installed until you press it.
- Uninstalling asks whether to keep your settings; choosing No removes them and MDGlint's
  cache too.

### Security

- Remote images, video and audio (including inline-style `background-image` URLs) are blocked
  until you allow them, so a file can't reveal when you opened it.
- Raw HTML in Markdown files is sanitized: scripts, event handlers, frames, forms, style sheets
  and `javascript:` links are removed, while passive markup such as `<details>`, `<kbd>`, image
  sizes and `<picture>` keeps working.
- Scripts in Markdown files (`<script>` elements and event-handler attributes) never run: a
  strict Content Security Policy only allows MDGlint's own scripts, and also blocks frames,
  plugins and form submission.
- The preview reads local files only as images, audio and video, and script can't read them.
- The preview turns off WebView2's SmartScreen check, which would send Microsoft the address of
  each page it shows, and keeps WebView2's crash reports on the PC instead of sending them to
  Microsoft. README and SECURITY say what the WebView2 Runtime still sends Microsoft.
- Each release's installer has a signed build-provenance attestation, so you can check it was
  built by this repository's release workflow:
  `gh attestation verify MDGlintSetup.exe -R Jawad-Chughtai/MDGlint`.

[Unreleased]: https://github.com/Jawad-Chughtai/MDGlint/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Jawad-Chughtai/MDGlint/releases/tag/v1.0.0
