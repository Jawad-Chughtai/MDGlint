# Roadmap — MDGlint

Where MDGlint is heading, in priority order. Each release is small and has at least one feature
worth showing (a screenshot or short clip for the release notes). Nothing after 1.0.0 is
promised for a date, and the order can change; when it does, update this file and note why in
`decisions.md`.

Guiding constraint for every release: **opening a file stays instant.** Heavier features
(diagrams, export, editing) load only when used.

Each release here maps to a GitHub milestone and its items to issues (`issue-tracking.md`); the
issues hold the details and the discussion, and this file stays the high-level plan. Known gaps
not tied to a release are issues without a milestone. When a release ships, move its section to
`CHANGELOG.md` and delete it here.

## 1.2 — Quick inline edits

Milestone: [1.2 Quick inline edits](https://github.com/Jawad-Chughtai/MDGlint/milestone/2)

Fix a typo or change a couple of lines without switching apps.

- Toggle an edit mode (#20) (or a source pane beside the preview) for the open file.
- Save back to the same file (#21) with the original encoding and line endings preserved; warn if the
  file changed on disk meanwhile.
- Undo (#22), and a clear unsaved-changes indicator and prompt on close.

Also in 1.2:

- **App design** (#19): a redesigned toolbar and overall look. Three directions are mocked up in
  `docs/design/` (a quiet reading page, a dark workbench, a command bar); pick one first.

## 1.3 — Diagrams and rich content

Milestone: [1.3 Diagrams and rich content](https://github.com/Jawad-Chughtai/MDGlint/milestone/3)

- **Mermaid** diagrams (#23) (flowcharts, sequence, class, state, Gantt) rendered from ```` ```mermaid ````
  blocks, following the light or dark theme.
- Math (#24), with KaTeX or MathJax, if it fits the size budget.
- Emoji shortcodes such as `:tada:` (#25). GitHub-style alerts already render since 1.0.0.

Diagram and math libraries must be bundled (no CDN), so previews work offline and stay within the
security model.

## 1.4 — Navigation

Milestone: [1.4 Navigation](https://github.com/Jawad-Chughtai/MDGlint/milestone/4)

- **Outline sidebar** (#26) built from the headings, with click-to-jump and the current section
  highlighted.
- **Find in page** (#27), Ctrl+F, with match count and next/previous.
- **Recent files** list (#28), stored locally and clearable.
- A proper menu or command bar (#29) for the growing set of actions, with keyboard shortcuts.

## 1.5 — Tabs

Milestone: [1.5 Tabs](https://github.com/Jawad-Chughtai/MDGlint/milestone/5)

- **Multiple files in tabs** (#30), each with its own scroll position and file watcher.
- Single instance (#31): drag and drop opens in a new tab; double-clicking another file while MDGlint is open adds a
  tab to the running window instead of starting a second one (single instance).
- Restore open tabs on next launch, optionally (#32).

## Exploring

Ideas not yet committed to a release.

- **Mac and Linux, possibly with Electron** (#38). Kept open, not planned: Electron (MIT) would bring
  other platforms and remove the WebView2 licence terms, but means rewriting the app around a
  bundled Chromium that MDGlint itself must keep patched, plus a larger, slower app (decision
  log).
- **Download updates in the background** (#17). Moved out of 1.1, to be designed later (decision
  log). Installing when MDGlint closes would reverse "Update checks are opt-in", and MDGlint
  runs one process per open file, so closing one window can't simply run setup. One option is to
  download in the background and install only when **Restart now** is pressed.
- **Quick peek at other file types** (#39): plain text, JSON, YAML, CSV as a table, source code with
  highlighting. Decide whether this fits once tabs exist.

## Ongoing tracks

Not tied to a feature release; picked up as soon as practical.

- **Code signing** (#36). Removes the SmartScreen warning, which matters most for first-time users. Look
  at Azure Trusted Signing, a standard OV/EV certificate, and free signing programmes for open
  source (for example SignPath Foundation). Record the choice in `decisions.md`. 1.0.0 shipped
  unsigned (the README explains the warning); the first step is to apply to SignPath
  Foundation, which needs the repository to be public (it is since 2026-11-21).
- **Keep the self-contained build.** .NET stays bundled; WebView2 stays the Evergreen runtime
  that Windows provides and updates (`decisions.md`).
- **Tests stay at full coverage** since 1.0.0: CI enforces it (`testing.md`).
- **Bundled library updates.** Before each release, check `github-markdown-css` and highlight.js
  for new versions; update the files and
  `THIRD-PARTY-NOTICES.md`.
- **Release showcase**: each release gets light and dark screenshots or a short GIF of the new
  feature in its release notes, ready to share. No personal files, paths or names in them.
