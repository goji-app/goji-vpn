package xyz.gojihub.vpn.ui.theme

import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawWithCache
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.drawOutline
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.dp

/**
 * Поверхности «Goji Expressive» (Material 3 Expressive): плотная тональная заливка нужного
 * уровня surfaceContainer, без теней, бликов, кромок и размытия. Рисуется один раз и не
 * перерисовывается сама по себе — у стекла v5 бегающий блик и тени setShadowLayer
 * перерисовывали каждую карточку на каждом кадре, отсюда и лаги.
 *
 * Сигнатуры godjiCard(shape, tint, borderColor) и остальных — как в v5, экраны не меняются:
 *  • tint == Surface/Chip/SurfaceGlass (дефолтные) → обычная карточка (surfaceContainer);
 *  • любой другой tint (TealTint, TerracottaTint, JamBg…) → заливка этим цветом;
 *  • borderColor == Teal/TealTintBorder/TealDeep → акцентная обводка 2dp (выбранный сервер,
 *    текущий тариф); остальные цвета обводки игнорируются.
 */
private fun Modifier.tonalSurface(shape: Shape, fill: () -> Color, accentBorder: Boolean): Modifier = drawWithCache {
    val outline = shape.createOutline(size, layoutDirection, this)
    val stroke = Stroke(2.dp.toPx())
    onDrawBehind {
        drawOutline(outline, fill())
        if (accentBorder) drawOutline(outline, GodjiColors.Teal, style = stroke)
    }
}

private fun isNeutralTint(c: Color) =
    c == GodjiColors.Surface || c == GodjiColors.Chip || c == GodjiColors.SurfaceGlass || c == Color.Transparent

private fun isAccentBorder(c: Color) =
    c == GodjiColors.Teal || c == GodjiColors.TealTintBorder || c == GodjiColors.TealDeep || c == GodjiColors.TealBright

/** Карточка-модуль. Сигнатура совместима с 1.0.77. */
fun Modifier.godjiCard(
    shape: Shape = RoundedCornerShape(24.dp),
    tint: Color = GodjiColors.Surface,
    borderColor: Color = GodjiColors.CardBorder
): Modifier = tonalSurface(
    shape,
    if (isNeutralTint(tint)) ({ GodjiColors.Glass }) else ({ tint }),
    isAccentBorder(borderColor)
)

/** Лист входа, тост — уровень выше карточки (surfaceContainerHigh). */
fun Modifier.godjiGlassStrong(shape: Shape = RoundedCornerShape(28.dp)): Modifier =
    tonalSurface(shape, { GodjiColors.GlassStrong }, false)

/** Круглые кнопки, плашка «Goji», сетевой статус, чипы. */
fun Modifier.godjiGlassPill(shape: Shape = RoundedCornerShape(50), tint: Color? = null): Modifier =
    tonalSurface(shape, if (tint != null) ({ tint }) else ({ GodjiColors.SurfaceContainerHigh }), false)

/** Строка/чип ВНУТРИ карточки (surfaceContainerHighest). */
fun Modifier.godjiGlassFlat(shape: Shape = RoundedCornerShape(16.dp)): Modifier =
    tonalSurface(shape, { GodjiColors.GlassFaint }, false)
