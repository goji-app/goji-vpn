using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace GodjiVpn.Utils;

/// <summary>Размер шрифта — порт Android FontSizePreset: множители 0.9 / 1 / 1.15.</summary>
public enum FontSizePreset { Small, Normal, Large }

/// <summary>
/// Текущий множитель размера шрифта (Настройки → Внешний вид → «Размер шрифта»). Меняет только
/// текст, не отступы и размеры элементов — как fontScale в Android. Все размеры шрифта в XAML
/// заданы через {s:Fs …} и подписаны на <see cref="Factor"/>, поэтому смена применяется сразу.
/// </summary>
public sealed class FontScale : INotifyPropertyChanged
{
    public static FontScale Instance { get; } = new();

    private double _factor = 0.9; // «Обычный», до применения сохранённой настройки

    public double Factor
    {
        get => _factor;
        private set
        {
            if (Math.Abs(_factor - value) < 0.0001) return;
            _factor = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Factor)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(FontSizePreset preset) => Factor = Multiplier(preset);

    public static double Multiplier(FontSizePreset preset) => preset switch
    {
        // Шкала сдвинута на ступень вниз 2026-10-16: «Обычный» = прежний «Маленький»,
        // «Большой» = прежний «Обычный», «Маленький» — заметно мельче.
        FontSizePreset.Small => 0.8,
        FontSizePreset.Large => 1.0,
        _ => 0.9
    };

    /// <summary>Размер с учётом настройки — для текста, который строится в коде.</summary>
    public static double Of(double size) => Math.Round(size * Instance.Factor, 2);

    /// <summary>Привязка размера шрифта элемента, созданного в коде, к настройке.</summary>
    public static void Bind(DependencyObject target, DependencyProperty property, double size) =>
        BindingOperations.SetBinding(target, property, FsExtension.CreateBinding(size));
}

/// <summary>
/// Размер шрифта в XAML: FontSize="{s:Fs 13.5}" — 13.5 × 0.9 при «Обычном», ×0.8 и ×1.0 при
/// «Маленьком» и «Большом». Работает и в атрибутах, и в Setter стилей и шаблонов.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class FsExtension : MarkupExtension
{
    public FsExtension() { }
    public FsExtension(double size) => Size = size;

    [ConstructorArgument("size")]
    public double Size { get; set; }

    internal static Binding CreateBinding(double size) => new(nameof(FontScale.Factor))
    {
        Source = FontScale.Instance,
        Mode = BindingMode.OneWay,
        Converter = ScaleConverter.Instance,
        ConverterParameter = size
    };

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        CreateBinding(Size).ProvideValue(serviceProvider);

    private sealed class ScaleConverter : IValueConverter
    {
        public static readonly ScaleConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            Math.Round((double)parameter * (value is double f ? f : 1.0), 2);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
