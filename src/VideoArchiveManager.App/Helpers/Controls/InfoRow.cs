using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VideoArchiveManager.App.Helpers.Controls;

/// <summary>
/// Single label / value row used by the clip-info popup.
///
/// <para>
/// Renders as <c>[label, tertiary tone] [value, primary tone] [copy button on hover]</c>.
/// The copy button copies <see cref="Value"/> to the clipboard; it's only visible while
/// the row is hovered, so the popup reads as quiet reference data when idle and reveals
/// affordances on intent. Set <see cref="IsMono"/> to render the value in the
/// <c>App.FontFamily.Mono</c> stack — useful for paths, GPS coords, raw byte counts.
/// </para>
/// <para>
/// The default template lives in <c>Resources/Components/InfoRow.xaml</c> and is keyed by
/// type, mirroring the <see cref="EmptyState"/> pattern used elsewhere in the app.
/// </para>
/// </summary>
public class InfoRow : Control
{
    static InfoRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(InfoRow),
            new FrameworkPropertyMetadata(typeof(InfoRow)));
    }

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(
            nameof(Label),
            typeof(string),
            typeof(InfoRow),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(string),
            typeof(InfoRow),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsMonoProperty =
        DependencyProperty.Register(
            nameof(IsMono),
            typeof(bool),
            typeof(InfoRow),
            new PropertyMetadata(false));

    // Optional smaller, tertiary line shown under the value. Used for raw
    // bytes under a humanised file size, full timestamps under "5 minutes
    // ago", and similar second-row context.
    public static readonly DependencyProperty SubValueProperty =
        DependencyProperty.Register(
            nameof(SubValue),
            typeof(string),
            typeof(InfoRow),
            new PropertyMetadata(null));

    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Value
    {
        get => (string?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsMono
    {
        get => (bool)GetValue(IsMonoProperty);
        set => SetValue(IsMonoProperty, value);
    }

    public string? SubValue
    {
        get => (string?)GetValue(SubValueProperty);
        set => SetValue(SubValueProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_CopyButton") is Button btn)
        {
            btn.Click -= CopyButton_Click;
            btn.Click += CopyButton_Click;
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var v = Value;
        if (string.IsNullOrEmpty(v)) return;
        try
        {
            Clipboard.SetText(v);
        }
        catch
        {
            // Clipboard access can intermittently fail on locked-down
            // sessions / RDP / remote desktops; ignore silently rather
            // than tearing down the popup with an unhandled exception.
        }
    }
}
