using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace GodjiVpn.Controls;

/// <summary>
/// Список без прокрутки: показывает столько строк, сколько помещается в отведённую высоту,
/// остальное — на следующих страницах ("‹ 1 / 3 ›" под списком, колесо мыши листает).
/// Сколько строк на странице, считается заново при каждом изменении размера окна, шрифта
/// или содержимого. Высоту списку должен задать контейнер (строка Grid со звёздочкой):
/// при бесконечной высоте всё помещается на одну страницу. Шаблон — стиль c|PagedItems в Glass.axaml.
/// </summary>
public class PagedItems : ItemsControl
{
    public static readonly StyledProperty<int> PageIndexProperty =
        AvaloniaProperty.Register<PagedItems, int>(nameof(PageIndex));

    public static readonly DirectProperty<PagedItems, int> PageCountProperty =
        AvaloniaProperty.RegisterDirect<PagedItems, int>(nameof(PageCount), o => o.PageCount);

    public static readonly DirectProperty<PagedItems, bool> HasPagesProperty =
        AvaloniaProperty.RegisterDirect<PagedItems, bool>(nameof(HasPages), o => o.HasPages);

    public static readonly DirectProperty<PagedItems, string> PagerTextProperty =
        AvaloniaProperty.RegisterDirect<PagedItems, string>(nameof(PagerText), o => o.PagerText);

    private int _pageCount = 1;
    private bool _hasPages;
    private string _pagerText = "1 / 1";

    public int PageIndex { get => GetValue(PageIndexProperty); set => SetValue(PageIndexProperty, value); }
    public int PageCount { get => _pageCount; private set => SetAndRaise(PageCountProperty, ref _pageCount, value); }
    /// <summary>Страниц больше одной — показывать переключатель.</summary>
    public bool HasPages { get => _hasPages; private set => SetAndRaise(HasPagesProperty, ref _hasPages, value); }
    public string PagerText { get => _pagerText; private set => SetAndRaise(PagerTextProperty, ref _pagerText, value); }

    public IRelayCommand PrevCommand { get; }
    public IRelayCommand NextCommand { get; }

    /// <summary>Панель строк (регистрируется сама при первом измерении).</summary>
    internal PagedPanel? Panel { get; set; }

    public PagedItems()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new PagedPanel());
        PrevCommand = new RelayCommand(() => PageIndex--, () => PageIndex > 0);
        NextCommand = new RelayCommand(() => PageIndex++, () => PageIndex < PageCount - 1);
        // Превью-экземпляр: стартовая страница списков для сверки (GODJI_UI_PREVIEW_LIST_PAGE=1).
        if (Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW") == "1" &&
            int.TryParse(Environment.GetEnvironmentVariable("GODJI_UI_PREVIEW_LIST_PAGE"), out var previewListPage))
            Dispatcher.UIThread.Post(() => PageIndex = previewListPage, DispatcherPriority.Background);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageIndexProperty)
        {
            UpdatePager();
            // Другая страница: размер панели не меняется — перемеряем её явно.
            Panel?.InvalidateMeasure();
            Panel?.InvalidateArrange();
        }
        else if (change.Property == ItemsSourceProperty)
        {
            // Новый набор строк (поиск, сортировка, удаление) — снова с первой страницы.
            PageIndex = 0;
        }
    }

    /// <summary>Колесо мыши листает страницы, раз прокрутки нет.</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (PageCount <= 1) return;
        var next = PageIndex + (e.Delta.Y < 0 ? 1 : -1);
        if (next >= 0 && next < PageCount) { PageIndex = next; e.Handled = true; }
    }

    /// <summary>Число страниц сообщает панель во время измерения — применяем после прохода
    /// раскладки: переключатель страниц меняет высоту, доступную самой панели.</summary>
    internal void ReportPages(int count)
    {
        if (count == PageCount && PageIndex <= count - 1 && HasPages == count > 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            PageCount = count;
            HasPages = count > 1;
            if (PageIndex > count - 1) PageIndex = Math.Max(0, count - 1);
            UpdatePager();
        });
    }

    private void UpdatePager()
    {
        PagerText = $"{Math.Min(PageIndex + 1, PageCount)} / {PageCount}";
        PrevCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>
/// Панель PagedItems: раскладывает строки текущей страницы стопкой, остальные уводит за
/// границу (панель обрезает содержимое). Первой строке каждой страницы ставит IsPageStart —
/// стиль Rectangle.pagedSep по нему прячет разделитель сверху.
/// </summary>
public class PagedPanel : Panel
{
    public static readonly AttachedProperty<bool> IsPageStartProperty =
        AvaloniaProperty.RegisterAttached<PagedPanel, Control, bool>("IsPageStart", inherits: true);

    public static bool GetIsPageStart(Control c) => c.GetValue(IsPageStartProperty);
    public static void SetIsPageStart(Control c, bool value) => c.SetValue(IsPageStartProperty, value);

    private readonly List<int> _pageStarts = new() { 0 };
    private PagedItems? _owner;

    public PagedPanel() => ClipToBounds = true;

    private PagedItems? Owner
    {
        get
        {
            if (_owner != null) return _owner;
            _owner = this.FindAncestorOfType<PagedItems>();
            if (_owner != null) _owner.Panel = this;
            return _owner;
        }
    }

    protected override Size MeasureOverride(Size available)
    {
        _pageStarts.Clear();
        _pageStarts.Add(0);
        double used = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            var h = child.DesiredSize.Height;
            if (used > 0 && used + h > available.Height)
            {
                _pageStarts.Add(i);
                used = 0;
            }
            used += h;
        }
        var owner = Owner;
        // Предварительные проходы с бесконечной высотой (так сетка меряет строки со звёздочкой)
        // не считаются: иначе 1 и N страниц чередовались бы и раскладка не останавливалась.
        if (!double.IsInfinity(available.Height)) owner?.ReportPages(_pageStarts.Count);
        for (var i = 0; i < Children.Count; i++)
            SetIsPageStart(Children[i], _pageStarts.Contains(i));

        var (from, to) = PageRange(CurrentPage());
        double height = 0, maxWidth = 0;
        for (var i = from; i < to; i++)
        {
            height += Children[i].DesiredSize.Height;
            maxWidth = Math.Max(maxWidth, Children[i].DesiredSize.Width);
        }
        return new Size(double.IsInfinity(available.Width) ? maxWidth : available.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (from, to) = PageRange(CurrentPage());
        double y = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (i >= from && i < to)
            {
                child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
                y += child.DesiredSize.Height;
            }
            else
            {
                child.Arrange(new Rect(0, finalSize.Height + 10000, finalSize.Width, child.DesiredSize.Height));
            }
        }
        return finalSize;
    }

    private int CurrentPage() => Math.Clamp(Owner?.PageIndex ?? 0, 0, _pageStarts.Count - 1);

    private (int From, int To) PageRange(int page)
    {
        var from = _pageStarts[page];
        var to = page + 1 < _pageStarts.Count ? _pageStarts[page + 1] : Children.Count;
        return (from, to);
    }
}
