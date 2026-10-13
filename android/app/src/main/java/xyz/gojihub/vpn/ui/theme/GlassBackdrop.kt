package xyz.gojihub.vpn.ui.theme

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier

/**
 * Общий фон приложения — рисуется один раз под NavHost (MainActivity) и под LoginScreen.
 * «Goji Expressive» (Material 3 Expressive): сплошной surface. Анимированные пятна, кольца и
 * сетка точек v5 убраны — они перерисовывали весь экран на каждом кадре.
 * Имя и сигнатура прежние, чтобы не трогать места вызова.
 */
@Composable
fun GlassBackdrop(
    modifier: Modifier = Modifier,
    @Suppress("UNUSED_PARAMETER") connected: Boolean = false,
    content: @Composable BoxScope.() -> Unit
) {
    Box(modifier.fillMaxSize().background(GodjiColors.BackgroundSolid), content = content)
}
