package xyz.gojihub.vpn.ui.util

import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.State
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember

/** Общий приём "приминания" при нажатии для кнопок/карточек-строк по всему приложению —
 *  раньше нажатия были совсем безжизненными (мгновенный ripple и всё). [interactionSource]
 *  нужно передать туда же, откуда фиксируется само нажатие (в Button — параметром
 *  interactionSource, в Row/Box.clickable — тоже параметром interactionSource), иначе scale
 *  не будет знать о состоянии "нажато". */
@Composable
fun rememberPressScale(pressedScale: Float = 0.96f): Pair<MutableInteractionSource, State<Float>> {
    val interactionSource = remember { MutableInteractionSource() }
    val isPressed by interactionSource.collectIsPressedAsState()
    val scale = animateFloatAsState(
        targetValue = if (isPressed) pressedScale else 1f,
        animationSpec = spring(dampingRatio = Spring.DampingRatioMediumBouncy, stiffness = 500f),
        label = "pressScale"
    )
    return interactionSource to scale
}
