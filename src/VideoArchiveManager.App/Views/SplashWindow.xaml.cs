using System.Reflection;
using System.Windows;
using System.Windows.Media.Animation;

namespace VideoArchiveManager.App.Views;

/// <summary>
/// Borderless splash shown immediately on startup while heavy init runs
/// (DB migrate, DI, FFmpeg discovery). Calls <see cref="FadeOutAndClose"/>
/// once <see cref="MainWindow"/> reports <see cref="FrameworkElement.Loaded"/>;
/// dismissal is animated rather than abrupt so the handoff doesn't flash.
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = BuildVersionText();
    }

    public void FadeOutAndClose()
    {
        var fade = new DoubleAnimation
        {
            From = Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    private static string BuildVersionText()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "version unknown" : $"version {v.Major}.{v.Minor}.{v.Build}";
    }
}
