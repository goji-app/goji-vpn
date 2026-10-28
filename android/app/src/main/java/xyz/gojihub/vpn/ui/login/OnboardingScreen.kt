package xyz.gojihub.vpn.ui.login

import android.content.Context
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.layout.layout
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import xyz.gojihub.vpn.R
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.ui.globe.GojiGlobe
import xyz.gojihub.vpn.ui.theme.GlassBackdrop
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import xyz.gojihub.vpn.ui.theme.godjiGlassPill
import xyz.gojihub.vpn.ui.theme.godjiGlassStrong
import xyz.gojihub.vpn.ui.util.rememberPressScale

/** Первый запуск: три коротких экрана до входа — что такое Goji, как начать, зачем системе
 *  разрешение на VPN (одинаково на всех платформах, 2026-10-28). Вид — как у экрана входа. */
object Onboarding {
    private const val PREFS = "onboarding"
    private const val KEY_SEEN = "seen"

    fun isSeen(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean(KEY_SEEN, false)

    fun markSeen(context: Context) =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putBoolean(KEY_SEEN, true).apply()
}

@Composable
fun OnboardingScreen(onDone: () -> Unit) {
    val pages = listOf(
        Loc.f.onboardingTitle1 to Loc.f.onboardingBody1,
        Loc.f.onboardingTitle2 to Loc.f.onboardingBody2,
        Loc.f.onboardingTitle3 to Loc.f.onboardingBody3
    )
    var page by rememberSaveable { mutableIntStateOf(0) }
    val last = page == pages.lastIndex

    GlassBackdrop {
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

            Box(Modifier.fillMaxWidth().weight(1f).heightIn(min = 200.dp)) {
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

            Column(
                Modifier
                    .fillMaxWidth()
                    .godjiGlassStrong(RoundedCornerShape(32.dp))
                    .padding(start = 18.dp, top = 22.dp, end = 18.dp, bottom = 16.dp),
                verticalArrangement = Arrangement.spacedBy(14.dp)
            ) {
                val (title, body) = pages[page]
                Text(title, color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.SemiBold, fontSize = 28.sp, lineHeight = 31.sp)
                Column(verticalArrangement = Arrangement.spacedBy(9.dp)) {
                    body.lines().filter { it.isNotBlank() }.forEach { line ->
                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            Box(Modifier.padding(top = 7.dp).size(6.dp).clip(CircleShape).background(GodjiColors.Teal))
                            Text(line, color = GodjiColors.TextPrimary, fontWeight = FontWeight.Medium, fontSize = 14.sp, lineHeight = 19.sp)
                        }
                    }
                }

                // Точки страниц: «1 из 3» — для экранного диктора.
                Row(
                    Modifier.fillMaxWidth().semantics { contentDescription = "${page + 1} / ${pages.size}" },
                    horizontalArrangement = Arrangement.spacedBy(6.dp, Alignment.CenterHorizontally)
                ) {
                    pages.indices.forEach { i ->
                        Box(Modifier.height(6.dp).width(if (i == page) 18.dp else 6.dp).clip(CircleShape)
                            .background(if (i == page) GodjiColors.TealDeep else GodjiColors.Hair))
                    }
                }

                Row(verticalAlignment = Alignment.CenterVertically) {
                    if (!last) {
                        TextButton(onClick = onDone, modifier = Modifier.heightIn(min = 48.dp)) {
                            Text(Loc.f.onboardingSkip, color = GodjiColors.TextSecondary, fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
                        }
                    }
                    Spacer(Modifier.weight(1f))
                    val (interaction, scale) = rememberPressScale()
                    Button(
                        onClick = { if (last) onDone() else page++ },
                        interactionSource = interaction,
                        colors = ButtonDefaults.buttonColors(containerColor = GodjiColors.Ink),
                        shape = RoundedCornerShape(50),
                        modifier = Modifier.height(52.dp).scale(scale.value)
                    ) {
                        Text(if (last) Loc.f.onboardingStart else Loc.f.onboardingNext, color = GodjiColors.Surface, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                    }
                }
            }
        }
    }
}
