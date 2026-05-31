using System.Windows;
using System.Windows.Controls;

namespace VideoArchiveManager.App.Helpers.Controls;

/// <summary>
/// Lays children out in a uniform grid whose column count is derived from the
/// available width and <see cref="MinItemWidth"/>. Every cell in a row gets the
/// same width so each row exactly fills the panel; the last partially-filled
/// row left-aligns. Row heights adapt to the tallest child in that row.
/// Used as the catalog thumbnail grid so cards always fill the area between
/// the sidebars regardless of window size.
/// </summary>
public class AdaptiveWrapPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty =
        DependencyProperty.Register(
            nameof(MinItemWidth),
            typeof(double),
            typeof(AdaptiveWrapPanel),
            new FrameworkPropertyMetadata(
                240.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    private int ComputeColumns(double availableWidth)
    {
        double minW = Math.Max(1.0, MinItemWidth);
        if (double.IsInfinity(availableWidth) || double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            return 1;
        }
        return Math.Max(1, (int)Math.Floor(availableWidth / minW));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double availWidth = availableSize.Width;
        // When hosted inside a ScrollViewer with horizontal scroll disabled the
        // panel still receives a finite Width (the viewport). When measure is
        // unconstrained (rare here) fall back to MinItemWidth so we don't
        // collapse to zero columns.
        double effectiveWidth =
            (double.IsInfinity(availWidth) || double.IsNaN(availWidth) || availWidth <= 0)
                ? Math.Max(1.0, MinItemWidth)
                : availWidth;

        int columns = ComputeColumns(effectiveWidth);
        double itemWidth = effectiveWidth / columns;

        var childSize = new Size(itemWidth, double.PositiveInfinity);
        var children = InternalChildren;
        int count = children.Count;

        double totalHeight = 0;
        for (int rowStart = 0; rowStart < count; rowStart += columns)
        {
            int rowEnd = Math.Min(rowStart + columns, count);
            double rowHeight = 0;
            for (int i = rowStart; i < rowEnd; i++)
            {
                children[i].Measure(childSize);
                rowHeight = Math.Max(rowHeight, children[i].DesiredSize.Height);
            }
            totalHeight += rowHeight;
        }

        double reportedWidth = double.IsInfinity(availWidth) ? 0 : effectiveWidth;
        return new Size(reportedWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = ComputeColumns(finalSize.Width);
        double itemWidth = finalSize.Width / columns;

        var children = InternalChildren;
        int count = children.Count;

        double y = 0;
        for (int rowStart = 0; rowStart < count; rowStart += columns)
        {
            int rowEnd = Math.Min(rowStart + columns, count);
            double rowHeight = 0;
            // First pass: pick the tallest desired height in this row so every
            // card in a given row gets the same final height.
            for (int i = rowStart; i < rowEnd; i++)
            {
                rowHeight = Math.Max(rowHeight, children[i].DesiredSize.Height);
            }
            for (int i = rowStart; i < rowEnd; i++)
            {
                double x = (i - rowStart) * itemWidth;
                children[i].Arrange(new Rect(x, y, itemWidth, rowHeight));
            }
            y += rowHeight;
        }

        return finalSize;
    }
}
