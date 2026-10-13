package xyz.gojihub.vpn.ui.util

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.Icon
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.scale
import androidx.compose.ui.unit.dp
import xyz.gojihub.vpn.ui.theme.GodjiColors

/** Единая кнопка "назад" в шапках подэкранов (Настройки пинга/логов, Поддержка, тикет,
 *  Проксируемые приложения) — раньше в каждом файле был свой Text("←", ...) с копипастой
 *  rememberPressScale/clickable; теперь одна векторная AutoMirrored-иконка (зеркалится в
 *  RTL-раскладках, чего текстовая стрелка не умела) и общий composable. */
@Composable
fun BackButton(onClick: () -> Unit) {
    val (interaction, scale) = rememberPressScale()
    Icon(
        imageVector = Icons.AutoMirrored.Filled.ArrowBack,
        contentDescription = null,
        tint = GodjiColors.TextPrimary,
        modifier = Modifier
            .scale(scale.value)
            .clickable(interactionSource = interaction, indication = null, onClick = onClick)
            .padding(end = 10.dp)
    )
}
