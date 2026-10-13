using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GodjiVpn.Controls;

/// <summary>Сегментированный ввод OTP-кода — 6 отдельных клеток вместо одного текстового поля,
/// порт из Android VerifyEmailScreen.kt (OtpCodeInput/DigitCell). В отличие от Android (там
/// реальный ввод идёт через невидимое BasicTextField, растянутое поверх клеток — визуальные
/// клетки лишь отражают его value), здесь используются 6 РЕАЛЬНЫХ TextBox с явным управлением
/// фокусом (автопереход вперёд по вводу цифры, назад по Backspace на пустой клетке, поддержка
/// вставки всего кода целиком) — нативный для WPF приём вместо имитации невидимого поля.
/// Мигающий каретка в активной клетке — обычный системный каретка WPF-TextBox, отдельно
/// реализовывать не пришлось.</summary>
public partial class OtpInput : UserControl
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
        nameof(Code), typeof(string), typeof(OtpInput),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCodeChanged));

    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.Register(
        nameof(HasError), typeof(bool), typeof(OtpInput),
        new PropertyMetadata(false, OnVisualStateChanged));

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public bool HasError
    {
        get => (bool)GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
    }

    /// <summary>Аналог "auto-verify once entered" — срабатывает, когда все 6 клеток заполнены
    /// (набором с клавиатуры или вставкой), ровно в момент заполнения последней.</summary>
    public event Action? Completed;

    private TextBox[] Cells => _cells ??= new[] { Cell0, Cell1, Cell2, Cell3, Cell4, Cell5 };
    private TextBox[]? _cells;
    private bool _suppressCodeSync;

    public OtpInput()
    {
        InitializeComponent();
        foreach (var cell in Cells)
        {
            cell.PreviewTextInput += OnCellPreviewTextInput;
            cell.TextChanged += OnCellTextChanged;
            cell.PreviewKeyDown += OnCellPreviewKeyDown;
            cell.GotFocus += (_, _) => { cell.SelectAll(); UpdateVisualStates(); };
            cell.LostFocus += (_, _) => UpdateVisualStates();
            DataObject.AddPastingHandler(cell, OnPaste);
        }
        UpdateVisualStates();
    }

    public void FocusFirst() => Cell0.Focus();

    private static void OnCodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((OtpInput)d).SyncFromCode((string)e.NewValue);

    private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((OtpInput)d).UpdateVisualStates();

    /// <summary>Внешнее присваивание Code (например ViewModel очищает код после неверной
    /// попытки — см. LoginViewModel.VerifyAsync) должно перерисовать клетки, не запуская
    /// TextChanged-обработчики клеток заново (иначе получили бы рекурсию/лишний Completed).</summary>
    private void SyncFromCode(string code)
    {
        if (_suppressCodeSync) return;
        _suppressCodeSync = true;
        try
        {
            for (var i = 0; i < Cells.Length; i++)
                Cells[i].Text = i < code.Length ? code[i].ToString() : "";
        }
        finally { _suppressCodeSync = false; }
        UpdateVisualStates();
    }

    private void OnCellPreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = e.Text.Length == 0 || !char.IsDigit(e.Text[0]);

    private void OnCellTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressCodeSync) return;
        var cell = (TextBox)sender;
        var index = Array.IndexOf(Cells, cell);

        PushCodeFromCells();
        if (cell.Text.Length == 1 && index < Cells.Length - 1) Cells[index + 1].Focus();
        UpdateVisualStates();
        if (Cells.All(c => c.Text.Length == 1)) Completed?.Invoke();
    }

    private void OnCellPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var cell = (TextBox)sender;
        var index = Array.IndexOf(Cells, cell);
        if (e.Key == Key.Back && cell.Text.Length == 0 && index > 0)
        {
            Cells[index - 1].Focus();
            Cells[index - 1].Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Left && index > 0) { Cells[index - 1].Focus(); e.Handled = true; }
        else if (e.Key == Key.Right && index < Cells.Length - 1) { Cells[index + 1].Focus(); e.Handled = true; }
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text)) return;
        var text = (string)e.DataObject.GetData(DataFormats.Text);
        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return;
        e.CancelCommand();

        _suppressCodeSync = true;
        try
        {
            for (var i = 0; i < Cells.Length; i++)
                Cells[i].Text = i < digits.Length ? digits[i].ToString() : "";
        }
        finally { _suppressCodeSync = false; }
        PushCodeFromCells();
        var lastFilled = Math.Min(digits.Length, Cells.Length) - 1;
        Cells[Math.Min(lastFilled + 1, Cells.Length - 1)].Focus();
        UpdateVisualStates();
        if (Cells.All(c => c.Text.Length == 1)) Completed?.Invoke();
    }

    private void PushCodeFromCells()
    {
        _suppressCodeSync = true;
        try { SetCurrentValue(CodeProperty, string.Concat(Cells.Select(c => c.Text))); }
        finally { _suppressCodeSync = false; }
    }

    /// <summary>Цвет рамки клетки отражает её состояние (активная/заполненная/ошибка/пустая) —
    /// тот же смысл, что и borderColor в Android DigitCell, только без анимации перехода между
    /// цветами (там Compose, тут просто мгновенная смена — минорное упрощение).</summary>
    private void UpdateVisualStates()
    {
        var activeIndex = Cells.Length;
        for (var i = 0; i < Cells.Length; i++)
        {
            if (Cells[i].Text.Length == 0) { activeIndex = i; break; }
        }
        for (var i = 0; i < Cells.Length; i++)
        {
            var cell = Cells[i];
            var filled = cell.Text.Length == 1;
            var isActive = i == activeIndex && cell.IsFocused;
            cell.BorderBrush = HasError
                ? ResourceBrush("DangerBrush")
                : isActive ? ResourceBrush("TealBrush")
                : ResourceBrush("CardBorderBrush");
            cell.BorderThickness = new Thickness(isActive || HasError ? 1.5 : 1);
        }
    }

    private Brush ResourceBrush(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
}
