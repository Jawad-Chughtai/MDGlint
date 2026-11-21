# Backlog — MDGlint

Known gaps and their details. Remove an entry when it is done (and note the
change in `CHANGELOG.md`); add one when you find a gap you are not fixing.
Which release picks each one up is in `roadmap.md`; new features are planned
there, not here. Temporary: once the public repo exists, every entry becomes
a GitHub issue and this file is deleted (`issue-tracking.md`).

## Usability

### Video seeking
Local video is served as a plain 200 response without HTTP range support,
so seeking may not work. Add `Range` handling if video use grows.

## Release and packaging

### Code signing
The installer and exe are unsigned, so SmartScreen warns users. Options:
a code-signing certificate or a free OSS signing service.

## Quality

### Not covered by automated tests
Coverage is complete (`testing.md`), but a few things are still only checked by hand in the
release smoke test: the Open dialog, a drop on the toolbar, the title bar colour, Windows asking which
app opens `.md` files, and that an image above the viewport finishing loading after a save
doesn't move the reader (left to the browser's scroll anchoring). The installer tests update
from today's code built as older versions; once releases exist, add the oldest supported
release's published installer to them.

### UNC paths in Markdown links and images
`![](\\server\share\a.png)` shows nothing: in Markdown `\\` is an escaped backslash, so the
path arrives as `\server\share\a.png`. Writing `\\\\server\share\a.png`, a `file://server/share/`
URL, or raw `<img src="\\server\share\a.png">` works. Either document this or treat a leading
single backslash followed by a server name as a share (the preview can't tell it from a
root-relative path, so this needs a decision).

### Error log name
Every unhandled UI exception, not just startup ones, writes
`startup-error.txt` and shows "could not start". Rename or split.
