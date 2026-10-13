using System.Windows.Controls;

namespace GodjiVpn.Controls;

/// <summary>
/// Общий фон приложения — рисуется один раз под всем содержимым окна (MainWindow, SupportWindow,
/// диалоги). «Goji Expressive» (Material 3 Expressive, порт Android GlassBackdrop.kt 163f5ab):
/// сплошной surface. Пятна, кольца и сетка точек v5 убраны — вместе с бликами стекла они и были
/// главной нагрузкой на перерисовку. Имя прежнее, чтобы не трогать разметку окон.
/// </summary>
public class GlassBackdrop : Grid
{
    public GlassBackdrop()
    {
        IsHitTestVisible = false;
        SetResourceReference(BackgroundProperty, "BgSolidBrush");
    }
}
