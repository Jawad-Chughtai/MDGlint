using MDGlint.Core.Services;

namespace MDGlint.Core.Tests;

public sealed class HtmlContentSanitizerTests
{
    private readonly HtmlContentSanitizer _sanitizer = new();

    [Fact]
    public void Sanitize_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _sanitizer.Sanitize(null!));
    }

    [Theory]
    [InlineData("<h1 id=\"intro\">Intro</h1>")]
    [InlineData("<pre><code class=\"language-js\">x</code></pre>")]
    [InlineData("<p><kbd>Ctrl</kbd> H<sub>2</sub>O x<sup>2</sup> <mark>m</mark></p>")]
    [InlineData("<p><ins>i</ins> <del>d</del> <s>s</s> <abbr title=\"t\">a</abbr></p>")]
    [InlineData("<dl><dt>t</dt><dd>d</dd></dl>")]
    [InlineData("<p align=\"center\">c</p>")]
    [InlineData("<img src=\"a.png\" width=\"100\" height=\"20\" alt=\"x\" align=\"right\">")]
    [InlineData("<a name=\"old-anchor\">n</a>")]
    [InlineData("<a href=\"#section\">s</a>")]
    [InlineData("<a href=\"../other.md#sec\">rel</a>")]
    [InlineData("<a href=\"https://example.com/\">w</a>")]
    [InlineData("<a href=\"http://example.com/\">w</a>")]
    [InlineData("<a href=\"mailto:someone@example.com\">m</a>")]
    [InlineData("<img src=\"C:\\pics\\a.png\">")]
    [InlineData("<img src=\"C:/pics/a.png\">")]
    [InlineData("<img src=\"C:%5Cpics%5Ca.png\">")]
    [InlineData("<a href=\"c:%5cdocs%5cb.md\">l</a>")]
    [InlineData("<img src=\"file:///D:/pics/a.png\">")]
    [InlineData("<img src=\"\\\\server\\share\\a.png\">")]
    [InlineData("<img src=\"data:image/png;base64,AAAA\">")]
    [InlineData("<video src=\"v.mp4\" controls=\"\" poster=\"p.png\" loop=\"\" muted=\"\" playsinline=\"\"></video>")]
    [InlineData("<audio controls=\"\"><source src=\"a.mp3\" type=\"audio/mpeg\"></audio>")]
    [InlineData("<picture><source srcset=\"dark.png\" media=\"(prefers-color-scheme: dark)\"><img src=\"light.png\"></picture>")]
    [InlineData("<ul class=\"contains-task-list\"><li class=\"task-list-item\"><input disabled=\"disabled\" type=\"checkbox\" checked=\"checked\"> done</li></ul>")]
    [InlineData("<table><thead><tr><th style=\"text-align: left\">a</th></tr></thead><tbody><tr><td colspan=\"2\">1</td></tr></tbody></table>")]
    public void Sanitize_SafeMarkup_IsKept(string html)
    {
        Assert.Equal(html, _sanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_Details_IsKept()
    {
        Assert.Equal(
            "<details open=\"\"><summary>More</summary><p>Hidden</p></details>",
            _sanitizer.Sanitize("<details open><summary>More</summary><p>Hidden</p></details>"));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "")]
    [InlineData("<script src=\"https://example.com/x.js\"></script>", "")]
    [InlineData("<style>body { display: none }</style>", "")]
    [InlineData("<link rel=\"stylesheet\" href=\"https://example.com/x.css\">", "")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0; url=https://example.com/\">", "")]
    [InlineData("<base href=\"https://example.com/\">", "")]
    [InlineData("<iframe src=\"https://example.com/\"></iframe>", "")]
    [InlineData("<frameset><frame src=\"https://example.com/\"></frameset>", "")]
    [InlineData("<object data=\"x.swf\"></object>", "")]
    [InlineData("<embed src=\"x.swf\">", "")]
    [InlineData("<svg><script>alert(1)</script></svg>", "")]
    [InlineData("<math><mi>x</mi></math>", "")]
    [InlineData("<template><img src=\"x\"></template>", "")]
    [InlineData("<form action=\"https://example.com/\"><input type=\"text\" name=\"q\"></form>", "")]
    [InlineData("<button>Go</button>", "")]
    [InlineData("<textarea>t</textarea>", "")]
    [InlineData("<select><option>o</option></select>", "")]
    [InlineData("<input type=\"text\" value=\"x\">", "")]
    [InlineData("<input type=\"password\">", "")]
    [InlineData("<input type=\"image\" src=\"x.png\">", "")]
    [InlineData("<input>", "")]
    [InlineData("<!-- note -->", "")]
    [InlineData("<img src=\"x\" onerror=\"alert(1)\">", "<img src=\"x\">")]
    [InlineData("<p onclick=\"alert(1)\" onmouseover=\"alert(2)\">p</p>", "<p>p</p>")]
    [InlineData("<body onload=\"alert(1)\">b</body>", "b")]
    [InlineData("<a href=\"javascript:alert(1)\">j</a>", "<a>j</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">j</a>", "<a>j</a>")]
    [InlineData("<a href=\" javascript:alert(1)\">j</a>", "<a>j</a>")]
    [InlineData("<a href=\"java&#x09;script:alert(1)\">j</a>", "<a>j</a>")]
    [InlineData("<a href=\"&#106;avascript:alert(1)\">j</a>", "<a>j</a>")]
    [InlineData("<a href=\"vbscript:msgbox(1)\">j</a>", "<a>j</a>")]
    [InlineData("<a href=\"data:text/html,<script>alert(1)</script>\">d</a>", "<a>d</a>")]
    [InlineData("<a href=\"data:image/png;base64,AAAA\">d</a>", "<a>d</a>")]
    [InlineData("<img src=\"data:text/html,x\">", "<img>")]
    [InlineData("<video src=\"data:video/mp4;base64,AAAA\"></video>", "<video></video>")]
    [InlineData("<img src=\"javascript:alert(1)\">", "<img>")]
    [InlineData("<a href=\"https://example.com/\" target=\"_blank\">w</a>", "<a href=\"https://example.com/\">w</a>")]
    [InlineData("<a href=\"#x\" accesskey=\"s\">a</a>", "<a href=\"#x\">a</a>")]
    [InlineData("<img name=\"getElementById\" src=\"x\">", "<img src=\"x\">")]
    [InlineData("<video src=\"v.mp4\" autoplay></video>", "<video src=\"v.mp4\"></video>")]
    [InlineData("<div data-x=\"1\">d</div>", "<div>d</div>")]
    public void Sanitize_ActiveContent_IsRemoved(string html, string expected)
    {
        Assert.Equal(expected, _sanitizer.Sanitize(html));
    }

    [Theory]
    [InlineData("<p style=\"background: url(javascript:alert(1))\">p</p>")]
    [InlineData("<p style=\"background-image: url('vbscript:x')\">p</p>")]
    [InlineData("<p style=\"width: expression(alert(1))\">p</p>")]
    [InlineData("<p style=\"behavior: url(x.htc)\">p</p>")]
    [InlineData("<p style=\"-moz-binding: url(x.xml#x)\">p</p>")]
    public void Sanitize_DangerousStyles_AreRemoved(string html)
    {
        Assert.Equal("<p>p</p>", _sanitizer.Sanitize(html));
    }

    [Fact]
    public void Sanitize_SrcsetWithScriptUrl_IsRemoved()
    {
        Assert.Equal(
            "<picture><source media=\"(min-width: 1px)\"></picture>",
            _sanitizer.Sanitize("<picture><source srcset=\"javascript:alert(1) 1x\" media=\"(min-width: 1px)\"></picture>"));
    }

    [Fact]
    public void Sanitize_Checkbox_IsAlwaysDisabled()
    {
        Assert.Equal(
            "<input type=\"checkbox\" disabled=\"disabled\">",
            _sanitizer.Sanitize("<input type=\"checkbox\" name=\"n\" value=\"v\">"));
    }

    [Fact]
    public void Sanitize_CheckboxTypeIsCaseInsensitive()
    {
        Assert.Equal(
            "<input type=\"CheckBox\" disabled=\"disabled\">",
            _sanitizer.Sanitize("<input type=\"CheckBox\">"));
    }

    [Fact]
    public void Sanitize_TextAroundRemovedElements_IsKept()
    {
        Assert.Equal("<p>before  after</p>", _sanitizer.Sanitize("<p>before <script>x</script> after</p>"));
    }
}
