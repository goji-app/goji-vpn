using GodjiVpn.Utils;
using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace GodjiVpn.Controls;

public enum SegmentTrack { Chip, Glass }

/// <summary>
/// Сегмент-контрол v5 со скользящим бегунком — порт SegmentedControl Windows-клиента: дорожка
/// (Chip + обводка или стеклянная капсула), под выбранным пунктом бегунок Thumb с обводкой,
/// едущий с лёгкой пружиной. Пункты — строки или объекты (DisplayMemberPath).
/// </summary>
public class SegmentedControl : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SegmentedControl, IEnumerable?>(nameof(ItemsSource));
    public static readonly StyledProperty<string?> DisplayMemberPathProperty =
        AvaloniaProperty.Register<SegmentedControl, string?>(nameof(DisplayMemberPath));
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<SegmentedControl, int>(nameof(SelectedIndex), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<SegmentedControl, double>(nameof(ItemHeight), 34);
    public static readonly StyledProperty<double> LabelFontSizeProperty =
        AvaloniaProperty.Register<SegmentedControl, double>(nameof(LabelFontSize), 12.5);
    public static readonly StyledProperty<SegmentTrack> TrackProperty =
        AvaloniaProperty.Register<SegmentedControl, SegmentTrack>(nameof(Track));
    public static readonly StyledProperty<ICommand?> SelectCommandProperty =
        AvaloniaProperty.Register<SegmentedControl, ICommand?>(nameof(SelectCommand));

    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public string? DisplayMemberPath { get => GetValue(DisplayMemberPathProperty); set => SetValue(DisplayMemberPathProperty, value); }
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public double ItemHeight { get => GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    public double LabelFontSize { get => GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }
    public SegmentTrack Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public ICommand? SelectCommand { get => GetValue(SelectCommandProperty); set => SetValue(SelectCommandProperty, value); }

    private readonly Panel _root = new();
    private readonly Border _thumb = new();
    private readonly UniformGrid _cells = new() { Rows = 1 };
    private readonly List<TextBlock> _labels = new();
    private INotifyCollectionChanged? _observed;
    private bool _thumbPlaced;

    public SegmentedControl()
    {
        _thumb.HorizontalAlignment = HorizontalAlignment.Left;
        _thumb.VerticalAlignment = VerticalAlignment.Center;
        _thumb.BorderThickness = new Thickness(1);
        _thumb[!Border.BackgroundProperty] = new DynamicResourceExtension("ThumbBrush");
        _thumb[!Border.BorderBrushProperty] = new DynamicResourceExtension("CardBorderBrush");
        // M3: выбранный сегмент — плоская заливка secondaryContainer, без тени.
        _root.Children.Add(_thumb);
        _root.Children.Add(_cells);
        BuildTrack();
        _root.SizeChanged += (_, _) => PlaceThumb(animate: false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
        {
            if (_observed != null) _observed.CollectionChanged -= OnCollectionChanged;
            _observed = ItemsSource as INotifyCollectionChanged;
            if (_observed != null) _observed.CollectionChanged += OnCollectionChanged;
            RebuildItems();
        }
        else if (change.Property == DisplayMemberPathProperty || change.Property == ItemHeightProperty ||
                 change.Property == LabelFontSizeProperty)
        {
            RebuildItems();
        }
        else if (change.Property == TrackProperty)
        {
            BuildTrack();
        }
        else if (change.Property == SelectedIndexProperty)
        {
            UpdateLabelColors();
            PlaceThumb(animate: true);
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildItems();

    private void BuildTrack()
    {
        if (Content is Decorator old) old.Child = null;
        if (Track == SegmentTrack.Glass)
        {
            var glass = new GlassPanel { Padding = new Thickness(4), Child = _root };
            glass.Classes.Add("pill");
            Content = glass;
        }
        else
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(3),
                BorderThickness = new Thickness(1),
                Child = _root
            };
            b[!Border.BackgroundProperty] = new DynamicResourceExtension("ChipBrush");
            b[!Border.BorderBrushProperty] = new DynamicResourceExtension("CardBorderBrush");
            Content = b;
        }
    }

    private void RebuildItems()
    {
        _cells.Children.Clear();
        _labels.Clear();
        var items = ItemsSource?.Cast<object>().ToList() ?? new List<object>();
        _cells.Columns = Math.Max(1, items.Count);
        _thumb.Height = ItemHeight;
        _thumb.CornerRadius = new CornerRadius(ItemHeight / 2);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var label = new TextBlock
            {
                [!TextBlock.FontSizeProperty] = FontScale.For(LabelFontSize),
                FontWeight = FontWeight.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0)
            };
            if (!string.IsNullOrEmpty(DisplayMemberPath))
                label.Bind(TextBlock.TextProperty, new Binding(DisplayMemberPath) { Source = item });
            else
                label.Text = item?.ToString() ?? "";
            var cell = new Border
            {
                Background = Brushes.Transparent,
                Height = ItemHeight,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = label
            };
            var index = i;
            cell.PointerReleased += (_, _) =>
            {
                SetCurrentValue(SelectedIndexProperty, index);
                if (SelectCommand?.CanExecute(item) == true) SelectCommand.Execute(item);
            };
            _labels.Add(label);
            _cells.Children.Add(cell);
        }
        UpdateLabelColors();
        _thumbPlaced = false;
        PlaceThumb(animate: false);
    }

    private void UpdateLabelColors()
    {
        for (var i = 0; i < _labels.Count; i++)
            _labels[i][!TextBlock.ForegroundProperty] =
                new DynamicResourceExtension(i == SelectedIndex ? "TextPrimaryBrush" : "TextSecondaryBrush");
    }

    private void PlaceThumb(bool animate)
    {
        var count = _labels.Count;
        var width = _root.Bounds.Width;
        _thumb.IsVisible = count > 0 && SelectedIndex >= 0 && SelectedIndex < count;
        if (count == 0 || width <= 0) return;
        var slot = width / count;
        _thumb.Width = slot;
        var x = slot * Math.Clamp(SelectedIndex, 0, count - 1);
        _thumb.Transitions = animate && _thumbPlaced
            ? new Transitions
            {
                new ThicknessTransition { Property = MarginProperty, Duration = TimeSpan.FromMilliseconds(380), Easing = new BackEaseOut() }
            }
            : null;
        _thumb.Margin = new Thickness(x, 0, 0, 0);
        _thumbPlaced = true;
    }
}
