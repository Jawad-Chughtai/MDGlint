using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MDGlint.Core.Services;

/// <summary>
/// The fixed parts of an exported Word document: its styles (Word's own heading, quote and
/// footnote styles, so the navigation pane and a table of contents work), the list formats, and
/// its settings. Colours follow GitHub's light theme.
/// </summary>
internal static class DocxParts
{
    public const string BodyFont = "Calibri";
    public const string CodeFont = "Consolas";
    public const string BorderColor = "D0D7DE";
    public const string CodeFill = "F6F8FA";
    public const string LinkColor = "0969DA";

    /// <summary>The abstract list for bulleted lists, and the one numbered lists restart from.</summary>
    public const int BulletList = 1;
    public const int NumberedList = 2;

    /// <summary>Each list level is indented this much further (twips).</summary>
    public const int ListIndent = 720;
    public const int Hanging = 360;

    /// <summary>Each quote level is indented this much further (twips).</summary>
    public const int QuoteIndent = 360;

    private static readonly int[] HeadingSizes = [32, 28, 26, 24, 22, 22];

    public static Styles Styles()
    {
        var styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = BodyFont, HighAnsi = BodyFont, EastAsia = BodyFont, ComplexScript = BodyFont },
                    new FontSize { Val = "22" },
                    new FontSizeComplexScript { Val = "22" },
                    new Languages { Val = "en-US" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "160", Line = "264", LineRule = LineSpacingRuleValues.Auto }))),
            ParagraphStyle("Normal", "Normal", isDefault: true));

        for (var level = 1; level <= 6; level++)
        {
            var properties = new StyleParagraphProperties(
                new KeepNext(),
                new KeepLines(),
                new SpacingBetweenLines { Before = level <= 2 ? "360" : "240", After = "120" },
                new OutlineLevel { Val = level - 1 });
            if (level <= 2)
            {
                // GitHub underlines the top two levels.
                properties.ParagraphBorders = new ParagraphBorders(
                    new BottomBorder { Val = BorderValues.Single, Size = 6, Space = 4, Color = BorderColor });
            }

            var run = new StyleRunProperties(new Bold(), new FontSize { Val = HeadingSizes[level - 1].ToString() });
            if (level == 6)
            {
                run.Color = new Color { Val = "59636E" };
            }

            styles.Append(ParagraphStyle($"Heading{level}", $"heading {level}", properties: properties, run: run, next: "Normal"));
        }

        styles.Append(
            ParagraphStyle("Quote", "Quote", run: new StyleRunProperties(new Color { Val = "59636E" })),
            ParagraphStyle("CodeBlock", "Code Block", custom: true,
                properties: new StyleParagraphProperties(
                    new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = CodeFill },
                    new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }),
                run: new StyleRunProperties(Fonts(CodeFont), new FontSize { Val = "19" }, new FontSizeComplexScript { Val = "19" })),
            ParagraphStyle("FootnoteText", "footnote text",
                properties: new StyleParagraphProperties(new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }),
                run: new StyleRunProperties(new FontSize { Val = "18" }, new FontSizeComplexScript { Val = "18" })),
            CharacterStyle("InlineCode", "Inline Code", custom: true, new StyleRunProperties(
                Fonts(CodeFont),
                new FontSize { Val = "20" },
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = CodeFill })),
            CharacterStyle("Hyperlink", "Hyperlink", custom: false, new StyleRunProperties(
                new Color { Val = LinkColor },
                new Underline { Val = UnderlineValues.Single })),
            CharacterStyle("FootnoteReference", "footnote reference", custom: false, new StyleRunProperties(
                new VerticalTextAlignment { Val = VerticalPositionValues.Superscript })));
        return styles;
    }

    /// <summary>The list formats; <see cref="DocxWriter"/> adds a numbering instance per numbered list.</summary>
    public static AbstractNum[] Lists() =>
    [
        List(BulletList, level => (NumberFormatValues.Bullet, (level % 3) switch { 0 => "•", 1 => "◦", _ => "▪" })),
        List(NumberedList, level => ((level % 3) switch
        {
            0 => NumberFormatValues.Decimal,
            1 => NumberFormatValues.LowerLetter,
            _ => NumberFormatValues.LowerRoman,
        }, $"%{level + 1}.")),
    ];

    /// <summary>Opens without Compatibility Mode, with the footnote separators when there are footnotes.</summary>
    public static Settings Settings(bool footnotes)
    {
        var settings = new Settings();
        if (footnotes)
        {
            settings.Append(new FootnoteDocumentWideProperties(
                new FootnoteSpecialReference { Id = -1 },
                new FootnoteSpecialReference { Id = 0 }));
        }

        settings.Append(new Compatibility(new CompatibilitySetting
        {
            Name = CompatSettingNameValues.CompatibilityMode,
            Uri = "http://schemas.microsoft.com/office/word",
            Val = "15",
        }));
        return settings;
    }

    private static AbstractNum List(int id, Func<int, (NumberFormatValues Format, string Text)> level)
    {
        var list = new AbstractNum(new MultiLevelType { Val = MultiLevelValues.HybridMultilevel }) { AbstractNumberId = id };
        for (var i = 0; i < 9; i++)
        {
            var (format, text) = level(i);
            list.Append(new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = format },
                new LevelText { Val = text },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation
                {
                    Left = (ListIndent * (i + 1)).ToString(),
                    Hanging = Hanging.ToString(),
                })) { LevelIndex = i });
        }

        return list;
    }

    private static RunFonts Fonts(string font) => new() { Ascii = font, HighAnsi = font, EastAsia = font, ComplexScript = font };

    private static Style ParagraphStyle(
        string id, string name, bool isDefault = false, bool custom = false,
        StyleParagraphProperties? properties = null, StyleRunProperties? run = null, string? next = null)
    {
        var style = new Style(new StyleName { Val = name }) { Type = StyleValues.Paragraph, StyleId = id };
        if (isDefault)
        {
            style.Default = true;
        }
        else
        {
            style.Append(new BasedOn { Val = "Normal" });
        }

        if (custom)
        {
            style.CustomStyle = true;
        }

        if (next is not null)
        {
            style.Append(new NextParagraphStyle { Val = next });
        }

        style.Append(new PrimaryStyle());
        if (properties is not null)
        {
            style.Append(properties);
        }

        if (run is not null)
        {
            style.Append(run);
        }

        return style;
    }

    private static Style CharacterStyle(string id, string name, bool custom, StyleRunProperties run)
    {
        var style = new Style(new StyleName { Val = name }, run) { Type = StyleValues.Character, StyleId = id };
        if (custom)
        {
            style.CustomStyle = true;
        }

        return style;
    }
}
