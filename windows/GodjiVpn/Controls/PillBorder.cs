using System.Windows;
using System.Windows.Controls;

namespace GodjiVpn.Controls;

/// <summary>Border-капсула: радиус всегда ровно половина меньшей стороны — RoundedCornerShape(50)/
/// CircleShape из Android. У обычного Border CornerRadius больше половины высоты даёт вытянутые
/// искажённые фигуры (реальный баг прошлого редизайна), поэтому радиус выставляется по факту
/// размера, а не константой.</summary>
public class PillBorder : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        var r = Math.Min(ActualWidth, ActualHeight) / 2;
        CornerRadius = new CornerRadius(r);
    }
}
