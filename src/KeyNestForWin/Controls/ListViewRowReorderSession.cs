using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WpfApp = System.Windows.Application;
using WpfListView = System.Windows.Controls.ListView;
using WpfListViewItem = System.Windows.Controls.ListViewItem;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace KeyNestForWin.Controls;

/// <summary>列表行拖拽重排：浮动预览 + 插入线，松手后再落盘。</summary>
internal sealed class ListViewRowReorderSession
{
    private readonly WpfListView _listView;
    private readonly WpfListViewItem _sourceItem;
    private readonly Guid _sourceId;
    private readonly WpfPoint _grabOffsetInItem;
    private readonly Action<RowReorderEventArgs> _onComplete;
    private readonly Action _onEnded;

    private AdornerLayer? _layer;
    private DragGhostAdorner? _ghost;
    private InsertionLineAdorner? _line;
    private double _sourceOpacity = 1;
    private Guid? _insertBeforeId;
    private bool _insertAtEnd;

    private ListViewRowReorderSession(
        WpfListView listView,
        WpfListViewItem sourceItem,
        Guid sourceId,
        WpfPoint grabOffsetInItem,
        Action<RowReorderEventArgs> onComplete,
        Action onEnded)
    {
        _listView = listView;
        _sourceItem = sourceItem;
        _sourceId = sourceId;
        _grabOffsetInItem = grabOffsetInItem;
        _onComplete = onComplete;
        _onEnded = onEnded;
    }

    public static ListViewRowReorderSession? TryBegin(
        WpfListView listView,
        WpfListViewItem sourceItem,
        Guid sourceId,
        WpfPoint grabOffsetInItem,
        Action<RowReorderEventArgs> onComplete,
        Action onEnded)
    {
        if (!sourceItem.IsEnabled || listView.Items.Count < 2)
            return null;
        var layer = AdornerLayer.GetAdornerLayer(listView);
        if (layer == null)
            return null;
        var session = new ListViewRowReorderSession(listView, sourceItem, sourceId, grabOffsetInItem, onComplete, onEnded);
        session.Begin(layer);
        return session;
    }

    private void Begin(AdornerLayer layer)
    {
        _layer = layer;
        _sourceOpacity = _sourceItem.Opacity;
        _sourceItem.Opacity = 0.38;

        var itemSize = new WpfSize(_sourceItem.ActualWidth, _sourceItem.ActualHeight);
        if (itemSize.Width < 1 || itemSize.Height < 1)
            itemSize = new WpfSize(_listView.ActualWidth, 36);

        _ghost = new DragGhostAdorner(_listView, _sourceItem, itemSize, _grabOffsetInItem);
        _line = new InsertionLineAdorner(_listView);
        layer.Add(_ghost);
        layer.Add(_line);

        _listView.CaptureMouse();
        _listView.PreviewMouseMove += OnListMouseMove;
        _listView.PreviewMouseLeftButtonUp += OnListMouseUp;
        _listView.LostMouseCapture += OnLostCapture;

        UpdateFromMouse(Mouse.GetPosition(_listView));
    }

    public void UpdateFromMouse(WpfPoint posInList)
    {
        if (_ghost == null || _line == null) return;
        _ghost.SetPosition(posInList);
        UpdateDropTarget(posInList);
    }

    private void UpdateDropTarget(WpfPoint posInList)
    {
        _insertBeforeId = null;
        _insertAtEnd = false;
        double? lineY = null;

        WpfListViewItem? hoverItem = null;
        var hit = VisualTreeHelper.HitTest(_listView, posInList);
        if (hit?.VisualHit != null)
            hoverItem = FindAncestor<WpfListViewItem>(hit.VisualHit);

        if (hoverItem != null && hoverItem != _sourceItem)
        {
            var bounds = GetBoundsInList(hoverItem);
            var mid = bounds.Top + bounds.Height * 0.5;
            if (posInList.Y < mid)
            {
                lineY = bounds.Top;
                if (hoverItem.Content is { } row)
                    _insertBeforeId = GetRowId(row);
            }
            else
            {
                lineY = bounds.Bottom;
                _insertAtEnd = IsLastVisibleItem(hoverItem);
                if (!_insertAtEnd)
                    _insertBeforeId = GetNextRowId(hoverItem);
            }
        }
        else
        {
            var last = GetLastVisibleItem();
            if (last != null && last != _sourceItem)
            {
                var bounds = GetBoundsInList(last);
                if (posInList.Y >= bounds.Top + bounds.Height * 0.5)
                {
                    lineY = bounds.Bottom;
                    _insertAtEnd = true;
                }
            }
        }

        if (_line != null)
        {
            if (lineY.HasValue)
                _line.SetY(lineY.Value);
            else
                _line.Hide();
        }
    }

    private Rect GetBoundsInList(WpfListViewItem item)
    {
        var topLeft = item.TranslatePoint(new WpfPoint(0, 0), _listView);
        return new Rect(topLeft.X, topLeft.Y, item.ActualWidth, item.ActualHeight);
    }

    private WpfListViewItem? GetLastVisibleItem()
    {
        WpfListViewItem? last = null;
        for (var i = 0; i < _listView.Items.Count; i++)
        {
            if (_listView.ItemContainerGenerator.ContainerFromIndex(i) is WpfListViewItem lvi)
                last = lvi;
        }
        return last;
    }

    private bool IsLastVisibleItem(WpfListViewItem item)
    {
        var last = GetLastVisibleItem();
        return last != null && ReferenceEquals(last, item);
    }

    private Guid? GetNextRowId(WpfListViewItem item)
    {
        var gen = _listView.ItemContainerGenerator;
        for (var i = 0; i < _listView.Items.Count - 1; i++)
        {
            if (gen.ContainerFromIndex(i) != item) continue;
            if (gen.ContainerFromIndex(i + 1) is WpfListViewItem next)
                return GetRowId(next.Content);
            break;
        }
        return null;
    }

    private static Guid? GetRowId(object? content) =>
        content switch
        {
            Models.VaultListRow row => row.Id,
            _ => null
        };

    private void OnListMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            Cancel();
            return;
        }
        UpdateFromMouse(e.GetPosition(_listView));
        e.Handled = true;
    }

    private void OnListMouseUp(object sender, WpfMouseButtonEventArgs e)
    {
        Complete();
        e.Handled = true;
    }

    private void OnLostCapture(object sender, WpfMouseEventArgs e)
    {
        if (_ghost != null)
            Cancel();
    }

    public void Cancel()
    {
        if (_ghost == null) return;
        DetachHandlers();
        RestoreSource();
        RemoveAdorners();
        _onEnded();
    }

    public void Complete()
    {
        if (_ghost == null) return;
        DetachHandlers();
        RestoreSource();
        RemoveAdorners();

        var changed = _insertAtEnd || (_insertBeforeId is { } tid && tid != _sourceId);
        if (changed)
            _onComplete(new RowReorderEventArgs(_sourceId, _insertBeforeId, _insertAtEnd));
        _onEnded();
    }

    private void DetachHandlers()
    {
        _listView.PreviewMouseMove -= OnListMouseMove;
        _listView.PreviewMouseLeftButtonUp -= OnListMouseUp;
        _listView.LostMouseCapture -= OnLostCapture;
        try
        {
            if (_listView.IsMouseCaptured)
                _listView.ReleaseMouseCapture();
        }
        catch { /* ignore */ }
    }

    private void RestoreSource() => _sourceItem.Opacity = _sourceOpacity;

    private void RemoveAdorners()
    {
        if (_layer == null) return;
        if (_ghost != null)
        {
            _layer.Remove(_ghost);
            _ghost = null;
        }
        if (_line != null)
        {
            _layer.Remove(_line);
            _line = null;
        }
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

    private sealed class DragGhostAdorner : Adorner
    {
        private readonly VisualBrush _brush;
        private readonly WpfSize _size;
        private readonly WpfPoint _grabOffset;
        private WpfPoint _position;

        public DragGhostAdorner(WpfListView adorned, UIElement sourceVisual, WpfSize size, WpfPoint grabOffset)
            : base(adorned)
        {
            _size = size;
            _grabOffset = grabOffset;
            _brush = new VisualBrush(sourceVisual)
            {
                Opacity = 0.92,
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };
            IsHitTestVisible = false;
        }

        public void SetPosition(WpfPoint listPos)
        {
            _position = new WpfPoint(
                Math.Max(0, listPos.X - _grabOffset.X),
                Math.Max(0, listPos.Y - _grabOffset.Y));
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.PushOpacity(0.95);
            dc.DrawRectangle(_brush, null, new Rect(_position, _size));
            dc.Pop();
            var accent = WpfApp.Current.TryFindResource("Brush.Accent") as System.Windows.Media.Brush
                         ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x63, 0x66, 0xF1));
            var pen = new System.Windows.Media.Pen(accent, 1.5) { DashStyle = DashStyles.Dash };
            dc.DrawRectangle(null, pen, new Rect(_position, _size));
        }
    }

    private sealed class InsertionLineAdorner : Adorner
    {
        private double? _lineY;

        public InsertionLineAdorner(WpfListView adorned) : base(adorned)
        {
            IsHitTestVisible = false;
        }

        public void SetY(double y)
        {
            _lineY = y;
            InvalidateVisual();
        }

        public void Hide()
        {
            _lineY = null;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_lineY is not { } y) return;
            var accent = WpfApp.Current.TryFindResource("Brush.Accent") as System.Windows.Media.Brush
                         ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x63, 0x66, 0xF1));
            var pen = new System.Windows.Media.Pen(accent, 2.5)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            var w = Math.Max(0, ActualWidth - 24);
            dc.DrawLine(pen, new WpfPoint(12, y), new WpfPoint(12 + w, y));
            dc.DrawEllipse(accent, null, new WpfPoint(8, y), 4, 4);
        }
    }
}
