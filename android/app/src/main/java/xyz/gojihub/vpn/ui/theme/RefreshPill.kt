package xyz.gojihub.vpn.ui.theme

import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.scale
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** Подписанная кнопка «Обновить» в шапке «Серверов» и «Подписки» (2026-10-16) — одна вместо
 *  круглых значков без подписи; такая же на Windows и macOS. */
@Composable
fun RefreshPill(label: String, busy: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier) {
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    Row(
        modifier
            .scale(if (pressed) 0.95f else 1f)
            .heightIn(min = 40.dp)
            .godjiGlassPill()
            .clickable(interactionSource = interaction, indication = null, role = Role.Button, onClick = onClick)
            .padding(horizontal = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp)
    ) {
        if (busy) CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp, color = GodjiColors.TealDeep, trackColor = GodjiColors.TrackBg)
        else Icon(Icons.Filled.Refresh, contentDescription = null, tint = GodjiColors.TealDeep, modifier = Modifier.size(16.dp))
        Text(label, color = GodjiColors.TealDeep, fontWeight = FontWeight.Bold, fontSize = 13.sp)
    }
}
