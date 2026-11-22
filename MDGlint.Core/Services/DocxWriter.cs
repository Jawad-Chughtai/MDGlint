using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using Document = DocumentFormat.OpenXml.Wordprocessing.Document;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace MDGlint.Core.Services;

/// <summary>
/// Turns the preview's HTML into the body of a Word document for <see cref="DocxExporter"/>.
/// Block elements become paragraphs, lists, tables and code blocks; inline elements become
/// formatted runs. Anything it doesn't know is read for its text, so nothing in the file is lost
/// and nothing in it runs.
/// </summary>
internal sealed partial class DocxWriter(MainDocumentPart main, IReadOnlyDictionary<string, WordImage?> images)
{
    /// <summary>The widest an image is shown: 6 inches at 96 pixels an inch, inside any page's margins.</summary>
    public const int MaxImageWidth = 576;

    private const long EmusPerPixel = 9525;

    private static readonly HashSet<string> BlockElements =
    [
        "address", "article", "aside", "center", "dd", "details", "div", "dl", "dt", "figcaption", "figure",
        "footer", "header", "hgroup", "li", "main", "nav", "p", "section", "summary",
    ];

    private readonly Body _body = new();
    private readonly Dictionary<string, IElement> _footnoteItems = new(StringComparer.Ordinal);
    private readonly Dictionary<(OpenXmlPart, string), string> _imageParts = [];
    private readonly List<NumberingInstance> _numbered = [];
    private FootnotesPart? _footnotes;
    private int? _bullets;
    private long _footnoteCount;
    private uint _drawingCount;

    public void Write(IElement content)
    {
        // Footnotes arrive as a list at the end; each becomes a Word footnote where it is referred to.
        foreach (var notes in content.QuerySelectorAll("div.footnotes").ToList())
        {
            foreach (var item in notes.Children.Where(c => c.LocalName == "ol").SelectMany(list => list.Children))
            {
                _footnoteItems.TryAdd("#" + item.Id, item);
            }

            notes.Remove();
        }

        Blocks(content, _body, new Layout(main), default);

        main.Document = new Document(_body);
        main.AddNewPart<StyleDefinitionsPart>().Styles = DocxParts.Styles();
        var numbering = new Numbering(DocxParts.Lists());
        numbering.Append(_numbered);
        main.AddNewPart<NumberingDefinitionsPart>().Numbering = numbering;
        main.AddNewPart<DocumentSettingsPart>().Settings = DocxParts.Settings(_footnotes is not null);
    }

    private void Blocks(INode parent, OpenXmlCompositeElement target, Layout layout, Format format, NumberingProperties? numbering = null)
    {
        var flow = new Flow(target, layout, numbering);
        Children(parent, flow, format);
        flow.Break();
    }

    private void Children(INode parent, Flow flow, Format format)
    {
        foreach (var node in parent.ChildNodes)
        {
            Node(node, flow, format);
        }
    }

    private void Node(INode node, Flow flow, Format format)
    {
        switch (node)
        {
            case IText text:
                flow.Text(text.Data, format);
                break;
            case IElement element:
                Element(element, flow, format);
                break;
        }
    }

    private void Element(IElement element, Flow flow, Format format)
    {
        var name = element.LocalName;
        var layout = flow.Layout;
        switch (name)
        {
            case "video" or "audio" or "source":
                return;
            case "br":
                flow.Add(new Run(new Break()), space: true);
                return;
            case "img":
                Image(element, flow, format);
                return;
            case "input":
                flow.Add(Run(element.HasAttribute("checked") ? "☑" : "☐", format), space: false);
                return;
            case "a":
                Link(element, flow, format);
                return;
            case "ul" or "ol":
                flow.Break();
                List(element, flow.Target, layout, format);
                return;
            case "pre":
                flow.Break();
                CodeBlock(element, flow.Target, layout);
                return;
            case "table":
                flow.Break();
                Table(element, flow.Target, layout);
                return;
            case "hr":
                flow.Break();
                NewParagraph(flow.Target, layout, null).ParagraphProperties!.ParagraphBorders =
                    new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 6, Space = 1, Color = DocxParts.BorderColor });
                return;
            case "blockquote":
            case "div" when element.ClassList.Contains("markdown-alert"):
                flow.Break();
                Blocks(element, flow.Target, layout with
                {
                    Quote = layout.Quote + 1,
                    QuoteColor = AlertColor(element),
                    Style = layout.Style ?? "Quote",
                }, format, flow.TakeNumbering());
                return;
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                flow.Break();
                Blocks(element, flow.Target, layout with { Style = $"Heading{name[1]}" }, format, flow.TakeNumbering());
                return;
            case "dt" or "summary":
            case "p" when element.ClassList.Contains("markdown-alert-title"):
                flow.Break();
                Blocks(element, flow.Target, layout, format with { Bold = true }, flow.TakeNumbering());
                return;
            case "dd":
                flow.Break();
                Blocks(element, flow.Target, layout with { Indent = layout.Indent + 1 }, format, flow.TakeNumbering());
                return;
        }

        if (BlockElements.Contains(name))
        {
            flow.Break();
            Blocks(element, flow.Target, layout, format, flow.TakeNumbering());
        }
        else
        {
            Children(element, flow, Apply(name, format));
        }
    }

    /// <summary>GitHub's border colour for an alert (<c>markdown-alert-note</c> and the rest); grey for a quote.</summary>
    private static string AlertColor(IElement element) =>
        element.ClassList.FirstOrDefault(name => AlertColors.ContainsKey(name)) is { } alert ? AlertColors[alert] : DocxParts.BorderColor;

    private static readonly Dictionary<string, string> AlertColors = new(StringComparer.Ordinal)
    {
        ["markdown-alert-note"] = "0969DA",
        ["markdown-alert-tip"] = "1A7F37",
        ["markdown-alert-important"] = "8250DF",
        ["markdown-alert-warning"] = "9A6700",
        ["markdown-alert-caution"] = "CF222E",
    };

    private static Format Apply(string name, Format format) => name switch
    {
        "strong" or "b" => format with { Bold = true },
        "em" or "i" or "cite" or "dfn" or "var" => format with { Italic = true },
        "del" or "s" or "strike" => format with { Strike = true },
        "ins" or "u" => format with { Underline = true },
        "code" or "kbd" or "samp" or "tt" => format with { Code = true },
        "sup" => format with { Position = VerticalPositionValues.Superscript },
        "sub" => format with { Position = VerticalPositionValues.Subscript },
        "mark" => format with { Mark = true },
        _ => format,
    };

    private void Link(IElement element, Flow flow, Format format)
    {
        if (element.ClassList.Contains("footnote-back-ref"))
        {
            return;
        }

        var href = element.GetAttribute("href");
        if (element.ClassList.Contains("footnote-ref") && Footnote(href ?? "", flow))
        {
            return;
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeMailto))
        {
            var relationship = flow.Layout.Part.AddHyperlinkRelationship(uri, isExternal: true);
            var outer = flow.Link;
            flow.Link = new Hyperlink { Id = relationship.Id };
            Children(element, flow, format with { Link = true });
            flow.Link = outer;
            return;
        }

        // Links to other files and to places in this one have nowhere to go in Word.
        Children(element, flow, format);
    }

    /// <summary>Adds the footnote a <c>#fn:…</c> link points to, the first time it is referred to.</summary>
    private bool Footnote(string href, Flow flow)
    {
        if (!_footnoteItems.Remove(href, out var item))
        {
            return false;
        }

        if (_footnotes is null)
        {
            _footnotes = main.AddNewPart<FootnotesPart>();
            _footnotes.Footnotes = new Footnotes(
                new Footnote(new Paragraph(new Run(new SeparatorMark()))) { Type = FootnoteEndnoteValues.Separator, Id = -1 },
                new Footnote(new Paragraph(new Run(new ContinuationSeparatorMark()))) { Type = FootnoteEndnoteValues.ContinuationSeparator, Id = 0 });
        }

        var number = ++_footnoteCount;
        var footnote = new Footnote { Id = number };
        Blocks(item, footnote, new Layout(_footnotes, Style: "FootnoteText"), default);
        var first = footnote.GetFirstChild<Paragraph>();
        if (first is null)
        {
            first = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "FootnoteText" }));
            footnote.PrependChild(first);
        }

        first.ParagraphProperties!.InsertAfterSelf(new Run(
            new RunProperties(new RunStyle { Val = "FootnoteReference" }), new FootnoteReferenceMark(), new Text(" ") { Space = SpaceProcessingModeValues.Preserve }));
        _footnotes.Footnotes!.Append(footnote);

        flow.Add(new Run(new RunProperties(new RunStyle { Val = "FootnoteReference" }), new FootnoteReference { Id = number }), space: false);
        return true;
    }

    private void List(IElement list, OpenXmlCompositeElement target, Layout layout, Format format)
    {
        var level = Math.Min(layout.ListLevel + 1, 8);
        int numberId;
        if (list.LocalName == "ol")
        {
            numberId = _numbered.Count + 2;
            var start = int.TryParse(list.GetAttribute("start"), out var value) ? value : 1;
            _numbered.Add(new NumberingInstance(
                new AbstractNumId { Val = DocxParts.NumberedList },
                new LevelOverride(new StartOverrideNumberingValue { Val = start }) { LevelIndex = level }) { NumberID = numberId });
        }
        else
        {
            numberId = _bullets ??= AddBullets();
        }

        var itemLayout = layout with { ListLevel = level };
        foreach (var child in list.ChildNodes)
        {
            if (child is IElement { LocalName: "li" } item)
            {
                // A task's checkbox stands in for its bullet or number, as on GitHub.
                Blocks(item, target, itemLayout, format, item.ClassList.Contains("task-list-item")
                    ? null
                    : new NumberingProperties(new NumberingLevelReference { Val = level }, new NumberingId { Val = numberId }));
            }
            else
            {
                var flow = new Flow(target, itemLayout, null);
                Node(child, flow, format);
                flow.Break();
            }
        }
    }

    private int AddBullets()
    {
        _numbered.Add(new NumberingInstance(new AbstractNumId { Val = DocxParts.BulletList }) { NumberID = 1 });
        return 1;
    }

    private void CodeBlock(IElement pre, OpenXmlCompositeElement target, Layout layout)
    {
        var text = pre.TextContent.Replace("\r\n", "\n");
        if (text.EndsWith('\n'))
        {
            text = text[..^1];
        }

        Paragraph? last = null;
        foreach (var line in text.Split('\n'))
        {
            last = NewParagraph(target, layout with { Style = "CodeBlock" }, null);
            var run = new Run();
            var pieces = line.Split('\t');
            for (var i = 0; i < pieces.Length; i++)
            {
                if (i > 0)
                {
                    run.Append(new TabChar());
                }

                if (pieces[i].Length > 0)
                {
                    run.Append(new Text(pieces[i]) { Space = SpaceProcessingModeValues.Preserve });
                }
            }

            last.Append(run);
        }

        // Space after the block, as after a paragraph.
        last!.ParagraphProperties!.SpacingBetweenLines = new SpacingBetweenLines { After = layout.Compact ? "0" : "160" };
    }

    private void Table(IElement table, OpenXmlCompositeElement target, Layout layout)
    {
        var rows = new List<(IElement Row, bool Header)>();
        foreach (var child in table.Children)
        {
            switch (child.LocalName)
            {
                case "thead" or "tbody" or "tfoot":
                    rows.AddRange(child.Children.Where(row => row.LocalName == "tr").Select(row => (row, child.LocalName == "thead")));
                    break;
                case "caption":
                    Blocks(child, target, layout, new Format { Italic = true });
                    break;
            }
        }

        var cells = rows.Select(row => row.Row.Children.Where(cell => cell.LocalName is "td" or "th").ToList()).ToList();
        var columns = cells.Select(row => row.Sum(Span)).DefaultIfEmpty(0).Max();
        if (columns == 0)
        {
            return;
        }

        var properties = new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Auto, Width = "0" },
            new TableBorders(
                Border(new TopBorder()), Border(new LeftBorder()), Border(new BottomBorder()), Border(new RightBorder()),
                Border(new InsideHorizontalBorder()), Border(new InsideVerticalBorder())),
            new TableLayout { Type = TableLayoutValues.Autofit });
        var indent = Indentation(layout);
        if (indent > 0)
        {
            properties.TableIndentation = new TableIndentation { Width = indent, Type = TableWidthUnitValues.Dxa };
        }

        var word = new Table(properties, new TableGrid(Enumerable.Range(0, columns).Select(_ => new GridColumn { Width = (9000 / columns).ToString() })));
        for (var i = 0; i < rows.Count; i++)
        {
            var row = new TableRow();
            if (rows[i].Header)
            {
                row.Append(new TableRowProperties(new TableHeader()));
            }

            foreach (var cell in cells[i])
            {
                var header = rows[i].Header || cell.LocalName == "th";
                var wordCell = Cell(Span(cell), header);
                Blocks(cell, wordCell, new Layout(layout.Part, Align: Alignment(cell), Compact: true), new Format { Bold = header });
                if (wordCell.LastChild is not Paragraph)
                {
                    wordCell.Append(new Paragraph());
                }

                row.Append(wordCell);
            }

            // Word needs every row to fill the grid.
            var used = cells[i].Sum(Span);
            if (used < columns)
            {
                var filler = Cell(columns - used, rows[i].Header);
                filler.Append(new Paragraph());
                row.Append(filler);
            }

            word.Append(row);
        }

        target.Append(word);
    }

    private static TableCell Cell(int span, bool header)
    {
        var properties = new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Auto, Width = "0" });
        if (span > 1)
        {
            properties.GridSpan = new GridSpan { Val = span };
        }

        if (header)
        {
            properties.Shading = new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = DocxParts.CodeFill };
        }

        return new TableCell(properties);
    }

    private static T Border<T>(T border) where T : BorderType
    {
        border.Val = BorderValues.Single;
        border.Size = 4;
        border.Color = DocxParts.BorderColor;
        return border;
    }

    private static int Span(IElement cell) =>
        int.TryParse(cell.GetAttribute("colspan"), out var span) && span > 1 ? Math.Min(span, 63) : 1;

    private static JustificationValues? Alignment(IElement cell)
    {
        var style = TextAlign().Match(cell.GetAttribute("style") ?? "");
        var align = style.Success ? style.Groups[1].Value : cell.GetAttribute("align");
        return align?.ToLowerInvariant() switch
        {
            "center" => JustificationValues.Center,
            "right" => JustificationValues.Right,
            _ => null,
        };
    }

    private void Image(IElement element, Flow flow, Format format)
    {
        var source = element.GetAttribute("src");
        var alt = element.GetAttribute("alt") ?? "";
        if (source is null || images.GetValueOrDefault(source) is not { } image)
        {
            // Shown in its place, as a browser does for an image it can't show.
            flow.Text(alt, format with { Italic = true });
            return;
        }

        var part = flow.Layout.Part;
        if (!_imageParts.TryGetValue((part, source), out var relationship))
        {
            var imagePart = part.AddNewPart<ImagePart>(image.ContentType);
            using (var bytes = new MemoryStream(image.Bytes))
            {
                imagePart.FeedData(bytes);
            }

            relationship = part.GetIdOfPart(imagePart);
            _imageParts[(part, source)] = relationship;
        }

        var (width, height) = Size(image, element);
        var id = ++_drawingCount;
        var cx = width * EmusPerPixel;
        var cy = height * EmusPerPixel;
        var picture = new PIC.Picture(
            new PIC.NonVisualPictureProperties(
                new PIC.NonVisualDrawingProperties { Id = 0U, Name = $"Picture {id}", Description = alt },
                new PIC.NonVisualPictureDrawingProperties()),
            new PIC.BlipFill(new A.Blip { Embed = relationship }, new A.Stretch(new A.FillRectangle())),
            new PIC.ShapeProperties(
                new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
        var inline = new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
            new DW.DocProperties { Id = id, Name = $"Picture {id}", Description = alt },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(new A.GraphicData(picture) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        {
            DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U,
        };
        flow.Add(new Run(new Drawing(inline)), space: false);
    }

    /// <summary>The image's own size, or the width and height the HTML gives it, no wider than the page.</summary>
    private static (long Width, long Height) Size(WordImage image, IElement element)
    {
        double width = image.Width;
        double height = image.Height;
        var givenWidth = Pixels(element.GetAttribute("width"));
        var givenHeight = Pixels(element.GetAttribute("height"));
        if (givenWidth is { } w)
        {
            height = givenHeight ?? height * w / width;
            width = w;
        }
        else if (givenHeight is { } h)
        {
            width = width * h / height;
            height = h;
        }

        if (width > MaxImageWidth)
        {
            height = height * MaxImageWidth / width;
            width = MaxImageWidth;
        }

        return (Math.Max(1, (long)Math.Round(width)), Math.Max(1, (long)Math.Round(height)));
    }

    private static int? Pixels(string? value)
    {
        var match = PixelSize().Match(value ?? "");
        var pixels = match.Success ? int.Parse(match.Groups[1].Value) : 0;
        return pixels is > 0 and <= ImageInfo.MaxSide ? pixels : null;
    }

    private static Run Run(string text, Format format)
    {
        var run = new Run();
        if (Properties(format) is { } properties)
        {
            run.Append(properties);
        }

        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static RunProperties? Properties(Format format)
    {
        var properties = new RunProperties();
        if (format.Code || format.Link)
        {
            properties.RunStyle = new RunStyle { Val = format.Code ? "InlineCode" : "Hyperlink" };
        }

        if (format.Bold)
        {
            properties.Bold = new Bold();
        }

        if (format.Italic)
        {
            properties.Italic = new Italic();
        }

        if (format.Strike)
        {
            properties.Strike = new Strike();
        }

        if (format.Code && format.Link)
        {
            // Code in a link keeps the link's look.
            properties.Color = new Color { Val = DocxParts.LinkColor };
        }

        if (format.Underline || (format.Code && format.Link))
        {
            properties.Underline = new Underline { Val = UnderlineValues.Single };
        }

        if (format.Mark)
        {
            properties.Highlight = new Highlight { Val = HighlightColorValues.Yellow };
        }

        if (format.Position is { } position)
        {
            properties.VerticalTextAlignment = new VerticalTextAlignment { Val = position };
        }

        return properties.HasChildren ? properties : null;
    }

    private static int Indentation(Layout layout) =>
        layout.Quote * DocxParts.QuoteIndent
        + (layout.ListLevel >= 0 ? DocxParts.ListIndent * (layout.ListLevel + 1) : 0)
        + layout.Indent * DocxParts.ListIndent;

    private static Paragraph NewParagraph(OpenXmlCompositeElement target, Layout layout, NumberingProperties? numbering)
    {
        var properties = new ParagraphProperties();
        if (layout.Style is not null)
        {
            properties.ParagraphStyleId = new ParagraphStyleId { Val = layout.Style };
        }

        if (numbering is not null)
        {
            properties.NumberingProperties = numbering;
        }

        if (layout.Quote > 0)
        {
            properties.ParagraphBorders = new ParagraphBorders(
                new LeftBorder { Val = BorderValues.Single, Size = 18, Space = 8, Color = layout.QuoteColor });
        }

        if (layout.Compact)
        {
            properties.SpacingBetweenLines = new SpacingBetweenLines { After = "0" };
        }

        var left = Indentation(layout);
        if (left > 0)
        {
            properties.Indentation = new Indentation { Left = left.ToString() };
            if (numbering is not null)
            {
                properties.Indentation.Hanging = DocxParts.Hanging.ToString();
            }
        }

        if (layout.Align is { } align)
        {
            properties.Justification = new Justification { Val = align };
        }

        var paragraph = new Paragraph(properties);
        target.Append(paragraph);
        return paragraph;
    }

    [GeneratedRegex(@"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase)]
    private static partial Regex TextAlign();

    [GeneratedRegex(@"^\s*(\d{1,6})\s*(?:px)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PixelSize();

    /// <summary>Where paragraphs go and how they are set out.</summary>
    /// <param name="Part">The part they belong to, which holds their images and links.</param>
    /// <param name="Quote">How many quotes or alerts they are in.</param>
    /// <param name="ListLevel">The list level they are in, or -1 outside lists.</param>
    /// <param name="Indent">Further indentation, for definitions.</param>
    /// <param name="Compact">No space after paragraphs, in table cells.</param>
    private sealed record Layout(
        OpenXmlPart Part, string? Style = null, int Quote = 0, int ListLevel = -1, int Indent = 0,
        JustificationValues? Align = null, bool Compact = false, string QuoteColor = DocxParts.BorderColor);

    private readonly record struct Format(
        bool Bold, bool Italic, bool Strike, bool Underline, bool Code, bool Mark, bool Link,
        VerticalPositionValues? Position);

    /// <summary>
    /// Fills paragraphs with inline content in one container, starting a paragraph when content
    /// arrives and collapsing white space as HTML does. The first paragraph gets the list
    /// numbering, if any; later ones line up with it.
    /// </summary>
    private sealed class Flow(OpenXmlCompositeElement target, Layout layout, NumberingProperties? numbering)
    {
        private Paragraph? _paragraph;
        private bool _space = true;
        private NumberingProperties? _numbering = numbering;

        public OpenXmlCompositeElement Target => target;

        public Layout Layout => layout;

        /// <summary>The link runs go into, if they are in one.</summary>
        public Hyperlink? Link { get; set; }

        public void Text(string text, Format format)
        {
            var collapsed = new StringBuilder();
            foreach (var c in text)
            {
                if (c is ' ' or '\t' or '\n' or '\r' or '\f')
                {
                    if (!_space)
                    {
                        collapsed.Append(' ');
                        _space = true;
                    }
                }
                else
                {
                    collapsed.Append(c);
                    _space = false;
                }
            }

            if (collapsed.Length > 0)
            {
                Append(DocxWriter.Run(collapsed.ToString(), format));
            }
        }

        /// <summary>Adds a run that isn't text; <paramref name="space"/> says whether white space after it is dropped.</summary>
        public void Add(Run run, bool space)
        {
            Append(run);
            _space = space;
        }

        /// <summary>The numbering the next paragraph would get, for a block that starts the list item.</summary>
        public NumberingProperties? TakeNumbering()
        {
            var taken = _numbering;
            _numbering = null;
            return taken;
        }

        /// <summary>Ends the paragraph, without the space it may end with.</summary>
        public void Break()
        {
            if (_paragraph?.Descendants<Run>().Last().LastChild is Text last)
            {
                last.Text = last.Text.TrimEnd(' ');
            }

            _paragraph = null;
            Link = null;
            _space = true;
        }

        private void Append(Run run)
        {
            if (_paragraph is null)
            {
                _paragraph = NewParagraph(target, layout, _numbering);
                _numbering = null;
            }

            if (Link is null)
            {
                _paragraph.Append(run);
                return;
            }

            if (Link.Parent is null)
            {
                _paragraph.Append(Link);
            }

            Link.Append(run);
        }
    }
}
