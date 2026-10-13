package xyz.gojihub.vpn.ui.util

import android.graphics.Bitmap
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.FilterQuality
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import com.google.zxing.BarcodeFormat
import com.google.zxing.EncodeHintType
import com.google.zxing.qrcode.QRCodeWriter
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily

/** QR-код текста в виде ImageBitmap — по модулю на пиксель, масштабируется без сглаживания. */
fun qrBitmap(text: String, quietZone: Int = 1): ImageBitmap {
    val matrix = QRCodeWriter().encode(
        text, BarcodeFormat.QR_CODE, 0, 0,
        mapOf(EncodeHintType.MARGIN to quietZone, EncodeHintType.ERROR_CORRECTION to ErrorCorrectionLevel.M)
    )
    val w = matrix.width
    val h = matrix.height
    val pixels = IntArray(w * h) { i -> if (matrix.get(i % w, i / w)) 0xFF0B1F1C.toInt() else 0xFFFFFFFF.toInt() }
    return Bitmap.createBitmap(pixels, w, h, Bitmap.Config.ARGB_8888).asImageBitmap()
}

/**
 * Диалог с QR-кодом в стиле приложения: карточка, заголовок, пояснение, белая подложка под
 * кодом (сканеры плохо читают код на цветном/прозрачном фоне), подпись и необязательное
 * предупреждение/доп. кнопка.
 */
@Composable
fun QrDialog(
    title: String,
    subtitle: String,
    content: String,
    closeLabel: String,
    onDismiss: () -> Unit,
    caption: String? = null,
    warning: String? = null,
    extraAction: (@Composable () -> Unit)? = null
) {
    val bitmap = remember(content) { qrBitmap(content) }
    Dialog(onDismissRequest = onDismiss) {
        Column(
            Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(28.dp))
                .background(GodjiColors.Surface)
                .border(1.dp, GodjiColors.CardBorder, RoundedCornerShape(28.dp))
                .padding(20.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            Text(title, color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.Bold, fontSize = 20.sp, textAlign = TextAlign.Center)
            Text(subtitle, color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 12.sp, lineHeight = 17.sp, textAlign = TextAlign.Center)
            Box(
                Modifier
                    .size(236.dp)
                    .clip(RoundedCornerShape(20.dp))
                    .background(Color.White)
                    .border(1.dp, GodjiColors.CardBorder, RoundedCornerShape(20.dp))
                    .padding(14.dp),
                contentAlignment = Alignment.Center
            ) {
                Image(bitmap, contentDescription = null, filterQuality = FilterQuality.None, modifier = Modifier.fillMaxSize())
            }
            caption?.let {
                Text(it, color = GodjiColors.TextPrimary, fontWeight = FontWeight.SemiBold, fontSize = 12.sp, maxLines = 2, textAlign = TextAlign.Center)
            }
            warning?.let {
                Text(
                    it, color = GodjiColors.Danger, fontWeight = FontWeight.SemiBold, fontSize = 11.5.sp, lineHeight = 16.sp,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.clip(RoundedCornerShape(14.dp)).background(GodjiColors.TerracottaTint).padding(horizontal = 12.dp, vertical = 9.dp)
                )
            }
            extraAction?.invoke()
            Box(
                Modifier
                    .fillMaxWidth()
                    .height(44.dp)
                    .clip(RoundedCornerShape(50))
                    .background(GodjiColors.Chip)
                    .border(1.dp, GodjiColors.CardBorder, RoundedCornerShape(50))
                    .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null, onClick = onDismiss),
                contentAlignment = Alignment.Center
            ) {
                Text(closeLabel, color = GodjiColors.TextPrimary, fontWeight = FontWeight.Bold, fontSize = 13.5.sp)
            }
        }
    }
}
