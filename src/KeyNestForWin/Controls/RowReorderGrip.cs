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
    private ListViewRowReorderSession? _session;

    public RowReorderGrip()
    {
        Cursor = System.Windows.Input.Cursors.SizeAll;
        ToolTip = "拖动以调整条目顺序";
        Background = System.Windows.Media.Brushes.Transparent;
        Child = BuildDots();
        PreviewMouseLeftButtonDown += OnMouseDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnMouseUp;
        MouseLeave += (_, _) =>
        {
            if (_session == null)
                TryCancelDrag();
        };
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
        if (!IsEnabled || _session != null) return;
        _start = e.GetPosition(this);
        _dragging = false;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_session != null)
        {
            e.Handled = true;
            return;
        }
        if (!IsMouseCaptured || _start == null) return;
        if (!_dragging && (e.GetPosition(this) - _start.Value).Length > 4)
        {
            _dragging = true;
            TryStartSession(e);
        }
        if (_dragging)
            e.Handled = true;
    }

    private void TryStartSession(System.Windows.Input.MouseEventArgs e)
    {
        var item = FindAncestor<System.Windows.Controls.ListViewItem>(this);
        var list = FindAncestor<System.Windows.Controls.ListView>(this);
        if (item == null || list == null) return;

        var grabOffset = e.GetPosition(item);
        _session = ListViewRowReorderSession.TryBegin(
            list,
            item,
            ItemId,
            grabOffset,
            args => ReorderTo?.Invoke(this, args),
            EndSession);
        if (_session == null) return;

        ReleaseMouseCapture();
        _start = null;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_session != null)
        {
            e.Handled = true;
            return;
        }
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        _start = null;
        _dragging = false;
    }

    private void EndSession()
    {
        _session = null;
        _dragging = false;
    }

    private void TryCancelDrag()
    {
        if (_session != null)
        {
            _session.Cancel();
            return;
        }
        if (!IsMouseCaptured) return;
        ReleaseMouseCapture();
        _start = null;
        _dragging = false;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}

public sealed class RowReorderEventArgs : EventArgs
{
    public Guid SourceId { get; }
    /// <summary>将源条目移动到该 Id 之前；与 <see cref="InsertAtEnd"/> 互斥。</summary>
    public Guid? InsertBeforeId { get; }
    public bool InsertAtEnd { get; }

    public RowReorderEventArgs(Guid sourceId, Guid? insertBeforeId, bool insertAtEnd = false)
    {
        SourceId = sourceId;
        InsertBeforeId = insertBeforeId;
        InsertAtEnd = insertAtEnd;
    }
}
