using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// A panel that arranges child elements horizontally and wraps to the next
/// line when the available width is exceeded. Each child uses its desired
/// width, supporting variable-width items.
/// </summary>
public sealed partial class WrapPanel : Panel
{
    /// <summary>Horizontal spacing between items in a row.</summary>
    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    /// <summary>Identifies the <see cref="HorizontalSpacing"/> dependency property.</summary>
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double),
            typeof(WrapPanel), new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    /// <summary>Vertical spacing between rows.</summary>
    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    /// <summary>Identifies the <see cref="VerticalSpacing"/> dependency property.</summary>
    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double),
            typeof(WrapPanel), new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    private static void OnLayoutPropertyChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((WrapPanel)d).InvalidateMeasure();
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var hSpacing = HorizontalSpacing;
        var vSpacing = VerticalSpacing;
        var maxWidth = availableSize.Width;

        double lineWidth = 0;
        double lineHeight = 0;
        double totalWidth = 0;
        double totalHeight = 0;
        var isFirstInLine = true;

        var childConstraint = new Size(availableSize.Width, double.PositiveInfinity);

        foreach (var child in Children)
        {
            child.Measure(childConstraint);
            var desired = child.DesiredSize;

            var addedWidth = isFirstInLine ? desired.Width : desired.Width + hSpacing;

            if (!isFirstInLine && lineWidth + addedWidth > maxWidth)
            {
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += lineHeight + vSpacing;
                lineWidth = desired.Width;
                lineHeight = desired.Height;
                isFirstInLine = false;
            }
            else
            {
                lineWidth += addedWidth;
                lineHeight = Math.Max(lineHeight, desired.Height);
                isFirstInLine = false;
            }
        }

        totalWidth = Math.Max(totalWidth, lineWidth);
        totalHeight += lineHeight;

        return new Size(totalWidth, totalHeight);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var hSpacing = HorizontalSpacing;
        var vSpacing = VerticalSpacing;
        var maxWidth = finalSize.Width;

        double x = 0;
        double y = 0;
        double lineHeight = 0;
        var isFirstInLine = true;

        foreach (var child in Children)
        {
            var desired = child.DesiredSize;
            var addedWidth = isFirstInLine ? desired.Width : desired.Width + hSpacing;

            if (!isFirstInLine && x + addedWidth > maxWidth)
            {
                x = 0;
                y += lineHeight + vSpacing;
                lineHeight = 0;
                isFirstInLine = true;
            }

            if (!isFirstInLine)
                x += hSpacing;

            child.Arrange(new Rect(x, y, desired.Width, desired.Height));
            x += desired.Width;
            lineHeight = Math.Max(lineHeight, desired.Height);
            isFirstInLine = false;
        }

        return finalSize;
    }
}
