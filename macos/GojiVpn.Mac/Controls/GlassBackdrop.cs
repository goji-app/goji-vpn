using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace GodjiVpn.Controls;

/// <summary>
/// Общий фон приложения — порт Android GlassBackdrop.kt (163f5ab) и Windows-клиента: «Goji
/// Expressive» (Material 3 Expressive) — сплошной surface. Пятна, кольца и сетка точек v5 убраны.
/// Имя прежнее, чтобы не трогать разметку окон.
/// </summary>
public class GlassBackdrop : Panel
{
    public GlassBackdrop()
    {
        IsHitTestVisible = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("BgSolidBrush");
    }
}
