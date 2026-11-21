# Contributing to MDGlint

Thanks for helping. MDGlint aims to be the quickest way to open, read and lightly work with
Markdown on Windows. Whatever it grows into, it should still open a file instantly; changes that
keep it fast and simple are the easiest to accept.

## Before you start

- **Bugs:** open an issue with the bug template. A small `.md` file that shows the problem helps a
  lot.
- **Features:** open an issue to discuss it before writing code, so we can agree it fits the
  project. Check [docs/roadmap.md](docs/roadmap.md) first; if it is already planned, say so on
  the issue so effort isn't duplicated.
- **Security problems:** do not open a public issue. Follow [SECURITY.md](SECURITY.md).

## Development setup

- Windows 10 or 11, x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Visual Studio 2026, Rider, or VS Code with the C# Dev Kit (any of them works)
- [Inno Setup](https://jrsoftware.org/isinfo.php) 6.3 or later, only if you want to build the
  installer
- [Node.js](https://nodejs.org) 22.8 or later, only to run the preview script's tests

```powershell
dotnet build MDGlint.slnx
dotnet run --project MDGlint.Desktop -- path\to\file.md
```

The solution file is `MDGlint.slnx` (the newer XML solution format); recent Visual Studio and the
.NET 10 SDK both open it.

## Project layout

- `MDGlint.Core` has no Windows dependencies: Markdown rendering, themes, settings model, and the
  interfaces the desktop app implements. Keep it that way so it stays testable and portable.
- `MDGlint.Desktop` is the WPF app: window, WebView2 host, file watching, settings storage,
  installer.
- `MDGlint.Core.Tests` and `MDGlint.Desktop.Tests` hold the unit tests (and, in
  `MDGlint.Desktop.Tests/Preview`, the Node tests for the preview script);
  `MDGlint.EndToEnd.Tests` drives the real app.

[docs/architecture.md](docs/architecture.md) explains how a file goes from disk to the screen.

## Making a change

1. Fork the repository and create a branch from `main`, named after the issue (for example
   `12-relative-images`).
2. Keep the change focused; one fix or feature per pull request.
3. Follow the existing style: file-scoped namespaces, nullable enabled, `sealed` classes where
   nothing inherits, `_camelCase` private fields.
4. Build and try it by hand with a few Markdown files: light and dark themes, tables, code blocks,
   images with relative paths, and live reload after saving the file.
5. Add an entry under **Unreleased** in [CHANGELOG.md](CHANGELOG.md) for anything a user would
   notice.
6. Open a pull request and fill in the template. GitHub Actions builds it and runs the tests;
   they must pass before it's merged.

Every change comes with tests, and a bug fix with a test that fails without it. Run them with:

```powershell
dotnet test MDGlint.slnx
node --test "MDGlint.Desktop.Tests/Preview/*.test.mjs"
```

The end-to-end tests open MDGlint windows for a few seconds each and need the WebView2
Runtime; they use a folder of their own, never your settings. The preview tests need Node 22.8
or later, and nothing from npm. Everything that ships is covered at 100% of lines and branches, and
CI fails if a change lowers that: logic belongs in a class that can be unit-tested, not in a
window or WebView2 event handler. [docs/testing.md](docs/testing.md) explains the tests, how to
check coverage locally, and what is left to the end-to-end tests.

Changes to settings, the installer or the release workflow must keep updates working for every
older version: never rename or remove a setting without migrating it, never change the
installer's `AppId`, and keep the release asset names. The full list is in
[docs/releasing.md](docs/releasing.md), "Keeping updates working".

## Third-party code

Adding a NuGet package or bundled asset needs a compatible license (MIT, BSD, Apache 2.0 or
similar) and an entry in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
