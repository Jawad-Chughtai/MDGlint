# Security Policy

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub:
[Report a vulnerability](https://github.com/Jawad-Chughtai/MDGlint/security/advisories/new).
Do not open a public issue.

Include the MDGlint version, your Windows version, and a Markdown file or steps that reproduce the
problem. The maintainer aims to reply within a week. Once a fix is released, the advisory will be
published with credit to you unless you prefer otherwise.

## Security model

MDGlint renders a Markdown file to HTML and shows it in an embedded Microsoft Edge WebView2
control. A Markdown file is a document, and opening one should never be able to run code or reach
the network without the user knowing.

### Known issues

None at the moment. Known issues are listed here until they are fixed.

### What is already in place

- Remote content is blocked by default. Images, video and audio from `http:` or `https:`
  addresses, including `background-image` URLs in inline styles, would let their server learn
  your IP address and when you opened the file (a "tracking pixel"). When a file tries to load
  any, MDGlint shows a bar where you can load it for that file (until MDGlint closes) or
  always; "Always allow" is saved and can be turned off with the Remote button in the toolbar.
  The bar is part of the window, not the page, so a file cannot fake or press it.
- Raw HTML in a Markdown file is sanitized: only passive elements and attributes are kept
  (headings, tables, images, `<details>`, `<kbd>` and similar). Scripts, event handlers, frames,
  forms, style sheets and `javascript:` links are removed. As a second layer, the preview's
  Content Security Policy only allows MDGlint's own scripts, and blocks frames, plugins and
  form submission.
- The preview only ever shows MDGlint's own page. Clicking an `http`, `https` or `mailto` link
  opens it in your default app; a link to another Markdown file opens it in MDGlint; a link to
  any other local file or folder shows it in Explorer instead of running it. Navigation that a
  file tries on its own, including opening windows, is ignored.
- The preview can load local files only as images, audio and video, which it needs to show them;
  any other file type is refused, and script cannot read files.
- WebView2 data is kept in MDGlint's own folder (`%LOCALAPPDATA%\MDGlint\WebView2`), separate
  from your Edge profile.
- The preview runs in Microsoft's WebView2 Runtime, which sends Microsoft diagnostic data whatever
  MDGlint does: the data Windows marks as required is always sent, and how much more follows
  your Windows setting (**Settings > Privacy & security > Diagnostics & feedback > Diagnostic
  data**). Apps can't turn this off. MDGlint does turn off WebView2's SmartScreen check, which
  would otherwise send Microsoft the address of each page the preview shows, and keeps
  WebView2's crash reports in `%LOCALAPPDATA%\MDGlint\WebView2` instead of sending them to
  Microsoft.
- MDGlint itself sends no telemetry. Its only network requests of its own are update checks, and only
  when you turn them on (in setup or the **⋯** menu) or press **Check for updates now**: a
  request to the GitHub API for the latest release (with **Get beta versions** on, for the
  newest releases, pre-releases included), at most once a day, which lets GitHub see your IP
  address and MDGlint's version. The one other case is an export to HTML or Word of a file
  whose remote content you allowed: MDGlint downloads those images itself to put them in the
  exported file. With checks off and no such export, MDGlint makes no requests of its own.
- Exported files load nothing when opened. An HTML export carries its styles and images inside
  it, with a Content Security Policy that allows only those; a Word export stores its images
  and links nothing. Both start from the already sanitized page, so a file's scripts don't
  reach them, and Word exports turn only `http`, `https` and `mailto` links into hyperlinks.
- An update is installed only when you press **Install**. MDGlint downloads only the installer and
  its `MDGlintSetup.exe.sha256` file attached to a release of this repository, refuses the
  installer unless its SHA-256 matches that file, and keeps it locked from that check until
  setup starts. The hash proves the download is the file the release published, not who
  published it: someone who took over the maintainer's GitHub account could publish a release
  with a matching hash. The account uses two-factor authentication to make that harder; code
  signing would close the gap.
- Releases are built only by this repository's release workflow on GitHub's runners, never on a
  personal machine. A release run waits for the maintainer's approval, and only `v*` tags can
  start one. Each installer has a signed build-provenance attestation, so anyone can check
  which workflow run and commit built it
  (`gh attestation verify MDGlintSetup.exe -R Jawad-Chughtai/MDGlint`). A published release
  can't be changed afterwards: its files and tag are immutable. MDGlint itself checks the
  SHA-256, not the attestation.
- The test hooks that send update requests to a local server, move MDGlint's data folders,
  log programs instead of starting them or pass extra browser arguments to WebView2 (such as a
  DevTools port) exist only in Debug builds, which are never released.
  An installed MDGlint ignores those environment variables.

Each of these is checked by automated tests on every change, most of them in the real app:
scripts in a file not running, other local files refused, where links go, remote content
blocked until allowed, no update request unless checks are on, and a mismatched installer
refused ([docs/testing.md](docs/testing.md)).
