using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VideoArchiveManager.App.Helpers.Controls;

/// <summary>
/// Centered glyph + headline + subtext + optional CTA, used wherever a
/// content area would otherwise be visually empty (no selection, no
/// search results, no root folders configured yet).
///
/// <para>
/// The default template lives in <c>Resources/Components/EmptyState.xaml</c>
/// and is keyed by type. Setting <see cref="CtaText"/> or <see cref="CtaCommand"/>
/// auto-shows the CTA button (driven by <see cref="HasCta"/>). When neither
/// is set the control collapses gracefully into just the glyph + text block.
/// </para>
/// </summary>
public class EmptyState : Control
{
    static EmptyState()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(EmptyState),
            new FrameworkPropertyMetadata(typeof(EmptyState)));
    }

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(
            nameof(Glyph),
            typeof(string),
            typeof(EmptyState),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HeadlineProperty =
        DependencyProperty.Register(
            nameof(Headline),
            typeof(string),
            typeof(EmptyState),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SubtextProperty =
        DependencyProperty.Register(
            nameof(Subtext),
            typeof(string),
            typeof(EmptyState),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CtaTextProperty =
        DependencyProperty.Register(
            nameof(CtaText),
            typeof(string),
            typeof(EmptyState),
            new PropertyMetadata(null, OnCtaChanged));

    public static readonly DependencyProperty CtaIconProperty =
        DependencyProperty.Register(
            nameof(CtaIcon),
            typeof(string),
            typeof(EmptyState),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CtaCommandProperty =
        DependencyProperty.Register(
            nameof(CtaCommand),
            typeof(ICommand),
            typeof(EmptyState),
            new PropertyMetadata(null, OnCtaChanged));

    public static readonly DependencyProperty CtaCommandParameterProperty =
        DependencyProperty.Register(
            nameof(CtaCommandParameter),
            typeof(object),
            typeof(EmptyState),
            new PropertyMetadata(null));

    private static readonly DependencyPropertyKey HasCtaPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasCta),
            typeof(bool),
            typeof(EmptyState),
            new PropertyMetadata(false));

    public static readonly DependencyProperty HasCtaProperty = HasCtaPropertyKey.DependencyProperty;

    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string? Headline
    {
        get => (string?)GetValue(HeadlineProperty);
        set => SetValue(HeadlineProperty, value);
    }

    public string? Subtext
    {
        get => (string?)GetValue(SubtextProperty);
        set => SetValue(SubtextProperty, value);
    }

    public string? CtaText
    {
        get => (string?)GetValue(CtaTextProperty);
        set => SetValue(CtaTextProperty, value);
    }

    public string? CtaIcon
    {
        get => (string?)GetValue(CtaIconProperty);
        set => SetValue(CtaIconProperty, value);
    }

    public ICommand? CtaCommand
    {
        get => (ICommand?)GetValue(CtaCommandProperty);
        set => SetValue(CtaCommandProperty, value);
    }

    public object? CtaCommandParameter
    {
        get => GetValue(CtaCommandParameterProperty);
        set => SetValue(CtaCommandParameterProperty, value);
    }

    public bool HasCta => (bool)GetValue(HasCtaProperty);

    private static void OnCtaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not EmptyState self) return;
        var has = !string.IsNullOrEmpty(self.CtaText) && self.CtaCommand is not null;
        self.SetValue(HasCtaPropertyKey, has);
    }
}
