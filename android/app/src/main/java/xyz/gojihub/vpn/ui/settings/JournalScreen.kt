package xyz.gojihub.vpn.ui.settings

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.delay
import xyz.gojihub.vpn.i18n.Loc
import xyz.gojihub.vpn.journal.NetworkJournal
import xyz.gojihub.vpn.ui.theme.GodjiColors
import xyz.gojihub.vpn.ui.theme.SpaceGroteskFamily
import xyz.gojihub.vpn.ui.theme.godjiCard
import xyz.gojihub.vpn.ui.util.BackButton
import xyz.gojihub.vpn.vpn.GodjiVpnService
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * «Журнал сети» — как страница аптайма: доля времени под защитой по часам за сутки
 * (тёплый столбик — в этот час были сбои: глушение, ошибка, обрыв), счётчики и лента
 * событий. Данные — journal/NetworkJournal, только локальные.
 */
@Composable
fun JournalScreen(onBack: () -> Unit) {
    val events by NetworkJournal.events.collectAsState()
    val running by GodjiVpnService.isRunning.collectAsState()
    var daysAgo by remember { mutableIntStateOf(0) }
    // Текущий час «растёт» — раз в минуту пересчитываем сводку.
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(Unit) {
        while (true) { delay(60_000); now = System.currentTimeMillis() }
    }
    val day = remember(events, daysAgo, running, now) { NetworkJournal.day(events, daysAgo, running, now) }

    Column(
        Modifier
            .fillMaxSize()
            .background(GodjiColors.Background)
            .verticalScroll(rememberScrollState())
            .padding(start = 16.dp, top = 16.dp, end = 16.dp, bottom = 12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            BackButton(onClick = onBack)
            Text(Loc.f.journalTitle, color = GodjiColors.TextPrimary, fontFamily = SpaceGroteskFamily, fontWeight = FontWeight.SemiBold, fontSize = 24.sp)
        }

        DaySwitch(daysAgo) { daysAgo = it }

        UptimeCard(day)

        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            CounterTile(day.connects, Loc.f.journalConnects, GodjiColors.TextPrimary, Modifier.weight(1f))
            CounterTile(day.switches, Loc.f.journalSwitches, GodjiColors.TextPrimary, Modifier.weight(1f))
            CounterTile(day.issues, Loc.f.journalIssues, if (day.issues > 0) GodjiColors.Terracotta else GodjiColors.Teal, Modifier.weight(1f))
        }

        SectionLabel(Loc.f.journalEventsSection)
        if (day.events.isEmpty()) {
            Text(
                Loc.f.journalEmpty, color = GodjiColors.TextSecondary, fontSize = 12.5.sp, lineHeight = 17.sp,
                modifier = Modifier.fillMaxWidth().godjiCard(RoundedCornerShape(24.dp)).padding(horizontal = 16.dp, vertical = 14.dp)
            )
        } else {
            RefCard {
                day.events.forEachIndexed { i, e ->
                    if (i > 0) Hair()
                    EventRow(e)
                }
            }
        }
        Text(
            Loc.f.journalFooter, color = GodjiColors.TextMuted, fontSize = 11.sp,
            modifier = Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 4.dp)
        )
    }
}

@Composable
private fun DaySwitch(selected: Int, onSelect: (Int) -> Unit) {
    Row(
        Modifier.fillMaxWidth().clip(RoundedCornerShape(50)).background(GodjiColors.Chip).padding(3.dp),
        horizontalArrangement = Arrangement.spacedBy(3.dp)
    ) {
        listOf(Loc.f.journalToday, Loc.f.journalYesterday).forEachIndexed { i, label ->
            val on = i == selected
            Box(
                Modifier
                    .weight(1f)
                    .height(34.dp)
                    .clip(RoundedCornerShape(50))
                    .background(if (on) GodjiColors.Thumb else Color.Transparent)
                    .clickable(remember { MutableInteractionSource() }, null) { onSelect(i) },
                contentAlignment = Alignment.Center
            ) {
                Text(label, color = if (on) GodjiColors.TextPrimary else GodjiColors.TextSecondary, fontWeight = FontWeight.Bold, fontSize = 12.5.sp)
            }
        }
    }
}

private fun duration(ms: Long): String {
    val minutes = ms / 60_000
    return Loc.f.journalDuration(minutes / 60, minutes % 60)
}

@Composable
private fun UptimeCard(day: NetworkJournal.Day) {
    val pct = if (day.elapsedMs > 0) (day.protectedMs * 1000 / day.elapsedMs) / 10.0 else 0.0
    Column(
        Modifier.fillMaxWidth().godjiCard(RoundedCornerShape(24.dp)).padding(horizontal = 16.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.Bottom) {
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(Loc.f.journalProtected, color = GodjiColors.TextSecondary, fontWeight = FontWeight.SemiBold, fontSize = 12.sp)
                Text(
                    Loc.f.journalOf(duration(day.protectedMs), duration(day.elapsedMs)),
                    color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 11.5.sp
                )
            }
            Text(
                (if (pct % 1.0 == 0.0) "%.0f%%" else "%.1f%%").format(pct),
                color = GodjiColors.Teal, fontWeight = FontWeight.ExtraBold, fontSize = 24.sp, letterSpacing = (-0.4).sp
            )
        }
        Row(
            Modifier.fillMaxWidth().height(56.dp),
            horizontalArrangement = Arrangement.spacedBy(3.dp),
            verticalAlignment = Alignment.Bottom
        ) {
            day.hours.forEach { h ->
                val up = h.uptime
                val color = when {
                    up == null -> GodjiColors.TrackBg.copy(alpha = 0.45f)
                    h.issue -> GodjiColors.Terracotta
                    up <= 0f -> GodjiColors.TrackBg
                    else -> GodjiColors.Teal
                }
                val height = if (up == null || up <= 0f) 4.dp else (6 + 50 * up).dp
                Box(Modifier.weight(1f).height(height).clip(RoundedCornerShape(3.dp)).background(color))
            }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            listOf("00:00", "06:00", "12:00", "18:00", "24:00").forEach {
                Text(it, color = GodjiColors.TextMuted, fontSize = 10.5.sp)
            }
        }
    }
}

@Composable
private fun CounterTile(value: Int, label: String, color: Color, modifier: Modifier = Modifier) {
    Column(
        modifier.godjiCard(RoundedCornerShape(20.dp)).padding(horizontal = 12.dp, vertical = 10.dp),
        verticalArrangement = Arrangement.spacedBy(1.dp)
    ) {
        Text(value.toString(), color = color, fontWeight = FontWeight.ExtraBold, fontSize = 20.sp)
        Text(label, color = GodjiColors.TextSecondary, fontWeight = FontWeight.Medium, fontSize = 11.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

private val timeFormat = SimpleDateFormat("HH:mm", Locale.getDefault())

@Composable
private fun EventRow(e: NetworkJournal.Event) {
    val (title, text) = Loc.f.journalEvent(e.kind.name, e.arg)
    val dot = when {
        NetworkJournal.isIssue(e.kind) -> GodjiColors.Terracotta
        NetworkJournal.isSwitch(e.kind) -> GodjiColors.Warning
        e.kind == NetworkJournal.Kind.CONNECTED || e.kind == NetworkJournal.Kind.LEAK_OK -> GodjiColors.Teal
        else -> GodjiColors.Outline
    }
    Row(
        Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 10.dp),
        horizontalArrangement = Arrangement.spacedBy(10.dp),
        verticalAlignment = Alignment.Top
    ) {
        Text(timeFormat.format(Date(e.at)), color = GodjiColors.TextSecondary, fontWeight = FontWeight.Bold, fontSize = 12.sp, modifier = Modifier.width(40.dp))
        Box(Modifier.padding(top = 5.dp).size(8.dp).clip(CircleShape).background(dot))
        Column(verticalArrangement = Arrangement.spacedBy(1.dp)) {
            Text(title, color = GodjiColors.TextPrimary, fontWeight = FontWeight.SemiBold, fontSize = 13.5.sp)
            if (text.isNotBlank()) {
                Text(text, color = GodjiColors.TextSecondary, fontSize = 11.5.sp, lineHeight = 15.sp)
            }
        }
    }
}
