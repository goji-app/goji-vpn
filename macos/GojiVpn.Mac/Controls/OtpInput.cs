using GodjiVpn.Utils;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace GodjiVpn.Controls;

/// <summary>
/// Ввод кода из письма — 6 клеток (порт OtpInput Windows-клиента): автопереход вперёд по
/// цифре, назад по Backspace на пустой клетке, вставка кода целиком; Completed — когда все
/// клетки заполнены. Активная клетка — с бирюзовой рамкой, ошибка — красной.
/// </summary>
public class OtpInput : UserControl
{
    public static readonly StyledProperty<string> CodeProperty =
        AvaloniaProperty.Register<OtpInput, string>(nameof(Code), "", defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<bool> HasErrorProperty =
        AvaloniaProperty.Register<OtpInput, bool>(nameof(HasError));

    public string Code { get => GetValue(CodeProperty); set => SetValue(CodeProperty, value); }
    public bool HasError { get => GetValue(HasErrorProperty); set => SetValue(HasErrorProperty, value); }

    public event Action? Completed;

    private readonly TextBox[] _cells = new TextBox[6];
    private bool _sync;

    public OtpInput()
    {
        var grid = new UniformGrid { Rows = 1 };
        for (var i = 0; i < _cells.Length; i++)
        {
            var cell = new TextBox
            {
                Height = 56,
                Margin = new Thickness(4, 0),
                MaxLength = 1,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                [!TextBox.FontSizeProperty] = FontScale.For(20),
                FontWeight = FontWeight.Bold,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1)
            };
            cell.Classes.Add("glass");
            var index = i;
            cell.AddHandler(TextInputEvent, (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Text) || !char.IsDigit(e.Text[0])) e.Handled = true;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            cell.TextChanged += (_, _) => OnCellChanged(index);
            cell.KeyDown += (_, e) => OnCellKeyDown(index, e);
            cell.PastingFromClipboard += async (_, e) =>
            {
                e.Handled = true;
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                var text = clipboard != null ? await clipboard.GetTextAsync() : null;
                var digits = new string((text ?? "").Where(char.IsDigit).Take(6).ToArray());
                if (digits.Length == 0) return;
                SetCode(digits, pushToProperty: true);
                _cells[Math.Min(digits.Length, 5)].Focus();
                if (digits.Length == 6) Completed?.Invoke();
            };
            cell.GotFocus += (_, _) => { cell.SelectAll(); UpdateVisuals(); };
            cell.LostFocus += (_, _) => UpdateVisuals();
            _cells[i] = cell;
            grid.Children.Add(cell);
        }
        Content = grid;
        UpdateVisuals();
    }

    public void FocusFirst() => _cells[0].Focus();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CodeProperty && !_sync) SetCode(Code ?? "", pushToProperty: false);
        else if (change.Property == HasErrorProperty) UpdateVisuals();
    }

    private void SetCode(string code, bool pushToProperty)
    {
        _sync = true;
        try
        {
            for (var i = 0; i < _cells.Length; i++) _cells[i].Text = i < code.Length ? code[i].ToString() : "";
            if (pushToProperty) SetCurrentValue(CodeProperty, string.Concat(_cells.Select(c => c.Text)));
        }
        finally { _sync = false; }
        UpdateVisuals();
    }

    private void OnCellChanged(int index)
    {
        if (_sync) return;
        _sync = true;
        try { SetCurrentValue(CodeProperty, string.Concat(_cells.Select(c => c.Text))); }
        finally { _sync = false; }
        if (_cells[index].Text?.Length == 1 && index < _cells.Length - 1) _cells[index + 1].Focus();
        UpdateVisuals();
        if (_cells.All(c => c.Text?.Length == 1)) Completed?.Invoke();
    }

    private void OnCellKeyDown(int index, KeyEventArgs e)
    {
        if (e.Key == Key.Back && string.IsNullOrEmpty(_cells[index].Text) && index > 0)
        {
            _cells[index - 1].Focus();
            _cells[index - 1].Text = "";
            e.Handled = true;
        }
        else if (e.Key == Key.Left && index > 0) { _cells[index - 1].Focus(); e.Handled = true; }
        else if (e.Key == Key.Right && index < _cells.Length - 1) { _cells[index + 1].Focus(); e.Handled = true; }
    }

    private void UpdateVisuals()
    {
        var active = Array.FindIndex(_cells, c => string.IsNullOrEmpty(c.Text));
        for (var i = 0; i < _cells.Length; i++)
        {
            var key = HasError ? "DangerBrush" : i == active && _cells[i].IsFocused ? "TealBrush" : "CardBorderBrush";
            _cells[i][!TemplatedControl.BorderBrushProperty] = new DynamicResourceExtension(key);
            _cells[i].BorderThickness = new Thickness(HasError || i == active ? 1.5 : 1);
        }
    }
}
