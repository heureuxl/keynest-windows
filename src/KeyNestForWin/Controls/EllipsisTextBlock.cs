using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyNestForWin.Controls;

/// <summary>省略显示；文本被截断时悬停 2 秒后展示完整 ToolTip。</summary>
public sealed class EllipsisTextBlock : TextBlock
{
    public EllipsisTextBlock()
    {
        TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetInitialShowDelay(this, 2000);
        ToolTipService.SetShowDuration(this, 60000);
        SizeChanged += (_, _) => UpdateToolTip();
        Loaded += (_, _) => UpdateToolTip();
        DependencyPropertyDescriptor.FromProperty(TextProperty, typeof(TextBlock))
            .AddValueChanged(this, (_, _) => UpdateToolTip());
    }

    private void UpdateToolTip()
    {
        var text = Text ?? "";
        if (string.IsNullOrEmpty(text) || ActualWidth <= 0)
        {
            ToolTip = null;
            return;
        }

        if (IsTextTrimmed(text, ActualWidth))
            ToolTip = text;
        else
            ToolTip = null;
    }

    private bool IsTextTrimmed(string text, double availableWidth)
    {
        try
        {
            var ft = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection,
                new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
                FontSize,
                Foreground ?? System.Windows.Media.Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return ft.Width > availableWidth - 4;
        }
        catch
        {
            return text.Length > 12;
        }
    }
}
