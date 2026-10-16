package xyz.gojihub.vpn.ui.settings

import androidx.compose.ui.semantics.Role
import android.content.Intent
import android.provider.Settings
import xyz.gojihub.vpn.BuildConfig
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.PriorityHigh
import xyz.gojihub.vpn.network.NetworkDiagnostics
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import xyz.gojihub.vpn.i18n.AppLanguage
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.settings.PingMethod
import xyz.gojihub.vpn.ui.theme.FontSizePreset
import xyz.gojihub.vpn.ui.theme.NewBadge
import xyz.gojihub.vpn.ui.theme.ThemeMode
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.util.BackButton
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import xyz.gojihub.vpn.ui.theme.godjiCard
import xyz.gojihub.vpn.ui.util.LogViewerDialog
import xyz.gojihub.vpn.ui.util.RichContent
import xyz.gojihub.vpn.ui.util.rememberPressScale
import xyz.gojihub.vpn.util.LogCategory

/** Подсветка строки при нажатии — style-active="background:rgba(127,127,127,.08)" в эталоне. */
private val PressedRowBg = Color(0x147F7F7F)

@Composable
fun SettingsScreen(
    onLoggedOut: () -> Unit,
    onOpenPingSettings: () -> Unit,
    onOpenLogLevel: () -> Unit,
    onOpenAppTunneling: () -> Unit,
    onOpenSupport: () -> Unit,
    onOpenNetworkRules: () -> Unit = {},
    onOpenBypassDomains: () -> Unit = {},
    onOpenJournal: () -> Unit = {},
    viewModel: SettingsViewModel = hiltViewModel()
) {
    val state by viewModel.state.collectAsState()
    val context = LocalContext.current
    var logDialog by remember { mutableStateOf<Pair<LogCategory, String>?>(null) }
    logDialog?.let { (category, title) ->
        LogViewerDialog(category = category, title = title, onDismiss = { logDialog = null })
    }

    // Как в Windows-клиенте: главный экран — карточка со ссылками на разделы, каждый раздел
    // открывается своим окном (стрелка «назад» и системный «назад» возвращают к списку).
    var page by androidx.compose.runtime.saveable.rememberSaveable { mutableStateOf(SettingsPage.MAIN) }
    androidx.activity.compose.BackHandler(enabled = page != SettingsPage.MAIN) { page = SettingsPage.MAIN }

    Column(
        Modifier
            .fillMaxSize()
            .background(GodjiColors.Background)
            .verticalScroll(rememberScrollState())
            .padding(start = 16.dp, top = 16.dp, end = 16.dp, bottom = 12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        if (page == SettingsPage.MAIN) {
            Text(
                Loc.s.settingsTitle, color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily,
                fontWeight = FontWeight.Bold, fontSize = 26.sp, lineHeight = 28.sp, letterSpacing = (-0.6).sp,
                modifier = Modifier.padding(start = 4.dp, end = 4.dp, bottom = 6.dp)
            )
            RefCard {
                LinkRow(Loc.s.settingsAppearance, Loc.f.setAppearanceDesc) { page = SettingsPage.APPEARANCE }
                Hair()
                LinkRow(Loc.s.settingsConnection, Loc.f.setConnectionDesc) { page = SettingsPage.CONNECTION }
                Hair()
                LinkRow(Loc.f.securitySection, Loc.f.setSecurityDesc) { page = SettingsPage.SECURITY }
                Hair()
                LinkRow(Loc.s.settingsNotifications, Loc.f.setNotificationsDesc) { page = SettingsPage.NOTIFICATIONS }
                Hair()
                LinkRow(Loc.s.settingsAbout, Loc.f.setAboutDesc) { page = SettingsPage.ABOUT }
            }
            Spacer(Modifier.height(4.dp))
            RefCard {
                LinkRow(Loc.s.support.settingsSupportLink, Loc.s.support.settingsSupportLinkDesc, onOpenSupport)
            }
            Spacer(Modifier.height(4.dp))
            var confirmLogout by androidx.compose.runtime.saveable.rememberSaveable { mutableStateOf(false) }
            val (outInteraction, outScale) = rememberPressScale()
            Box(
                Modifier
                    .fillMaxWidth()
                    .height(46.dp)
                    .scale(outScale.value)
                    .godjiCard(RoundedCornerShape(24.dp))
                    .clip(RoundedCornerShape(24.dp))
                    .clickable(interactionSource = outInteraction, indication = null, role = Role.Button) {
                        confirmLogout = true
                    },
                contentAlignment = Alignment.Center
            ) {
                Text(Loc.s.settingsLogout, color = GodjiColors.Danger, fontWeight = FontWeight.Bold, fontSize = 14.sp)
            }
            if (confirmLogout) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { confirmLogout = false },
                    title = { Text(Loc.f.logoutConfirmTitle, fontWeight = FontWeight.Bold) },
                    text = { Text(Loc.f.logoutConfirmText, fontSize = 13.5.sp, lineHeight = 19.sp) },
                    confirmButton = {
                        androidx.compose.material3.TextButton(onClick = { confirmLogout = false; viewModel.logout(); onLoggedOut() }) {
                            Text(Loc.f.logoutConfirm, color = GodjiColors.Danger, fontWeight = FontWeight.Bold)
                        }
                    },
                    dismissButton = {
                        androidx.compose.material3.TextButton(onClick = { confirmLogout = false }) { Text(Loc.s.plansDevicesCancel) }
                    }
                )
            }
            // Удаление аккаунта — Google Play требует путь к нему внутри приложения.
            var confirmDelete by androidx.compose.runtime.saveable.rememberSaveable { mutableStateOf(false) }
            LaunchedEffect(Unit) {
                viewModel.accountDeleted.collect {
                    confirmDelete = false
                    android.widget.Toast.makeText(context, Loc.f.deleteAccountSent, android.widget.Toast.LENGTH_LONG).show()
                    onLoggedOut()
                }
            }
            Text(
                Loc.f.deleteAccount, color = GodjiColors.TextSecondary, fontWeight = FontWeight.SemiBold, fontSize = 12.5.sp,
                textAlign = androidx.compose.ui.text.style.TextAlign.Center,
                modifier = Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(16.dp))
                    .clickable { viewModel.clearDeleteAccountError(); confirmDelete = true }
                    .padding(vertical = 10.dp)
            )
            if (confirmDelete || state.deletingAccount) {
                androidx.compose.material3.AlertDialog(
                    onDismissRequest = { if (!state.deletingAccount) { confirmDelete = false; viewModel.clearDeleteAccountError() } },
                    title = { Text(Loc.f.deleteAccountTitle, fontWeight = FontWeight.Bold) },
                    text = {
                        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                            Text(Loc.f.deleteAccountText, fontSize = 13.5.sp, lineHeight = 19.sp)
                            state.deleteAccountError?.let { Text(it, color = GodjiColors.Danger, fontSize = 12.5.sp) }
                        }
                    },
                    confirmButton = {
                        androidx.compose.material3.TextButton(
                            enabled = !state.deletingAccount,
                            onClick = { viewModel.requestAccountDeletion() }
                        ) {
                            if (state.deletingAccount) {
                                androidx.compose.material3.CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp, color = GodjiColors.Danger)
                            } else {
                                Text(Loc.f.deleteAccountConfirm, color = GodjiColors.Danger, fontWeight = FontWeight.Bold)
                            }
                        }
                    },
                    dismissButton = {
                        androidx.compose.material3.TextButton(
                            enabled = !state.deletingAccount,
                            onClick = { confirmDelete = false; viewModel.clearDeleteAccountError() }
                        ) { Text(Loc.f.deleteAccountCancel) }
                    }
                )
            }
            return@Column
        }

        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(bottom = 6.dp)) {
            BackButton(onClick = { page = SettingsPage.MAIN })
            Text(
                when (page) {
                    SettingsPage.APPEARANCE -> Loc.s.settingsAppearance
                    SettingsPage.CONNECTION -> Loc.s.settingsConnection
                    SettingsPage.SECURITY -> Loc.f.securitySection
                    SettingsPage.NOTIFICATIONS -> Loc.s.settingsNotifications
                    SettingsPage.ABOUT -> Loc.s.settingsAbout
                    SettingsPage.MAIN -> ""
                },
                color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.SemiBold, fontSize = 24.sp
            )
        }

        when (page) {
            SettingsPage.APPEARANCE -> RefCard {
                // Тема — три состояния (Светлая/Тёмная/Системная), тем же сегмент-контролом, что и язык.
                SegmentedRow(
                    title = Loc.s.settingsDarkTheme, subtitle = null,
                    options = ThemeMode.entries.map {
                        when (it) {
                            ThemeMode.LIGHT -> Loc.s.themeModeLight
                            ThemeMode.DARK -> Loc.s.themeModeDark
                            ThemeMode.SYSTEM -> Loc.s.themeModeSystem
                        }
                    },
                    selectedIndex = ThemeMode.entries.indexOf(state.themeMode),
                    onSelect = { viewModel.setThemeMode(ThemeMode.entries[it]) }
                )
                Hair()
                SegmentedRow(
                    title = Loc.s.settingsLanguage, subtitle = Loc.s.settingsLanguageDesc,
                    options = AppLanguage.entries.map { it.displayName },
                    selectedIndex = AppLanguage.entries.indexOf(state.language),
                    onSelect = { viewModel.setLanguage(AppLanguage.entries[it]) }
                )
                Hair()
                SegmentedRow(
                    title = Loc.s.settingsFontSize, subtitle = null,
                    options = FontSizePreset.entries.map {
                        when (it) {
                            FontSizePreset.SMALL -> Loc.s.fontSizeSmall
                            FontSizePreset.NORMAL -> Loc.s.fontSizeNormal
                            FontSizePreset.LARGE -> Loc.s.fontSizeLarge
                        }
                    },
                    selectedIndex = FontSizePreset.entries.indexOf(state.fontSize),
                    onSelect = { viewModel.setFontSize(FontSizePreset.entries[it]) }
                )
            }

            SettingsPage.CONNECTION -> {
                RefCard {
                    LinkRow(Loc.f.journalTitle, Loc.f.journalLinkDesc, onOpenJournal)
                    Hair()
                    LinkRow(Loc.f.netRulesTitle, Loc.f.netRulesLinkDesc, onOpenNetworkRules)
                    Hair()
                    LinkRow(Loc.s.settingsPingLink, Loc.s.settingsPingLinkDesc, onOpenPingSettings)
                    Hair()
                    LinkRow(Loc.s.settingsAppTunneling, Loc.s.settingsAppTunnelingDesc, onOpenAppTunneling)
                    Hair()
                    LinkRow(Loc.f.bypassTitle, Loc.f.bypassLinkDesc, onOpenBypassDomains)
                }
                Spacer(Modifier.height(4.dp))
                RefCard {
                    ToggleRow(Loc.s.settingsKillSwitch, Loc.s.settingsKillSwitchDesc, state.killSwitch, viewModel::setKillSwitch)
                    Hair()
                    // Программно включить Always-on VPN нельзя — ограничение Android; открываем
                    // системный экран, где пользователь включает это сам.
                    LinkRow(Loc.s.settingsAlwaysOnTitle, Loc.s.settingsAlwaysOnDesc) {
                        runCatching { context.startActivity(Intent(Settings.ACTION_VPN_SETTINGS)) }
                    }
                }
            }

            SettingsPage.SECURITY -> LeakCheckCard(state, onCheck = viewModel::checkLeak)

            SettingsPage.NOTIFICATIONS -> RefCard {
                ToggleRow(Loc.s.settingsPinNotif, Loc.s.settingsPinNotifDesc, state.pinNotification, viewModel::setPinNotification)
                Hair()
                ToggleRow(Loc.f.hapticsTitle, Loc.f.hapticsDesc, state.haptics, viewModel::setHaptics)
            }

            SettingsPage.ABOUT -> {
                SectionLabel(Loc.s.settingsAboutCopyHint)
                RefCard {
                    AboutRow(Loc.s.settingsAppVersion, state.appVersion)
                    Hair()
                    AboutRow(Loc.s.settingsXrayVersion, state.xrayVersion)
                    Hair()
                    state.subscriptionId?.let {
                        AboutRow(Loc.f.aboutSubscriptionId, it)
                        Hair()
                    }
                    AboutRow(Loc.s.settingsHwid, state.hwid)
                    Hair()
                    AboutRow(Loc.s.settingsDeviceInfo, state.deviceInfo)
                }
                // Обновления — здесь же, рядом с версией (отдельный экран был из одной строки).
                // Самообновление есть только в сборке site (см. app/build.gradle.kts).
                if (BuildConfig.ENABLE_SELF_UPDATE) {
                    SectionLabel(Loc.s.settingsUpdatesTitle)
                    Column(
                        Modifier
                            .fillMaxWidth()
                            .godjiCard(RoundedCornerShape(24.dp))
                            .padding(horizontal = 14.dp, vertical = 10.dp),
                        verticalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        UpdateSectionContent(state, viewModel)
                    }
                }
                SectionLabel(Loc.s.settingsLogs)
                RefCard {
                    LinkRow(Loc.s.settingsLogLevel, Loc.s.settingsLogLevelDesc, onOpenLogLevel)
                    listOf(
                        LogCategory.MAIN to Loc.s.logMain,
                        LogCategory.CORE to Loc.s.logCore,
                        LogCategory.SUBSCRIPTION to Loc.s.logSubscription,
                        LogCategory.SERVICE to Loc.s.logService,
                        LogCategory.PUSH to Loc.s.logPush,
                    ).forEach { (category, title) ->
                        Hair()
                        LogRow(title) { logDialog = category to title }
                    }
                }
            }

            SettingsPage.MAIN -> {}
        }
    }
}

/** Окна настроек (как разделы Windows-клиента). */
private enum class SettingsPage { MAIN, APPEARANCE, CONNECTION, SECURITY, NOTIFICATIONS, ABOUT }

/** Проверка утечек: заголовок + кнопка, после проверки — вердикт (круг 28dp с иконкой, как у
 *  баннеров Главной) и строки деталей в стиле "О программе". */
@Composable
private fun LeakCheckCard(state: SettingsUiState, onCheck: () -> Unit) {
    val report = state.leakReport
    Column(
        Modifier
            .fillMaxWidth()
            .godjiCard(RoundedCornerShape(24.dp))
            .padding(horizontal = 14.dp, vertical = 10.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            RowTexts(Loc.f.leakTitle, Loc.f.leakDesc, Modifier.weight(1f))
            val interaction = remember { MutableInteractionSource() }
            val pressed by interaction.collectIsPressedAsState()
            Box(
                Modifier
                    .height(32.dp)
                    .scale(if (pressed) 0.95f else 1f)
                    .clip(RoundedCornerShape(50))
                    .background(GodjiColors.Chip)
                    .border(1.dp, GodjiColors.CardBorder, RoundedCornerShape(50))
                    .clickable(interactionSource = interaction, indication = null, enabled = !state.leakChecking, onClick = onCheck)
                    .padding(horizontal = 14.dp),
                contentAlignment = Alignment.Center
            ) {
                if (state.leakChecking) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(7.dp)) {
                        CircularProgressIndicator(Modifier.size(13.dp), strokeWidth = 2.dp, color = GodjiColors.Teal, trackColor = GodjiColors.TrackBg)
                        Text(Loc.f.leakChecking, color = GodjiColors.TealDeep, fontWeight = FontWeight.Bold, fontSize = 12.sp)
                    }
                } else {
                    Text(Loc.f.leakButton, color = GodjiColors.TealDeep, fontWeight = FontWeight.Bold, fontSize = 12.sp)
                }
            }
        }
        if (report != null && !state.leakChecking) {
            val ok = report.verdict == NetworkDiagnostics.Verdict.SAFE
            val (title, desc) = when (report.verdict) {
                NetworkDiagnostics.Verdict.SAFE -> Loc.f.leakSafe to Loc.f.leakSafeDesc
                NetworkDiagnostics.Verdict.LEAK -> Loc.f.leakFound to listOfNotNull(
                    Loc.f.leakIpProblem.takeIf { report.ipLeak },
                    (if (report.dnsViaIsp) Loc.f.leakDnsProblem(report.dns?.isp) else Loc.f.leakDnsLocalProblem(report.dns?.isp))
                        .takeIf { report.dnsLeak }
                ).joinToString(" ")
                NetworkDiagnostics.Verdict.VPN_OFF -> Loc.f.leakVpnOff to Loc.f.leakVpnOffDesc
                NetworkDiagnostics.Verdict.ERROR -> Loc.f.leakError to Loc.f.leakErrorDesc
            }
            Row(
                Modifier
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(18.dp))
                    .background(if (ok) GodjiColors.TealTint else GodjiColors.TerracottaTint)
                    .padding(horizontal = 12.dp, vertical = 9.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(11.dp)
            ) {
                Box(
                    Modifier.size(28.dp).clip(CircleShape).background(if (ok) GodjiColors.Teal else GodjiColors.Terracotta),
                    contentAlignment = Alignment.Center
                ) {
                    Icon(if (ok) Icons.Filled.Check else Icons.Filled.PriorityHigh, contentDescription = null, tint = GodjiColors.Surface, modifier = Modifier.size(15.dp))
                }
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Text(title, color = GodjiColors.TextPrimary, fontWeight = FontWeight.Bold, fontSize = 14.sp)
                    Text(desc, color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 11.5.sp, lineHeight = 16.sp)
                }
            }
            report.vpnIp?.let { LeakRow(Loc.f.leakRowSiteIp, listOfNotNull(it.ip, it.country).joinToString(" · ")) }
            report.realIp?.let { LeakRow(Loc.f.leakRowRealIp, listOfNotNull(it.ip, it.country).joinToString(" · ")) }
            report.dns?.let { LeakRow(Loc.f.leakRowDns, listOfNotNull(it.isp, it.country).joinToString(" · ").ifBlank { it.ip }) }
        }
    }
}

@Composable
private fun LeakRow(label: String, value: String) {
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        Text(label, color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 12.5.sp)
        Text(
            value, color = GodjiColors.TextPrimary, fontWeight = FontWeight.SemiBold, fontSize = 12.5.sp,
            modifier = Modifier.weight(1f), textAlign = TextAlign.End, maxLines = 1, overflow = TextOverflow.Ellipsis
        )
    }
}

/** Подпись раздела эталона: 11px/600, трекинг .06em, padding 8 8 0 (плюс общий gap 8 колонки). */
@Composable
internal fun SectionLabel(title: String) {
    Text(
        title.uppercase(), color = GodjiColors.TextSecondary, fontWeight = FontWeight.SemiBold,
        fontSize = 11.sp, letterSpacing = 0.66.sp,
        modifier = Modifier.padding(start = 8.dp, top = 8.dp, end = 8.dp)
    )
}

/** Стеклянная карточка раздела: радиус 24, без внутренних отступов, overflow hidden. */
@Composable
internal fun RefCard(content: @Composable ColumnScope.() -> Unit) {
    Column(
        Modifier
            .fillMaxWidth()
            .godjiCard(RoundedCornerShape(24.dp))
            .clip(RoundedCornerShape(24.dp)),
        content = content
    )
}

@Composable
// M3 Expressive (настройки Android 16): строки группы разделены зазором цвета фона — карточка
// читается как стопка отдельных плашек.
internal fun Hair() = HorizontalDivider(thickness = 2.dp, color = GodjiColors.SurfaceBase)

/** Подпись секции + карточка с внутренним padding 16 — для подэкранов (пинг, логирование). */
@Composable
fun SettingsSection(title: String, content: @Composable ColumnScope.() -> Unit) {
    Column {
        Text(
            title.uppercase(), color = GodjiColors.TextSecondary, fontWeight = FontWeight.SemiBold,
            fontSize = 11.sp, letterSpacing = 0.66.sp,
            modifier = Modifier.padding(start = 8.dp, bottom = 8.dp)
        )
        Column(
            Modifier
                .fillMaxWidth()
                .godjiCard(RoundedCornerShape(24.dp))
                .padding(16.dp)
        ) {
            content()
        }
    }
}

@Composable
internal fun RowTexts(title: String, subtitle: String?, modifier: Modifier = Modifier) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(2.dp)) {
        Text(title, color = GodjiColors.TextPrimary, fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
        subtitle?.let { Text(it, color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 11.sp, lineHeight = 15.sp) }
    }
}

/** Кликабельная строка с подсветкой при нажатии, как кнопки-строки эталона. */
@Composable
internal fun pressableRow(onClick: () -> Unit): Modifier {
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    return Modifier
        .fillMaxWidth()
        .background(if (pressed) PressedRowBg else Color.Transparent)
        .clickable(interactionSource = interaction, indication = null, onClick = onClick)
}

@Composable
internal fun ToggleRow(title: String, subtitle: String?, checked: Boolean, onCheckedChange: (Boolean) -> Unit) {
    Row(
        Modifier
            .fillMaxWidth()
            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { onCheckedChange(!checked) }
            .padding(horizontal = 14.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        RowTexts(title, subtitle, Modifier.weight(1f))
        GlassSwitch(checked) { onCheckedChange(!checked) }
    }
}

/** Переключатель эталона: трек 54×32 (accent / track), бегунок 36×26, left 3 → 15,
 *  пружина cubic-bezier(.3,1.5,.5,1). */
@Composable
internal fun GlassSwitch(checked: Boolean, onToggle: () -> Unit) {
    val track by animateColorAsState(if (checked) GodjiColors.Teal else GodjiColors.TrackBg, tween(300), label = "swTrack")
    val knobX by animateDpAsState(if (checked) 15.dp else 3.dp, spring(dampingRatio = 0.55f, stiffness = 500f), label = "swKnob")
    Box(
        Modifier
            .size(width = 54.dp, height = 32.dp)
            .clip(RoundedCornerShape(50))
            .background(track)
            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null, onClick = onToggle)
    ) {
        Box(
            Modifier
                .offset(x = knobX, y = 3.dp)
                .size(width = 36.dp, height = 26.dp)
                .shadow(3.dp, RoundedCornerShape(50))
                .clip(RoundedCornerShape(50))
                .background(Color.White.copy(alpha = 0.95f))
        )
    }
}

@Composable
internal fun LinkRow(title: String, subtitle: String?, onClick: () -> Unit) {
    Row(
        pressableRow(onClick).padding(horizontal = 14.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        RowTexts(title, subtitle, Modifier.weight(1f))
        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, tint = GodjiColors.TextSecondary, modifier = Modifier.size(15.dp))
    }
}

/** Заголовок (+ подпись) и сегмент-контрол под ним — блок "Язык приложения" эталона:
 *  gap 9, дорожка padding 3 на chip + stroke, бегунок thumb + stroke, кнопки 34px, 12.5px/700. */
@Composable
private fun SegmentedRow(title: String, subtitle: String?, options: List<String>, selectedIndex: Int, onSelect: (Int) -> Unit) {
    Column(
        Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 10.dp),
        verticalArrangement = Arrangement.spacedBy(9.dp)
    ) {
        RowTexts(title, subtitle)
        BoxWithConstraints(
            Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(50))
                .background(GodjiColors.Chip)
                .border(1.dp, GodjiColors.CardBorder, RoundedCornerShape(50))
                .padding(3.dp)
        ) {
            val slotWidth = maxWidth / options.size
            val index = selectedIndex.coerceAtLeast(0)
            val thumbX by animateDpAsState(slotWidth * index, spring(dampingRatio = 0.62f, stiffness = 380f), label = "segThumb")
            Box(
                Modifier
                    .offset(x = thumbX)
                    .width(slotWidth)
                    .height(32.dp)
                    .clip(RoundedCornerShape(50))
                    .background(GodjiColors.Thumb)
                    .border(1.dp, GodjiColors.CardBorder, RoundedCornerShape(50))
            )
            Row(Modifier.fillMaxWidth()) {
                options.forEachIndexed { i, label ->
                    Box(
                        Modifier
                            .weight(1f)
                            .height(32.dp)
                            .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) { onSelect(i) },
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            label,
                            color = if (i == index) GodjiColors.TextPrimary else GodjiColors.TextSecondary,
                            fontWeight = FontWeight.Bold, fontSize = 12.5.sp,
                            maxLines = 1, overflow = TextOverflow.Ellipsis
                        )
                    }
                }
            }
        }
    }
}

/** "Проверить обновления" — капсула 34 на chip + stroke, текст accentInk 12/700, нажатие scale .95. */
@Composable
private fun CheckUpdatesPill(onClick: () -> Unit) {
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    Box(
        Modifier
            .height(32.dp)
            .scale(if (pressed) 0.95f else 1f)
            .clip(RoundedCornerShape(50))
            .background(GodjiColors.Chip)
            .border(1.dp, GodjiColors.CardBorder, RoundedCornerShape(50))
            .clickable(interactionSource = interaction, indication = null, onClick = onClick)
            .padding(horizontal = 14.dp),
        contentAlignment = Alignment.Center
    ) {
        Text(Loc.s.settingsCheckUpdates, color = GodjiColors.TealDeep, fontWeight = FontWeight.Bold, fontSize = 12.sp)
    }
}

@Composable
private fun AboutRow(label: String, value: String) {
    val clipboard = LocalClipboardManager.current
    Row(
        pressableRow { clipboard.setText(AnnotatedString(value)) }.padding(horizontal = 14.dp, vertical = 9.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Text(label, color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 13.sp)
        Text(
            value,
            color = GodjiColors.TextPrimary,
            fontWeight = FontWeight.SemiBold,
            fontSize = 13.sp,
            modifier = Modifier.weight(1f),
            textAlign = TextAlign.End,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis
        )
    }
}

@Composable
private fun LogRow(title: String, onClick: () -> Unit) {
    Row(
        pressableRow(onClick).padding(horizontal = 14.dp, vertical = 9.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Text(title, color = GodjiColors.TextPrimary, fontWeight = FontWeight.Medium, fontSize = 13.5.sp, modifier = Modifier.weight(1f))
        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, tint = GodjiColors.TextSecondary, modifier = Modifier.size(15.dp))
    }
}

@Composable
fun PingMethodRow(label: String, method: PingMethod, selected: PingMethod, onSelect: (PingMethod) -> Unit) {
    Row(
        Modifier
            .fillMaxWidth()
            .clickable { onSelect(method) }
            .padding(vertical = 7.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        RadioButton(
            selected = selected == method,
            onClick = { onSelect(method) },
            colors = RadioButtonDefaults.colors(selectedColor = GodjiColors.TealDeep, unselectedColor = GodjiColors.ButtonBorder)
        )
        Text(label, color = GodjiColors.TextPrimary, fontWeight = FontWeight.Medium, fontSize = 13.sp)
    }
}

/** Блок обновлений по эталону: idle (текст + "Проверить обновления"), проверка (спиннер),
 *  найдено (заголовок + НОВАЯ, changelog на chip, кнопка-градиент 44), загрузка (текст/% + полоса 8). */
@Composable
private fun UpdateSectionContent(state: SettingsUiState, viewModel: SettingsViewModel) {
    when {
        state.updateDownloading -> {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                Text(Loc.s.updateDownloading, color = GodjiColors.TextPrimary, fontWeight = FontWeight.SemiBold, fontSize = 12.5.sp)
                Text("${state.updateDownloadProgress}%", color = GodjiColors.TextSecondary, fontWeight = FontWeight.SemiBold, fontSize = 12.5.sp)
            }
            Box(Modifier.fillMaxWidth().height(8.dp).clip(RoundedCornerShape(50)).background(GodjiColors.TrackBg)) {
                Box(
                    Modifier
                        .fillMaxHeight()
                        .fillMaxWidth((state.updateDownloadProgress / 100f).coerceIn(0f, 1f))
                        .clip(RoundedCornerShape(50))
                        .background(Brush.verticalGradient(0f to GodjiColors.AccentGradTop, 0.55f to GodjiColors.AccentGradMid, 1f to GodjiColors.AccentGradBottom))
                )
            }
        }
        state.updateAvailable != null -> {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(Loc.s.updateAvailableText(state.updateAvailable.version), color = GodjiColors.TextPrimary, fontWeight = FontWeight.Bold, fontSize = 14.sp)
                NewBadge()
            }
            if (state.updateAvailable.changelog.isNotBlank()) {
                Box(
                    Modifier
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(16.dp))
                        .background(GodjiColors.Chip)
                        .padding(horizontal = 12.dp, vertical = 10.dp)
                ) {
                    RichContent(raw = state.updateAvailable.changelog, collapsedBlocks = 5, readMoreLabel = Loc.s.plansNewsReadMore)
                }
            }
            val (interaction, scale) = rememberPressScale()
            Box(
                Modifier
                    .fillMaxWidth()
                    .height(44.dp)
                    .scale(scale.value)
                    .shadow(10.dp, RoundedCornerShape(50), ambientColor = GodjiColors.AccentGlow, spotColor = GodjiColors.AccentGlow)
                    .clip(RoundedCornerShape(50))
                    .background(Brush.verticalGradient(0f to GodjiColors.AccentGradTop, 0.55f to GodjiColors.AccentGradMid, 1f to GodjiColors.AccentGradBottom))
                    .clickable(interactionSource = interaction, indication = null, onClick = viewModel::downloadUpdate),
                contentAlignment = Alignment.Center
            ) {
                Text(Loc.s.updateDownloadInstall, color = GodjiColors.Surface, fontWeight = FontWeight.Bold, fontSize = 13.5.sp)
            }
        }
        state.updateChecking -> {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp, color = GodjiColors.Teal, trackColor = GodjiColors.TrackBg)
                Text(Loc.s.updateChecking, color = GodjiColors.TextSecondary, fontWeight = FontWeight.SemiBold, fontSize = 12.5.sp)
            }
        }
        else -> {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Text(
                    if (state.updateChecked) Loc.s.updateUpToDate else Loc.s.settingsVersionShort(state.appVersion),
                    color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 12.sp,
                    modifier = Modifier.weight(1f)
                )
                CheckUpdatesPill(onClick = viewModel::checkForUpdate)
            }
        }
    }
}
