using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace _1RM.Controls;

/// <summary>Equal-width columns with independently sized rows for desktop aspect ratios.</summary>
public sealed class DesktopPreviewPanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(DesktopPreviewPanel),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure,
            null, (_, value) => Math.Max(1, Math.Min(6, (int)value))));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? Columns * 284 : availableSize.Width;
        double cellWidth = Math.Max(0, width / Columns);
        double height = 0, rowHeight = 0;
        int column = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(cellWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++column == Columns) { height += rowHeight; rowHeight = 0; column = 0; }
        }
        return new Size(width, height + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToArray();
        double cellWidth = finalSize.Width / Columns, y = 0;
        for (int first = 0; first < children.Length; first += Columns)
        {
            int count = Math.Min(Columns, children.Length - first);
            double height = 0;
            for (int i = 0; i < count; i++) height = Math.Max(height, children[first + i].DesiredSize.Height);
            for (int i = 0; i < count; i++) children[first + i].Arrange(new Rect(i * cellWidth, y, cellWidth, height));
            y += height;
        }
        return finalSize;
    }
}
