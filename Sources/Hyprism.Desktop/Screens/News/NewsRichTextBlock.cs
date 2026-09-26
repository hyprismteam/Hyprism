// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Hyprism.Desktop.Platform;

namespace Hyprism.Desktop.Screens.News;

/// <summary>Renders the safe inline subset produced by <see cref="NewsContentNode"/>.</summary>
public sealed class NewsRichTextBlock : TextBlock
{
    private static readonly Cursor LinkCursor = new(StandardCursorType.Hand);
    private static readonly FontFamily CodeFontFamily =
        new("avares://Hyprism.Desktop/Assets/Fonts#JetBrains Mono");

    private readonly List<LinkInline> _links = [];
    private LinkInline? _hoveredLink;
    private LinkInline? _pressedLink;
    private int _textPosition;
    private bool _rebuildScheduled;
    private int _rebuildGeneration;
    private Color LinkColor => ThemeColor("ArticleLinkBrush", Color.Parse("#8AB8EF"));
    private Color LinkHoverColor => ThemeColor("ArticleLinkHoverBrush", Color.Parse("#B2D0F5"));

    public NewsRichTextBlock()
    {
        ActualThemeVariantChanged += (_, _) => ScheduleRebuild();
    }

    public static readonly StyledProperty<IReadOnlyList<NewsContentNode>?> NodesProperty =
        AvaloniaProperty.Register<NewsRichTextBlock, IReadOnlyList<NewsContentNode>?>(nameof(Nodes));

    public static readonly StyledProperty<IReadOnlyList<NewsInlineImageViewModel>?> InlineImagesProperty =
        AvaloniaProperty.Register<NewsRichTextBlock, IReadOnlyList<NewsInlineImageViewModel>?>(
            nameof(InlineImages));

    public static readonly StyledProperty<ICommand?> LinkCommandProperty =
        AvaloniaProperty.Register<NewsRichTextBlock, ICommand?>(nameof(LinkCommand));

    static NewsRichTextBlock()
    {
        NodesProperty.Changed.AddClassHandler<NewsRichTextBlock>((control, _) => control.ScheduleRebuild());
        InlineImagesProperty.Changed.AddClassHandler<NewsRichTextBlock>((control, _) => control.ScheduleRebuild());
        LinkCommandProperty.Changed.AddClassHandler<NewsRichTextBlock>((control, _) => control.ScheduleRebuild());
    }

    public IReadOnlyList<NewsContentNode>? Nodes
    {
        get => GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public IReadOnlyList<NewsInlineImageViewModel>? InlineImages
    {
        get => GetValue(InlineImagesProperty);
        set => SetValue(InlineImagesProperty, value);
    }

    public ICommand? LinkCommand
    {
        get => GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    private void ScheduleRebuild()
    {
        if (_rebuildScheduled)
            return;

        _rebuildScheduled = true;
        var generation = _rebuildGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _rebuildGeneration)
                return;

            _rebuildScheduled = false;
            Rebuild();
        }, DispatcherPriority.Background);
    }

    internal void RefreshThemeColors() => ScheduleRebuild();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _rebuildGeneration++;
        _rebuildScheduled = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void Rebuild()
    {
        SetHoveredLink(null);
        _links.Clear();
        _textPosition = 0;
        var inlines = new InlineCollection();
        if (Nodes is not null)
        {
            foreach (var node in Nodes)
                AppendNode(inlines, node, InlineImages);
        }

        while (inlines.Count > 0 && inlines[^1] is LineBreak)
            inlines.RemoveAt(inlines.Count - 1);

        Inlines = inlines;
    }

    private void AppendNode(
        InlineCollection target,
        NewsContentNode node,
        IReadOnlyList<NewsInlineImageViewModel>? inlineImages)
    {
        switch (node.Kind)
        {
            case "text":
                if (!string.IsNullOrEmpty(node.Text))
                {
                    target.Add(new Run(node.Text));
                    _textPosition += node.Text.Length;
                }
                return;
            case "line-break":
                target.Add(new LineBreak());
                _textPosition++;
                return;
            case "inline-image":
                _textPosition += AppendInlineImage(target, node, inlineImages);
                return;
            case "bold":
            {
                var text = ExtractText(node);
                target.Add(new Run(text)
                {
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(ThemeColor("TextPrimaryBrush", Color.Parse("#F2F2F4")))
                });
                _textPosition += text.Length;
                return;
            }
            case "italic":
            {
                var text = ExtractText(node);
                target.Add(new Run(text)
                {
                    FontStyle = FontStyle.Italic,
                    Foreground = new SolidColorBrush(ThemeColor("ArticleBodyBrush", Color.Parse("#E1E2E5")))
                });
                _textPosition += text.Length;
                return;
            }
            case "inline-code":
            {
                var text = node.Text ?? ExtractText(node);
                var codeText = new TextBlock
                {
                    Text = text,
                    FontFamily = CodeFontFamily,
                    FontSize = 13,
                    LineHeight = 18,
                    Foreground = new SolidColorBrush(ThemeColor("ArticleCodeForegroundBrush", Color.Parse("#BCD6F6"))),
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                var codeChip = new Border
                {
                    Background = new SolidColorBrush(ThemeColor("ArticleInlineCodeBrush", Color.Parse("#2E3584E4"))),
                    BorderBrush = new SolidColorBrush(ThemeColor("ArticleInlineCodeBorderBrush", Color.Parse("#803584E4"))),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(4, 0),
                    Margin = new Thickness(2, 0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Child = codeText
                };
                codeChip.Classes.Add("inlineCode");
                target.Add(new InlineUIContainer(codeChip)
                {
                    BaselineAlignment = BaselineAlignment.Center
                });
                // Inline UI containers occupy one position in TextLayout. Keeping
                // the logical index in sync preserves link hit-testing after code.
                _textPosition++;
                return;
            }
            case "link":
                AppendLink(target, node);
                return;
        }

        var isBlock = node.Kind is "paragraph" or "heading" or "list-item";
        foreach (var child in node.Children)
            AppendNode(target, child, inlineImages);
        if (isBlock)
        {
            target.Add(new LineBreak());
            _textPosition++;
        }
    }

    private void AppendLink(InlineCollection target, NewsContentNode node)
    {
        var label = ExtractText(node);
        if (string.IsNullOrEmpty(label))
            label = node.Url ?? string.Empty;

        var foreground = new SolidColorBrush(LinkColor)
        {
            Transitions = new Transitions
            {
                new ColorTransition
                {
                    Property = SolidColorBrush.ColorProperty,
                    Duration = TimeSpan.FromMilliseconds(160)
                }
            }
        };
        var underline = new SolidColorBrush(LinkHoverColor)
        {
            Opacity = 0,
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Brush.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(160)
                }
            }
        };
        var run = new Run(label)
        {
            FontWeight = FontWeight.SemiBold,
            Foreground = foreground,
            TextDecorations = new TextDecorationCollection
            {
                new TextDecoration
                {
                    Location = TextDecorationLocation.Underline,
                    Stroke = underline,
                    StrokeThickness = 1
                }
            }
        };
        target.Add(run);

        if (!string.IsNullOrWhiteSpace(node.Url))
            _links.Add(new LinkInline(_textPosition, label.Length, node.Url, foreground, underline));
        _textPosition += label.Length;
    }

    private static int AppendInlineImage(
        InlineCollection target,
        NewsContentNode node,
        IReadOnlyList<NewsInlineImageViewModel>? inlineImages)
    {
        var media = inlineImages?.FirstOrDefault(candidate =>
            string.Equals(candidate.Url, node.ImageUrl, StringComparison.OrdinalIgnoreCase));
        if (media is null)
        {
            if (!string.IsNullOrWhiteSpace(node.AltText))
            {
                target.Add(new Run(node.AltText));
                return node.AltText.Length;
            }

            return 0;
        }

        var image = new Image
        {
            Width = media.DisplaySize,
            Height = media.DisplaySize,
            Stretch = Stretch.Uniform,
            Margin = media.IsSticker ? new Thickness(2, 0, 8, 0) : new Thickness(2, 0)
        };
        image.Bind(
            Image.SourceProperty,
            new Binding(nameof(NewsInlineImageViewModel.Image)) { Source = media });

        target.Add(new InlineUIContainer(image)
        {
            BaselineAlignment = BaselineAlignment.Center
        });
        return 1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetHoveredLink(FindLink(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pressedLink = null;
        SetHoveredLink(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind !=
            PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        _pressedLink = FindLink(e.GetPosition(this));
        if (_pressedLink is not null)
            e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind !=
            PointerUpdateKind.LeftButtonReleased)
        {
            return;
        }

        var releasedLink = FindLink(e.GetPosition(this));
        if (_pressedLink is not null && ReferenceEquals(_pressedLink, releasedLink) &&
            LinkCommand?.CanExecute(releasedLink.Url) == true)
        {
            LinkCommand.Execute(releasedLink.Url);
            e.Handled = true;
        }

        _pressedLink = null;
    }

    private LinkInline? FindLink(Point point)
    {
        if (_links.Count == 0 || point.X < Padding.Left || point.Y < Padding.Top)
            return null;

        var layoutPoint = new Point(
            point.X - Padding.Left,
            point.Y - Padding.Top);
        foreach (var link in _links)
        {
            foreach (var bounds in TextLayout.HitTestTextRange(link.Start, link.Length))
            {
                var hitArea = new Rect(
                    bounds.X - 1,
                    bounds.Y - 2,
                    bounds.Width + 2,
                    bounds.Height + 4);
                if (hitArea.Contains(layoutPoint))
                    return link;
            }
        }

        return null;
    }

    private void SetHoveredLink(LinkInline? link)
    {
        if (ReferenceEquals(_hoveredLink, link))
            return;

        if (_hoveredLink is not null)
        {
            _hoveredLink.Foreground.Color = LinkColor;
            _hoveredLink.Underline.Opacity = 0;
        }

        _hoveredLink = link;
        Cursor = link is null ? null : LinkCursor;

        if (link is not null)
        {
            link.Foreground.Color = LinkHoverColor;
            link.Underline.Opacity = 1;
        }
    }

    private Color ThemeColor(string key, Color fallback)
        => this.TryFindResource(key, ActualThemeVariant, out var resource) &&
           resource is SolidColorBrush brush
            ? brush.Color
            : fallback;

    private static string ExtractText(NewsContentNode node)
    {
        if (!string.IsNullOrEmpty(node.Text))
            return node.Text;
        return string.Concat(node.Children.Select(ExtractText));
    }

    private sealed record LinkInline(
        int Start,
        int Length,
        string Url,
        SolidColorBrush Foreground,
        SolidColorBrush Underline);

}
