package xyz.gojihub.vpn.ui.util

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.util.AppLogger
import xyz.gojihub.vpn.util.LogCategory

/** Просто открывает журнал приложения (конкретной категории — см. LogCategory) на
 *  просмотр/копирование — без отправки куда-либо (раньше тут был "поделиться" файлом через
 *  системное меню, отдельно попросили убрать). */
@Composable
fun LogViewerDialog(category: LogCategory, title: String, onDismiss: () -> Unit) {
    val context = LocalContext.current
    val clipboard = LocalClipboardManager.current
    val logText = remember(category) { AppLogger.readAll(context, category) }

    Dialog(onDismissRequest = onDismiss) {
        Surface(shape = RoundedCornerShape(24.dp), color = GodjiColors.BackgroundSolid) {
            Column(Modifier.padding(18.dp)) {
                Row(
                    Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(title, color = GodjiColors.TextPrimary, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                    Icon(
                        Icons.Filled.Close,
                        contentDescription = null,
                        tint = GodjiColors.TextSecondary,
                        modifier = Modifier.size(18.dp).clickable(onClick = onDismiss).padding(2.dp)
                    )
                }
                Spacer(Modifier.height(10.dp))
                Box(
                    Modifier
                        .fillMaxWidth()
                        .heightIn(max = 460.dp)
                        .background(GodjiColors.Chip, RoundedCornerShape(12.dp))
                        .padding(12.dp)
                ) {
                    SelectionContainer(Modifier.verticalScroll(rememberScrollState())) {
                        Text(
                            logText,
                            color = GodjiColors.TextPrimary,
                            fontFamily = FontFamily.Monospace,
                            fontSize = 10.5.sp,
                            lineHeight = 14.sp
                        )
                    }
                }
                Spacer(Modifier.height(12.dp))
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                    TextButton(onClick = { clipboard.setText(AnnotatedString(logText)) }) {
                        Text(Loc.s.logCopyAll, color = GodjiColors.TealDeep, fontWeight = FontWeight.SemiBold, fontSize = 13.sp)
                    }
                    TextButton(onClick = onDismiss) {
                        Text(Loc.s.logClose, color = GodjiColors.TextSecondary, fontWeight = FontWeight.SemiBold, fontSize = 13.sp)
                    }
                }
            }
        }
    }
}
