using System.Collections;
using System.Windows;
using GodjiVpn.Utils;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace GodjiVpn.Controls;

public enum SegmentTrack { Chip, Glass }

/// <summary>
/// Сегмент-контрол v5 со скользящим бегунком — порт SegmentedRow/PeriodSegments/SortSegments
/// (Android): дорожка (Chip + обводка, либо стеклянная капсула), под выбранным пунктом —
/// бегунок Thumb с обводкой, едущий с лёгкой пружиной. Выбранный пункт — TextPrimary,
/// остальные — TextSecondary. Пункты — строки или объекты (DisplayMemberPath).
/// </summary>
public class SegmentedControl : UserControl
{
    private readonly Grid _root = new();
    private readonly Border _thumb = new();
    private readonly TranslateTransform _thumbX = new();
    private readonly UniformGrid _cells = new() { Rows = 1 };
    private readonly List<TextBlock> _labels = new();
    private Decorator _track = new Border();

    public SegmentedControl()
    {
        Focusable = false;
        _thumb.HorizontalAlignment = HorizontalAlignment.Left;
        _thumb.VerticalAlignment = VerticalAlignment.Center;
        _thumb.RenderTransform = _thumbX;
        _thumb.BorderThickness = new Thickness(1);
        _thumb.SetResourceReference(Border.BackgroundProperty, "ThumbBrush");
        _thumb.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        // M3: выбранный сегмент — плоская заливка secondaryContainer, без тени.
        _root.Children.Add(_thumb);
        _root.Children.Add(_cells);
        BuildTrack();
        SizeChanged += (_, _) => PlaceThumb(animate: false);
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(SegmentedControl),
        new PropertyMetadata(null, (d, e) => ((SegmentedControl)d).OnItemsSourceChanged(e.OldValue as IEnumerable, e.NewValue as IEnumerable)));

    /// <summary>Коллекция может наполниться уже после привязки (периоды тарифа приходят с
    /// бэкенда позже) — перестраиваемся и по изменению самой коллекции, не только по замене.</summary>
    private void OnItemsSourceChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
        if (oldValue is System.Collections.Specialized.INotifyCollectionChanged oldNcc) oldNcc.CollectionChanged -= OnCollectionChanged;
        if (newValue is System.Collections.Specialized.INotifyCollectionChanged newNcc) newNcc.CollectionChanged += OnCollectionChanged;
        RebuildItems();
    }

    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RebuildItems();

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath), typeof(string), typeof(SegmentedControl),
        new PropertyMetadata(null, (d, _) => ((SegmentedControl)d).RebuildItems()));

    public string? DisplayMemberPath { get => (string?)GetValue(DisplayMemberPathProperty); set => SetValue(DisplayMemberPathProperty, value); }

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(SegmentedControl),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((SegmentedControl)d).OnSelectedChanged()));

    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(SegmentedControl),
        new PropertyMetadata(34.0, (d, _) => ((SegmentedControl)d).RebuildItems()));

    public double ItemHeight { get => (double)GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }

    public static readonly DependencyProperty LabelFontSizeProperty = DependencyProperty.Register(
        nameof(LabelFontSize), typeof(double), typeof(SegmentedControl),
        new PropertyMetadata(12.5, (d, _) => ((SegmentedControl)d).RebuildItems()));

    public double LabelFontSize { get => (double)GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(SegmentTrack), typeof(SegmentedControl),
        new PropertyMetadata(SegmentTrack.Chip, (d, _) => ((SegmentedControl)d).BuildTrack()));

    public SegmentTrack Track { get => (SegmentTrack)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    /// <summary>Вызывается при выборе пункта (параметр — сам пункт из ItemsSource).</summary>
    public static readonly DependencyProperty SelectCommandProperty = DependencyProperty.Register(
        nameof(SelectCommand), typeof(ICommand), typeof(SegmentedControl));

    public ICommand? SelectCommand { get => (ICommand?)GetValue(SelectCommandProperty); set => SetValue(SelectCommandProperty, value); }

    private void BuildTrack()
    {
        if (_track is Border oldBorder) oldBorder.Child = null;
        if (_track is GlassPanel oldGlass) oldGlass.Child = null;
        if (Track == SegmentTrack.Glass)
        {
            _track = new GlassPanel { CornerRadius = 999, Shadow = GlassShadow.Pill, Padding = new Thickness(4), Child = _root };
        }
        else
        {
            var b = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(3), BorderThickness = new Thickness(1), Child = _root };
            b.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            b.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            _track = b;
        }
        Content = _track;
        UpdateTrackCorners();
    }

    private void UpdateTrackCorners()
    {
        if (_track is Border b)
            b.CornerRadius = new CornerRadius((ItemHeight + 8) / 2);
    }

    private void RebuildItems()
    {
        _cells.Children.Clear();
        _labels.Clear();
        var items = ItemsSource?.Cast<object>().ToList() ?? new List<object>();
        _cells.Columns = Math.Max(1, items.Count);
        _thumb.Height = ItemHeight;
        _thumb.CornerRadius = new CornerRadius(ItemHeight / 2);
        UpdateTrackCorners();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var label = new TextBlock
            {
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            FontScale.Bind(label, TextBlock.FontSizeProperty, LabelFontSize);
            label.SetResourceReference(TextBlock.FontFamilyProperty, "ManropeFamily");
            if (!string.IsNullOrEmpty(DisplayMemberPath))
                label.SetBinding(TextBlock.TextProperty, new Binding(DisplayMemberPath) { Source = item });
            else
                label.Text = item?.ToString() ?? "";
            var cell = new Border { Background = Brushes.Transparent, Height = ItemHeight, Cursor = Cursors.Hand, Child = label };
            var index = i;
            cell.MouseLeftButtonUp += (_, _) =>
            {
                // SetCurrentValue, а не присваивание: не рвёт OneWay-привязку (выбор, который
                // применяет команда, а не сама привязка), TwoWay по-прежнему пишет в источник.
                SetCurrentValue(SelectedIndexProperty, index);
                if (SelectCommand?.CanExecute(item) == true) SelectCommand.Execute(item);
            };
            _labels.Add(label);
            _cells.Children.Add(cell);
        }
        UpdateLabelColors();
        PlaceThumb(animate: false);
    }

    private void OnSelectedChanged()
    {
        UpdateLabelColors();
        PlaceThumb(animate: true);
    }

    private void UpdateLabelColors()
    {
        for (var i = 0; i < _labels.Count; i++)
            _labels[i].SetResourceReference(TextBlock.ForegroundProperty, i == SelectedIndex ? "TextPrimaryBrush" : "TextSecondaryBrush");
    }

    private void PlaceThumb(bool animate)
    {
        var count = _labels.Count;
        var width = _root.ActualWidth;
        if (count == 0 || width <= 0)
        {
            _thumb.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
            return;
        }
        _thumb.Visibility = SelectedIndex >= 0 && SelectedIndex < count ? Visibility.Visible : Visibility.Hidden;
        var slot = width / count;
        _thumb.Width = slot;
        var x = slot * Math.Clamp(SelectedIndex, 0, count - 1);
        if (animate)
        {
            var anim = new DoubleAnimation(x, TimeSpan.FromMilliseconds(380))
            {
                EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
            };
            _thumbX.BeginAnimation(TranslateTransform.XProperty, anim);
        }
        else
        {
            _thumbX.BeginAnimation(TranslateTransform.XProperty, null);
            _thumbX.X = x;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Dispatcher.BeginInvoke(() => PlaceThumb(animate: false));
    }
}
