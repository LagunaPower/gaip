using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GAIP.Core;

namespace GAIP.Desktop;

public sealed class SiteOrderEditor : UserControl
{
    private readonly ObservableCollection<Site> _sites;
    private readonly Guid[] _initial;
    private readonly ListBox _list;
    private readonly Border _marker = new() { Height = 2, Background = Brushes.DodgerBlue, IsVisible = false };
    private readonly TextBlock _hint = Ui.Text("", 12);
    private readonly DispatcherTimer _scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private Site? _source;
    private Point _origin, _position;
    private IPointer? _pointer;
    private bool _dragging;
    private int _insertAt;

    public Guid[] OrderedIds => _sites.Select(s => s.Id).ToArray();
    public bool HasChanges => !_initial.SequenceEqual(OrderedIds);

    public SiteOrderEditor(IEnumerable<Site> sites)
    {
        _sites = new(SiteOrdering.Ordered(sites)); _initial = OrderedIds;
        _list = new ListBox { Name = "SiteOrderList", ItemsSource = _sites, Height = 220,
            ItemTemplate = new FuncDataTemplate<Site>((site, _) =>
            {
                if (site is null) return null;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,120,*"), ColumnSpacing = 10,
                    MinHeight = 42, Margin = new Thickness(6, 0) };
                row.Children.Add(Ui.Text("↕", 18));
                var code = Ui.Text(site.Code, 14, true); Grid.SetColumn(code, 1); row.Children.Add(code);
                var name = Ui.Text(site.Name); Grid.SetColumn(name, 2); row.Children.Add(name);
                return row;
            }) };
        Avalonia.Automation.AutomationProperties.SetName(_list, "Ordre d’affichage des sites");
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>()) { Setters =
        { new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
            new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 2)) } });
        var overlay = new Canvas { IsHitTestVisible = false }; overlay.Children.Add(_marker);
        var surface = new Grid(); surface.Children.Add(_list); surface.Children.Add(overlay);
        var up = Ui.Button("Monter", () => MoveSelected(-1));
        var down = Ui.Button("Descendre", () => MoveSelected(1));
        void Selection() { up.IsEnabled = _list.SelectedIndex > 0; down.IsEnabled = _list.SelectedIndex >= 0 && _list.SelectedIndex < _sites.Count - 1; }
        _list.SelectionChanged += (_, _) => Selection(); _sites.CollectionChanged += (_, _) => Selection(); Selection();
        _hint.Text = _sites.Count == 0 ? "Aucun site à classer." : "Glissez un site jusqu’au trait bleu. Vous pouvez aussi utiliser Monter / Descendre.";
        Content = Ui.Column(surface, Ui.Row(up, down), _hint);
        _list.AddHandler(PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        _list.AddHandler(PointerMovedEvent, Moved, RoutingStrategies.Tunnel);
        _list.AddHandler(PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
        _list.PointerCaptureLost += (_, _) => Reset();
        _list.KeyDown += (_, e) => { if (e.Key == Key.Escape) { Cancel(); e.Handled = true; } };
        DetachedFromVisualTree += (_, _) => Cancel();
        _scrollTimer.Tick += (_, _) =>
        {
            var scroll = _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroll is null) return;
            var delta = _position.Y < 28 ? -18 : _position.Y > _list.Bounds.Height - 28 ? 18 : 0;
            if (delta != 0) { scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, scroll.Offset.Y + delta)); _list.UpdateLayout(); UpdateMarker(); }
        };
    }

    private void MoveSelected(int offset)
    {
        var from = _list.SelectedIndex; var to = from + offset;
        if (from < 0 || to < 0 || to >= _sites.Count) return;
        var selected = _sites[from]; _sites.Move(from, to); _list.SelectedItem = selected; _list.ScrollIntoView(to);
    }
    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsEffectivelyEnabled || !e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) return;
        var row = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        if (row?.Content is not Site site) return;
        _source = site; _origin = _position = e.GetPosition(_list); _dragging = false;
        _list.SelectedItem = site; _list.Focus(); _pointer = e.Pointer; e.Pointer.Capture(_list); e.Handled = true;
    }
    private void Moved(object? sender, PointerEventArgs e)
    {
        if (_source is null) return;
        _position = e.GetPosition(_list);
        if (!_dragging && Math.Abs(_position.Y - _origin.Y) < 5) return;
        _dragging = true; UpdateMarker(); _scrollTimer.Start(); e.Handled = true;
    }
    private void UpdateMarker()
    {
        double y = 0; _insertAt = _sites.Count;
        foreach (var row in _list.GetVisualDescendants().OfType<ListBoxItem>().OrderBy(r => r.TranslatePoint(default, _list)?.Y))
        {
            if (row.Content is not Site site || row.TranslatePoint(default, _list) is not { } point) continue;
            y = point.Y + row.Bounds.Height;
            if (_position.Y < point.Y + row.Bounds.Height / 2) { _insertAt = _sites.IndexOf(site); y = point.Y; break; }
            _insertAt = _sites.IndexOf(site) + 1;
        }
        _marker.Width = Math.Max(0, _list.Bounds.Width - 14); Canvas.SetLeft(_marker, 2);
        Canvas.SetTop(_marker, Math.Clamp(y, 0, Math.Max(0, _list.Bounds.Height - 2))); _marker.IsVisible = true;
    }
    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_source is null) return;
        var point = e.GetPosition(_list);
        if (_dragging && point.X >= 0 && point.X <= _list.Bounds.Width && point.Y >= 0 && point.Y <= _list.Bounds.Height)
        {
            var from = _sites.IndexOf(_source); var to = Math.Clamp(_insertAt - (from < _insertAt ? 1 : 0), 0, _sites.Count - 1);
            var site = _source; _sites.Move(from, to); _list.SelectedItem = site;
        }
        Cancel(); e.Handled = true;
    }
    private void Cancel() { var pointer = _pointer; Reset(); pointer?.Capture(null); }
    private void Reset() { _source = null; _pointer = null; _dragging = false; _marker.IsVisible = false; _scrollTimer.Stop(); }
}
