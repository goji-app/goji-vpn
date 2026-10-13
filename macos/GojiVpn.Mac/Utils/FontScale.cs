using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace GodjiVpn.Utils;

/// <summary>Размер шрифта — порт Android FontSizePreset: множители 0.9 / 1 / 1.15.</summary>
public enum FontSizePreset { Small, Normal, Large }

/// <summary>
/// Текущий множитель размера шрифта (Настройки → Внешний вид → «Размер шрифта»). Меняет только
/// текст, не отступы и размеры элементов — как fontScale в Android. Все размеры шрифта в AXAML
/// заданы через {s:Fs …} и подписаны на <see cref="Factor"/>, поэтому смена применяется сразу.
/// </summary>
public sealed class FontScale : INotifyPropertyChanged
{
    public static FontScale Instance { get; } = new();

    private double _factor = 1.0;

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
        FontSizePreset.Small => 0.9,
        FontSizePreset.Large => 1.15,
        _ => 1.0
    };

    /// <summary>Привязка размера шрифта для элементов, созданных в коде:
    /// new TextBlock { [!TextBlock.FontSizeProperty] = FontScale.For(13) }.</summary>
    public static IBinding For(double size) => FsExtension.CreateBinding(size);

    /// <summary>Привязка размера шрифта элемента, созданного в коде, к настройке.</summary>
    public static void Bind(AvaloniaObject target, AvaloniaProperty property, double size) =>
        target.Bind(property, FsExtension.CreateBinding(size));
}

/// <summary>
/// Размер шрифта в AXAML: FontSize="{s:Fs 13.5}" — 13.5 при «Нормальном», ×0.9 и ×1.15 при
/// «Маленьком» и «Большом». Работает и в атрибутах, и в Setter стилей.
/// </summary>
public sealed class FsExtension : MarkupExtension
{
    public FsExtension() { }
    public FsExtension(double size) => Size = size;

    public double Size { get; set; }

    internal static Binding CreateBinding(double size) => new(nameof(FontScale.Factor))
    {
        Source = FontScale.Instance,
        Mode = BindingMode.OneWay,
        Converter = ScaleConverter.Instance,
        ConverterParameter = size
    };

    public override object ProvideValue(IServiceProvider serviceProvider) => CreateBinding(Size);

    private sealed class ScaleConverter : IValueConverter
    {
        public static readonly ScaleConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Math.Round((double)parameter! * (value is double f ? f : 1.0), 2);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
