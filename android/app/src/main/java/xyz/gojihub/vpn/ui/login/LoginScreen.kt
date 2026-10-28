package xyz.gojihub.vpn.ui.login

import android.content.Intent
import android.net.Uri
import xyz.gojihub.vpn.BuildConfig
import androidx.compose.foundation.Image
import androidx.compose.foundation.LocalIndication
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.layout.layout
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.unit.Constraints
import xyz.gojihub.vpn.R
import xyz.gojihub.vpn.ui.theme.godjiGlassPill
import androidx.compose.foundation.verticalScroll
import androidx.compose.ui.draw.scale
import xyz.gojihub.vpn.ui.util.rememberPressScale
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.activity.compose.rememberLauncherForActivityResult
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.LinkAnnotation
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.withLink
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.ui.globe.GojiGlobe
import xyz.gojihub.vpn.ui.theme.GlassBackdrop
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import xyz.gojihub.vpn.ui.theme.godjiGlassFlat
import xyz.gojihub.vpn.ui.theme.godjiGlassStrong

@Composable
fun LoginScreen(
    onCodeSent: (String) -> Unit,
    viewModel: LoginViewModel = hiltViewModel()
) {
    val state by viewModel.state.collectAsState()
    val context = LocalContext.current

    val qrScanner = rememberLauncherForActivityResult(ScanContract()) { result ->
        val text = result.contents ?: return@rememberLauncherForActivityResult
        viewModel.loginWithTransfer(text, onCodeSent)
    }

    GlassBackdrop {
      // Эталон: колонка padding 16/18/20, gap 12 — плашка "Goji", глобус со спутниками
      // (flex:1, min-height 250, холст вынесен на −20dp по вертикали и −18dp по бокам),
      // стеклянный лист снизу.
      Column(
        Modifier.fillMaxSize().padding(start = 18.dp, top = 16.dp, end = 18.dp, bottom = 20.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
      ) {
        Row(
            Modifier.godjiGlassPill().padding(start = 5.dp, top = 5.dp, end = 13.dp, bottom = 5.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(9.dp)
        ) {
            Box(Modifier.size(30.dp).clip(CircleShape).background(GodjiColors.Ink), contentAlignment = Alignment.Center) {
                Image(painterResource(R.drawable.ic_notification), contentDescription = null, modifier = Modifier.size(19.dp))
            }
            Text("Goji", color = GodjiColors.TextPrimary, fontWeight = FontWeight.Bold, fontSize = 15.sp)
        }

        Box(Modifier.fillMaxWidth().weight(1f).heightIn(min = 250.dp)) {
            GojiGlobe(
                status = "off", node = null, satellites = true,
                modifier = Modifier.layout { measurable, constraints ->
                    val dx = 18.dp.roundToPx()
                    val dy = 20.dp.roundToPx()
                    val placeable = measurable.measure(
                        Constraints.fixed(constraints.maxWidth + dx * 2, constraints.maxHeight + dy * 2)
                    )
                    layout(constraints.maxWidth, constraints.maxHeight) { placeable.place(-dx, -dy) }
                }
            )
        }

        // verticalScroll — на невысоких экранах (особенно в режиме email с показанной
        // ошибкой) лист не помещается по высоте целиком.
        Column(
            Modifier
                .fillMaxWidth()
                .godjiGlassStrong(RoundedCornerShape(32.dp))
                .padding(start = 16.dp, top = 20.dp, end = 16.dp, bottom = 16.dp)
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            Text(
                Loc.s.loginGreeting,
                color = GodjiColors.TextPrimary,
                fontFamily = SpaceGroteskFamily,
                fontWeight = FontWeight.SemiBold,
                fontSize = 30.sp,
                lineHeight = 32.sp
            )

            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                if (!state.emailMode) {
                    // Вход только по email-коду (и QR-переносом ниже): приложение распространяется
                    // через Google Play, вход через сайт и сторонние сервисы убраны.
                    val (emailInteraction, emailScale) = rememberPressScale()
                    Button(
                        onClick = { viewModel.toggleEmailMode(true) },
                        enabled = !state.loading,
                        interactionSource = emailInteraction,
                        colors = ButtonDefaults.buttonColors(containerColor = GodjiColors.Ink),
                        shape = RoundedCornerShape(50),
                        modifier = Modifier.fillMaxWidth().height(54.dp).scale(emailScale.value)
                    ) {
                        Text(
                            Loc.s.loginEmailMode,
                            color = GodjiColors.Surface, fontWeight = FontWeight.Bold, fontSize = 15.sp
                        )
                    }

                    // Перенос входа со старого устройства: там Подписка → "Перенести на другое
                    // устройство" показывает QR, здесь его сканируем — без почты и паролей.
                    TextButton(
                        onClick = {
                            qrScanner.launch(
                                ScanOptions()
                                    .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                                    .setPrompt(Loc.f.loginQrPrompt)
                                    .setBeepEnabled(false)
                                    .setOrientationLocked(true)
                            )
                        },
                        enabled = !state.loading,
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Icon(Icons.Filled.QrCodeScanner, contentDescription = null, tint = GodjiColors.TealDeep, modifier = Modifier.size(17.dp))
                        Spacer(Modifier.width(7.dp))
                        Text(Loc.f.loginByQr, color = GodjiColors.TealDeep, fontWeight = FontWeight.SemiBold, fontSize = 13.sp)
                    }
                } else {
                    OutlinedTextField(
                        value = state.email,
                        onValueChange = viewModel::onEmailChange,
                        label = { Text(Loc.s.loginEmailLabel) },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                    val (otpInteraction, otpScale) = rememberPressScale()
                    Button(
                        onClick = { viewModel.sendOtp(onSent = onCodeSent) },
                        enabled = !state.loading,
                        interactionSource = otpInteraction,
                        colors = ButtonDefaults.buttonColors(containerColor = GodjiColors.Teal),
                        shape = RoundedCornerShape(50),
                        modifier = Modifier.fillMaxWidth().height(54.dp).scale(otpScale.value)
                    ) { Text(if (state.loading) Loc.s.loginSendingOtp else Loc.s.loginGetCode, color = GodjiColors.Surface, fontWeight = FontWeight.Bold, fontSize = 15.sp) }
                    TextButton(onClick = { viewModel.toggleEmailMode(false) }, modifier = Modifier.fillMaxWidth()) {
                        Text(Loc.s.loginOtherMethods, color = GodjiColors.TextSecondary, fontSize = 12.sp)
                    }
                }

                state.error?.let {
                    Text(it, color = GodjiColors.Danger, fontSize = 12.sp, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth())
                }

                Text(
                    Loc.s.loginNoAccount,
                    color = GodjiColors.TextSecondary,
                    fontSize = 10.5.sp,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.fillMaxWidth().padding(top = 4.dp)
                )

                // Те же два документа и та же формулировка, что и на сайте кабинета — раньше
                // приложение отправляло согласие на бэкенд молча, не показывая пользователю
                // ни текста, ни самих документов.
                val consentText = remember(Loc.lang) {
                    buildAnnotatedString {
                        append(Loc.s.loginConsentPrefix)
                        withLink(LinkAnnotation.Url("https://telegra.ph/Polzovatelskoe-soglashenie-Goji-VPN-09-20")) {
                            withStyle(SpanStyle(color = GodjiColors.TealDeep, fontWeight = FontWeight.SemiBold)) {
                                append(Loc.s.loginConsentTerms)
                            }
                        }
                        append(Loc.s.loginConsentAnd)
                        withLink(LinkAnnotation.Url("https://telegra.ph/Politika-konfidencialnosti-Goji-VPN-09-20")) {
                            withStyle(SpanStyle(color = GodjiColors.TealDeep, fontWeight = FontWeight.SemiBold)) {
                                append(Loc.s.loginConsentPrivacy)
                            }
                        }
                        append(Loc.s.loginConsentSuffix)
                    }
                }
                Text(
                    consentText,
                    color = GodjiColors.TextSecondary,
                    fontSize = 10.5.sp,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.fillMaxWidth()
                )
            }
        }
      }
    }
}
