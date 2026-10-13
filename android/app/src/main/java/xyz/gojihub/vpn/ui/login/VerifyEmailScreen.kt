package xyz.gojihub.vpn.ui.login

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import xyz.gojihub.vpn.auth.AuthRepository
import xyz.gojihub.vpn.auth.AuthResult
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.JetBrainsMonoFamily
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import kotlin.math.cos
import kotlin.math.sin
import kotlin.random.Random
import javax.inject.Inject

data class VerifyUiState(val code: String = "", val loading: Boolean = false, val error: String? = null, val success: Boolean = false)

private const val OTP_LENGTH = 6

@HiltViewModel
class VerifyEmailViewModel @Inject constructor(
    private val authRepository: AuthRepository
) : ViewModel() {
    private val _state = MutableStateFlow(VerifyUiState())
    val state: StateFlow<VerifyUiState> = _state

    /** Автопроверка сразу по вводу последней цифры (см. VerifyEmailScreen, "It'll auto-verify
     *  once entered" из референса) — код меняется только отсюда, поэтому onCodeChange остаётся
     *  единственным местом, откуда стоит запускать verify(), а не дублировать условие
     *  "длина == 6" ещё и в Compose. */
    fun onCodeChange(v: String, email: String, onSuccess: () -> Unit) {
        val digits = v.filter { it.isDigit() }.take(OTP_LENGTH)
        _state.value = _state.value.copy(code = digits, error = null)
        if (digits.length == OTP_LENGTH) verify(email, onSuccess)
    }

    fun verify(email: String, onSuccess: () -> Unit) {
        val code = _state.value.code
        if (code.length != OTP_LENGTH || _state.value.loading) return
        _state.value = _state.value.copy(loading = true, error = null)
        viewModelScope.launch {
            when (val r = authRepository.verifyOtp(email, code)) {
                is AuthResult.Success -> _state.value = _state.value.copy(loading = false, success = true)
                // Очищаем код при ошибке — так же, как в референсе идея "auto-verify": раз
                // проверка происходит автоматически по факту заполнения, оставлять неверные
                // цифры в клетках после отказа сервера уже некуда — только заново перепечатывать
                // поверх них, что запутывает. Пустые клетки читаются как явное приглашение
                // ввести код ещё раз.
                is AuthResult.Error -> _state.value = _state.value.copy(loading = false, error = r.message, code = "")
            }
        }
    }
}

@Composable
fun VerifyEmailScreen(
    email: String,
    onVerified: () -> Unit,
    viewModel: VerifyEmailViewModel = hiltViewModel()
) {
    val state by viewModel.state.collectAsState()

    // Успех показываем как отдельный полноэкранный кадр (чекмарк с разлетающимися частицами,
    // см. VerifySuccessAnimation) и лишь затем уходим дальше — короткая пауза, чтобы анимация
    // реально успела доиграть, а не была обрезана мгновенным переходом.
    LaunchedEffect(state.success) {
        if (state.success) {
            delay(1300)
            onVerified()
        }
    }

    Column(
        modifier = Modifier.fillMaxSize().background(GodjiColors.Background).padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Spacer(Modifier.height(40.dp))

        AnimatedContent(
            targetState = state.success,
            transitionSpec = { fadeIn(tween(250)) togetherWith fadeOut(tween(150)) },
            label = "verifyContent"
        ) { success ->
            if (success) {
                VerifySuccessContent()
            } else {
                VerifyFormContent(email = email, state = state, viewModel = viewModel, onVerified = onVerified)
            }
        }
    }
}

@Composable
private fun VerifyFormContent(
    email: String,
    state: VerifyUiState,
    viewModel: VerifyEmailViewModel,
    onVerified: () -> Unit
) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(Loc.s.verifyTitle, color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.SemiBold, fontSize = 18.sp)
        Spacer(Modifier.height(6.dp))
        Text(Loc.s.verifySentTo(email), color = GodjiColors.TextMuted, fontSize = 12.sp, textAlign = TextAlign.Center)

        Spacer(Modifier.height(28.dp))
        OtpCodeInput(
            code = state.code,
            hasError = state.error != null,
            enabled = !state.loading,
            onCodeChange = { viewModel.onCodeChange(it, email, onVerified) }
        )

        Spacer(Modifier.height(14.dp))
        Text(
            if (state.loading) Loc.s.verifyChecking else Loc.s.verifyAutoHint,
            color = GodjiColors.TextMuted,
            fontSize = 11.sp
        )

        state.error?.let {
            Spacer(Modifier.height(8.dp))
            Text(it, color = GodjiColors.Danger, fontSize = 12.sp, textAlign = TextAlign.Center)
        }
    }
}

/**
 * Шесть отдельных клеток вместо одного текстового поля — то же ощущение, что у OTP-вводов в
 * банковских/мессенджер-приложениях (по мотивам присланного референса с "пакман"-переходом
 * между клетками): реальный ввод идёт через невидимое BasicTextField, растянутое поверх всего
 * ряда клеток (стандартный приём для такого UI в Compose — сам textfield никогда не виден,
 * визуальные клетки просто отражают его текущее value), а видимая часть — только клетки ниже.
 * "Пакмана" не копируем буквально (не наш бренд) — вместо него у только что заполненной клетки
 * короткая вспышка масштаба и cвечения акцентным тилом, тот же "photon bloom" приём, что уже
 * используется у кнопки подключения и на глобусе.
 */
@Composable
private fun OtpCodeInput(code: String, hasError: Boolean, enabled: Boolean, onCodeChange: (String) -> Unit) {
    val focusRequester = remember { FocusRequester() }
    LaunchedEffect(Unit) { focusRequester.requestFocus() }

    Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Row(horizontalArrangement = Arrangement.spacedBy(9.dp)) {
            repeat(OTP_LENGTH) { i ->
                DigitCell(
                    digit = code.getOrNull(i),
                    isActive = enabled && i == code.length,
                    justFilled = i == code.length - 1,
                    hasError = hasError
                )
            }
        }
        BasicTextField(
            value = code,
            onValueChange = onCodeChange,
            enabled = enabled,
            singleLine = true,
            textStyle = androidx.compose.ui.text.TextStyle(color = Color.Transparent),
            cursorBrush = androidx.compose.ui.graphics.SolidColor(Color.Transparent),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
            modifier = Modifier
                .fillMaxWidth()
                .height(52.dp)
                .focusRequester(focusRequester)
                .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) {
                    focusRequester.requestFocus()
                }
        )
    }
}

@Composable
private fun DigitCell(digit: Char?, isActive: Boolean, justFilled: Boolean, hasError: Boolean) {
    // Короткая вспышка (масштаб + свечение) ровно один раз в момент заполнения именно ЭТОЙ
    // клетки — retrigger только когда клетка реально только что стала последней заполненной
    // (не на каждой рекомпозиции), поэтому ключ — сама пара (digit, justFilled).
    val burst = remember { Animatable(0f) }
    LaunchedEffect(digit, justFilled) {
        if (digit != null && justFilled) {
            burst.snapTo(1f)
            burst.animateTo(0f, tween(380, easing = FastOutSlowInEasing))
        }
    }

    val cursorAlpha = if (isActive) {
        val transition = rememberInfiniteTransitionAlpha()
        transition
    } else 0f

    val borderColor = when {
        hasError -> GodjiColors.Danger
        isActive -> GodjiColors.Teal
        else -> GodjiColors.CardBorder
    }
    val borderWidth = 1.5.dp
    val glowAlpha = burst.value * 0.9f

    Box(
        Modifier
            .size(44.dp)
            .scale(1f + burst.value * 0.12f)
            .shadow(
                elevation = (burst.value * 14).dp,
                shape = RoundedCornerShape(13.dp),
                ambientColor = GodjiColors.Teal.copy(alpha = glowAlpha),
                spotColor = GodjiColors.Teal.copy(alpha = glowAlpha)
            )
            .clip(RoundedCornerShape(13.dp))
            .background(GodjiColors.Chip)
            .border(borderWidth, borderColor, RoundedCornerShape(13.dp)),
        contentAlignment = Alignment.Center
    ) {
        if (digit != null) {
            Text(digit.toString(), color = GodjiColors.TextPrimary, fontFamily = JetBrainsMonoFamily, fontWeight = FontWeight.Bold, fontSize = 19.sp)
        } else if (isActive) {
            Box(
                Modifier
                    .width(2.dp)
                    .height(20.dp)
                    .alpha(cursorAlpha)
                    .background(GodjiColors.Teal, RoundedCornerShape(1.dp))
            )
        }
    }
}

@Composable
private fun rememberInfiniteTransitionAlpha(): Float {
    val transition = rememberInfiniteTransition(label = "otpCursor")
    val alpha by transition.animateFloat(
        initialValue = 1f, targetValue = 0f,
        animationSpec = infiniteRepeatable(tween(650, easing = LinearEasing), repeatMode = RepeatMode.Reverse),
        label = "otpCursorAlpha"
    )
    return alpha
}

private data class SuccessParticle(val angleDeg: Float, val distanceDp: Float, val sizeDp: Float, val delayFraction: Float, val color: Color)

/** Финальный кадр после успешной проверки (по мотивам "Verified Successfully" из референса) —
 *  светящийся круг с векторной галочкой (Icons.Filled.Check) и разлетающиеся вокруг него
 *  частицы-искры, вместо статичной иконки. */
@Composable
private fun VerifySuccessContent() {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(Loc.s.verifySuccessTitle, color = GodjiColors.Teal, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.SemiBold, fontSize = 19.sp)
        Spacer(Modifier.height(6.dp))
        Text(Loc.s.verifySuccessSubtitle, color = GodjiColors.TextMuted, fontSize = 12.sp, textAlign = TextAlign.Center)
        Spacer(Modifier.height(36.dp))
        VerifySuccessBadge()
    }
}

@Composable
private fun VerifySuccessBadge() {
    val progress = remember { Animatable(0f) }
    LaunchedEffect(Unit) { progress.animateTo(1f, tween(900, easing = FastOutSlowInEasing)) }

    val particles = remember {
        List(16) {
            SuccessParticle(
                angleDeg = Random.nextFloat() * 360f,
                distanceDp = 46f + Random.nextFloat() * 44f,
                sizeDp = 3f + Random.nextFloat() * 5f,
                delayFraction = Random.nextFloat() * 0.35f,
                color = if (Random.nextBoolean()) GodjiColors.Teal else GodjiColors.TealDeep
            )
        }
    }
    val density = LocalDensity.current

    Box(Modifier.size(150.dp), contentAlignment = Alignment.Center) {
        particles.forEach { p ->
            val t = ((progress.value - p.delayFraction) / (1f - p.delayFraction)).coerceIn(0f, 1f)
            val eased = FastOutSlowInEasing.transform(t)
            val alpha = 1f - eased
            if (alpha > 0.01f) {
                val distancePx = with(density) { p.distanceDp.dp.toPx() }
                val rad = Math.toRadians(p.angleDeg.toDouble())
                val dx = (cos(rad) * distancePx * eased).toFloat()
                val dy = (sin(rad) * distancePx * eased).toFloat()
                Box(
                    Modifier
                        .offset(x = with(density) { dx.toDp() }, y = with(density) { dy.toDp() })
                        .size(p.sizeDp.dp)
                        .alpha(alpha)
                        .clip(RoundedCornerShape(2.dp))
                        .background(p.color)
                )
            }
        }

        val badgeScale = 0.6f + 0.4f * FastOutSlowInEasing.transform(progress.value.coerceIn(0f, 1f))
        Box(
            Modifier
                .size(88.dp)
                .scale(badgeScale)
                .shadow(28.dp, CircleShape, ambientColor = GodjiColors.Teal, spotColor = GodjiColors.Teal)
                .clip(CircleShape)
                .background(GodjiColors.Teal),
            contentAlignment = Alignment.Center
        ) {
            Icon(Icons.Filled.Check, contentDescription = null, tint = GodjiColors.Surface, modifier = Modifier.size(44.dp))
        }
    }
}
