using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfApp = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;

namespace KeyNestForWin.Controls;

/// <summary>行左侧拖动手柄，用于调整条目在保管库中的顺序。</summary>
public sealed class RowReorderGrip : Border
{
    public static readonly DependencyProperty ItemIdProperty =
        DependencyProperty.Register(nameof(ItemId), typeof(Guid), typeof(RowReorderGrip));

    public Guid ItemId
    {
        get => (Guid)GetValue(ItemIdProperty);
        set => SetValue(ItemIdProperty, value);
    }

    public event EventHandler<RowReorderEventArgs>? ReorderTo;

    private System.Windows.Point? _start;
    private bool _dragging;

    public RowReorderGrip()
    {
        Cursor = System.Windows.Input.Cursors.SizeAll;
        ToolTip = "拖动以调整条目顺序";
        Background = System.Windows.Media.Brushes.Transparent;
        Child = BuildDots();
        PreviewMouseLeftButtonDown += OnMouseDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnMouseUp;
        MouseLeave += (_, _) => TryCancelDrag();
    }

    private static UIElement BuildDots()
    {
        var panel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Vertical,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2)
        };
        for (var i = 0; i < 3; i++)
        {
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            row.Children.Add(CreateDot());
            row.Children.Add(CreateDot());
            if (i < 2) row.Margin = new Thickness(0, 0, 0, 3);
            panel.Children.Add(row);
        }
        return panel;
    }

    private static Ellipse CreateDot()
    {
        var fill = WpfApp.Current.TryFindResource("Brush.Grip") as WpfBrush
                   ?? new SolidColorBrush(WpfColor.FromRgb(0x4B, 0x4B, 0x60));
        return new Ellipse
        {
            Width = 3,
            Height = 3,
            Margin = new Thickness(1.5, 0, 1.5, 0),
            Fill = fill
        };
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;
        _start = e.GetPosition(this);
        _dragging = false;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!IsMouseCaptured || _start == null) return;
        if (!_dragging && (e.GetPosition(this) - _start.Value).Length > 4)
            _dragging = true;
        if (_dragging)
            e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        if (_dragging)
        {
            ReorderTo?.Invoke(this, new RowReorderEventArgs(ItemId));
            e.Handled = true;
        }
        _start = null;
        _dragging = false;
    }

    private void TryCancelDrag()
    {
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        _start = null;
        _dragging = false;
    }
}

public sealed class RowReorderEventArgs(Guid sourceId) : EventArgs
{
    public Guid SourceId { get; } = sourceId;
}
