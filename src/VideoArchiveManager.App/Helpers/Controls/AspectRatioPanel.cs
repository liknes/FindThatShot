using System.Windows;
using System.Windows.Controls;

namespace VideoArchiveManager.App.Helpers.Controls;

/// <summary>
/// Sizes a single child element to the largest rectangle of the given
/// <see cref="Ratio"/> that fits inside the available space, centered
/// horizontally and vertically. Any leftover space around the child is
/// transparent — typically backed by a solid-colour parent <c>Border</c>.
///
/// Used to host the in-app VLC <c>VideoView</c>: by sizing the host to the
/// video's actual aspect ratio, VLC fills 100% of its render surface and
/// can never produce letterbox bars. That avoids the entire problem class
/// of "VLC's DXGI swap chain is not cleared between frames so the unpainted
/// letterbox area inherits whatever was on the host window previously" —
/// usually the system default white brush, visible as bright bars above and
/// below the picture. With no letterbox the surface is fully owned by VLC's
/// video output, the surrounding pixels are owned by WPF (a black
/// <c>Border</c>), and there's no seam between the two for white to leak
/// through.
/// </summary>
public class AspectRatioPanel : Panel
{
    public static readonly DependencyProperty RatioProperty =
        DependencyProperty.Register(
            nameof(Ratio),
            typeof(double),
            typeof(AspectRatioPanel),
            new FrameworkPropertyMetadata(
                16.0 / 9.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>
    /// Width / height ratio of the child's render area. Defaults to 16:9.
    /// Bind this to the current video's native aspect ratio so the player
    /// area always matches the picture exactly. Non-finite or non-positive
    /// values are treated as the 16:9 default.
    /// </summary>
    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    private double EffectiveRatio
    {
        get
        {
            double r = Ratio;
            return double.IsFinite(r) && r > 0 ? r : 16.0 / 9.0;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0) return new Size(0, 0);

        // We always claim all the space the parent gave us. The child gets
        // the aspect-ratio-fitted box; the surrounding strip is left for the
        // parent's background to paint.
        double availW = availableSize.Width;
        double availH = availableSize.Height;
        if (double.IsInfinity(availW)) availW = 0;
        if (double.IsInfinity(availH)) availH = 0;

        var (childW, childH) = ComputeFit(availW, availH, EffectiveRatio);
        InternalChildren[0].Measure(new Size(childW, childH));

        return new Size(availW, availH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0) return finalSize;

        var (childW, childH) = ComputeFit(finalSize.Width, finalSize.Height, EffectiveRatio);
        double x = (finalSize.Width - childW) / 2.0;
        double y = (finalSize.Height - childH) / 2.0;

        InternalChildren[0].Arrange(new Rect(x, y, childW, childH));
        return finalSize;
    }

    private static (double width, double height) ComputeFit(double availW, double availH, double ratio)
    {
        if (availW <= 0 || availH <= 0 || ratio <= 0) return (0, 0);

        // Try fitting to the available width first; if that overflows the
        // available height, fall back to fitting by height.
        double byWidthH = availW / ratio;
        if (byWidthH <= availH) return (availW, byWidthH);
        return (availH * ratio, availH);
    }
}
