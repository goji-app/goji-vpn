package xyz.gojihub.vpn.ui.theme

import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Outline
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.LayoutDirection
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.min
import kotlin.math.sin

/**
 * «Печенье» Material 3 Expressive (MaterialShapes.Cookie9Sided и родня): круг с [lobes]
 * мягкими волнами по краю. [depth] — глубина волны в долях радиуса: 0 — ровный круг, так что
 * анимация depth плавно превращает печенье в круг и обратно (shape morphing M3E без
 * библиотеки graphics-shapes). [rotation] — поворот в градусах.
 */
class CookieShape(
    private val lobes: Int = 9,
    private val depth: Float = 0.08f,
    private val rotation: Float = 0f
) : Shape {
    override fun createOutline(size: Size, layoutDirection: LayoutDirection, density: Density): Outline {
        val cx = size.width / 2f
        val cy = size.height / 2f
        // Внешний край волны касается границ элемента.
        val r = min(cx, cy) / (1f + depth)
        val steps = 180
        val rot = rotation / 180f * PI.toFloat()
        val path = Path()
        for (i in 0..steps) {
            val t = i / steps.toFloat() * 2f * PI.toFloat()
            val rr = r * (1f + depth * cos(lobes * t))
            val x = cx + rr * cos(t + rot)
            val y = cy + rr * sin(t + rot)
            if (i == 0) path.moveTo(x, y) else path.lineTo(x, y)
        }
        path.close()
        return Outline.Generic(path)
    }

    override fun equals(other: Any?) =
        other is CookieShape && other.lobes == lobes && other.depth == depth && other.rotation == rotation

    override fun hashCode() = (lobes * 31 + depth.hashCode()) * 31 + rotation.hashCode()
}
