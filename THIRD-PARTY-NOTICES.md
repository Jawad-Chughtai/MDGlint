# Third-Party Notices

MDGlint is licensed under the [MIT License](LICENSE). It includes or depends on the components
below, each under its own license. The full license texts are at the linked locations.

| Component | Used for | License |
|---|---|---|
| [Markdig](https://github.com/xoofx/markdig) | Markdown to HTML conversion | [BSD 2-Clause](https://github.com/xoofx/markdig/blob/master/license.txt) |
| [HtmlSanitizer](https://github.com/mganss/HtmlSanitizer) | Sanitizing raw HTML in Markdown | [MIT](https://github.com/mganss/HtmlSanitizer/blob/master/LICENSE.md) |
| [AngleSharp](https://github.com/AngleSharp/AngleSharp), [AngleSharp.Css](https://github.com/AngleSharp/AngleSharp.Css) | HTML and CSS parsing for HtmlSanitizer, and building exported HTML files | [MIT](https://github.com/AngleSharp/AngleSharp/blob/devel/LICENSE) |
| [github-markdown-css](https://github.com/sindresorhus/github-markdown-css) 5.9.0 | Preview styles (`Assets/vendor/github-markdown-css/`), also inlined in exported HTML files with its license | [MIT](https://github.com/sindresorhus/github-markdown-css/blob/main/license) |
| [highlight.js](https://github.com/highlightjs/highlight.js) 11.12.0 | Syntax highlighting and its GitHub themes (`Assets/vendor/highlight.js/`); a theme is also inlined in exported HTML files with its license | [BSD 3-Clause](https://github.com/highlightjs/highlight.js/blob/main/LICENSE) |
| [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) | Embedded browser control for the preview | [Microsoft WebView2 SDK license](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4022.49/License) |
| [.NET runtime](https://github.com/dotnet/runtime) | Bundled in the self-contained build | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| [Inno Setup](https://jrsoftware.org/isinfo.php) | Builds `MDGlintSetup.exe`, which runs its setup and uninstall code | [Inno Setup License](https://jrsoftware.org/files/is/license.txt) |

The Microsoft Edge WebView2 Runtime is not bundled; it is installed separately by Windows or the
user.

The bundled files in `MDGlint.Desktop/Assets/vendor/` are unmodified copies from the npm
packages `github-markdown-css` and `@highlightjs/cdn-assets`, each with its license file beside it.
`Assets/app.css`, `Assets/export.css`, `Assets/app.js` and `Assets/preview.js` were written for this project.
MDGlint is not affiliated with or endorsed by GitHub.

Test-only dependencies, not shipped with the app: [xUnit](https://github.com/xunit/xunit)
(Apache 2.0), [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) (MIT) and
[coverlet](https://github.com/coverlet-coverage/coverlet) (MIT). The preview script's tests use
Node.js's built-in test runner, with no npm packages.

When adding a dependency or bundled asset, add it to this table in the same change.
