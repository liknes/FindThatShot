// Find That Shot - organize and search a large local video archive.
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Ingve Moss Liknes <findthatshot@ingve.no>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
using System.Reflection;
using System.Windows;
using System.Windows.Media.Animation;
using VideoArchiveManager.App.Localization;

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
        return v is null
            ? LocalizationManager.Instance["Splash_VersionUnknown"]
            : LocalizationManager.Instance.Format("Splash_VersionFormat", v.Major, v.Minor, v.Build);
    }
}
