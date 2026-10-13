package xyz.gojihub.vpn.ui.theme

import androidx.compose.animation.core.*
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/*
 * Бейджи «Goji Expressive» — статичные тональные таблетки Material 3 Expressive. В v5 они
 * анимировались бесконечно (комета по рамке, вращение, перелив) и перекомпоновывались на
 * каждом кадре — одна из причин лагов на главном экране и в «Подписке».
 */

@Composable
private fun Pill(bg: Color, height: androidx.compose.ui.unit.Dp, content: @Composable RowScope.() -> Unit) {
    Row(
        Modifier.height(height).clip(RoundedCornerShape(50)).background(bg).padding(horizontal = 10.dp),
        verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp),
        content = content
    )
}

@Composable
fun ActiveBadge(label: String = "АКТИВНА") {
    Pill(GodjiColors.Teal, 26.dp) {
        Box(Modifier.size(7.dp).background(GodjiColors.Surface, CircleShape))
        Text(label, color = GodjiColors.Surface, fontSize = 10.sp, fontWeight = FontWeight.ExtraBold, letterSpacing = 0.8.sp)
    }
}

@Composable
fun AutoBadge(label: String = "АВТО") {
    Pill(GodjiColors.SecondaryContainer, 24.dp) {
        Text("⟳", color = GodjiColors.OnSecondaryContainer, fontSize = 12.sp)
        Text(label, color = GodjiColors.OnSecondaryContainer, fontSize = 9.5.sp, fontWeight = FontWeight.ExtraBold, letterSpacing = 0.8.sp)
    }
}

/** «Текущий» / «Новая» — tertiaryContainer (reverse — primaryContainer). */
@Composable
fun HoloBadge(label: String, icon: String = "★", reverse: Boolean = false) {
    val bg = if (reverse) GodjiColors.PrimaryContainer else GodjiColors.TertiaryContainer
    val fg = if (reverse) GodjiColors.OnPrimaryContainer else GodjiColors.OnTertiaryContainer
    Pill(bg, 22.dp) {
        Text(icon, color = fg, fontSize = 9.sp)
        Text(label, color = fg, fontSize = 9.5.sp, fontWeight = FontWeight.ExtraBold)
    }
}

@Composable fun CurrentBadge() = HoloBadge("Текущий", "★")
@Composable fun NewBadge() = HoloBadge("НОВАЯ", "✦", reverse = true)
