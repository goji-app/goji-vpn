package xyz.gojihub.vpn.ui.util

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material.icons.filled.ExpandMore
import androidx.compose.material.icons.filled.LocationOn
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.BaselineShift
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil.compose.AsyncImage
import xyz.gojihub.vpn.ui.theme.GodjiColors

/**
 * Рендерер "Rich Markdown" + Telegram HTML-разметки (см. шпаргалку синтаксиса, которую
 * использует бэкенд для составления рассылок — content новостей из GET /api/broadcasts
 * приходит в этом формате, а не в чистом HTML). Без WebView — обычные Compose-примитивы,
 * поддержаны: жирный/курсив/зачёркнутый/спойлер/выделение/код (markdown-разметка из шпаргалки), теги
 * <b>/<i>/<u>/<s>/<code>/<pre>/<a href>/<tg-spoiler>/<br>, заголовки #..######, списки
 * (- / 1. / - [ ]), цитаты (>), таблицы (|a|b|), разделитель (---), сноски ([^id]/[^id]: текст),
 * медиа по HTTPS-URL (![](url "подпись")), <details><summary>, <tg-collage>, <tg-slideshow>,
 * <tg-map lat lon zoom/>.
 *
 * Не реализовано полностью: формулы LaTeX ($...$/$$...$$) показываются как есть моноширинным
 * текстом без набора формулы — полноценный typesetting потребовал бы отдельную библиотеку
 * (JLaTeXMath/KaTeX-в-WebView), непропорционально тяжело для новостной ленты в VPN-приложении.
 * <tg-map> открывает системное приложение карт через geo:-intent (без API-ключей и SDK карт),
 * а не показывает карту инлайн.
 */

// ── Модель ────────────────────────────────────────────────────────────────

enum class TextKind { PARAGRAPH, H1, H2, H3, H4, H5, H6, QUOTE, UL, OL, CHECK_UNCHECKED, CHECK_CHECKED }

data class ParsedInline(val annotated: AnnotatedString, val spoilerRanges: List<IntRange>)

sealed class RichBlock {
    data class TextBlock(val kind: TextKind, val parsed: ParsedInline, val ordinal: Int? = null) : RichBlock()
    object Divider : RichBlock()
    data class Table(val header: List<String>, val rows: List<List<String>>) : RichBlock()
    data class Image(val url: String, val caption: String?) : RichBlock()
    data class Collage(val urls: List<String>) : RichBlock()
    data class Slideshow(val urls: List<String>) : RichBlock()
    data class MapBlock(val lat: Double, val lon: Double, val zoom: Int?) : RichBlock()
    data class Details(val summary: ParsedInline, val blocks: List<RichBlock>) : RichBlock()
}

data class RichDoc(val blocks: List<RichBlock>, val footnotes: Map<String, String>)

// ── Публичный composable ─────────────────────────────────────────────────

/** [collapsedBlocks] — сколько верхнеуровневых блоков показывать до нажатия "Читать полностью"
 *  (Int.MAX_VALUE — не сворачивать). Разворачивается один раз и остаётся развёрнутым. */
@Composable
fun RichContent(
    raw: String,
    modifier: Modifier = Modifier,
    collapsedBlocks: Int = Int.MAX_VALUE,
    readMoreLabel: String = ""
) {
    val doc = remember(raw) { parseRichContent(raw) }
    var expanded by remember(raw) { mutableStateOf(false) }
    val overflowing = doc.blocks.size > collapsedBlocks
    val visible = if (expanded || !overflowing) doc.blocks else doc.blocks.take(collapsedBlocks)
    Column(modifier, verticalArrangement = Arrangement.spacedBy(6.dp)) {
        visible.forEach { RichBlockView(it) }
        if (overflowing && !expanded) {
            Text(
                readMoreLabel,
                color = GodjiColors.TealDeep,
                fontWeight = FontWeight.Bold,
                fontSize = 11.sp,
                modifier = Modifier.clickable { expanded = true }
            )
        }
        if (doc.footnotes.isNotEmpty() && (expanded || !overflowing)) {
            HorizontalDivider(color = GodjiColors.CardBorder, modifier = Modifier.padding(vertical = 4.dp))
            doc.footnotes.forEach { (id, text) ->
                Text(
                    "[$id] $text",
                    color = GodjiColors.TextSecondary,
                    fontSize = 10.sp,
                    lineHeight = 14.sp
                )
            }
        }
    }
}

// ── Рендер блоков ─────────────────────────────────────────────────────────

@Composable
private fun RichBlockView(block: RichBlock) {
    when (block) {
        is RichBlock.TextBlock -> TextBlockView(block)
        RichBlock.Divider -> HorizontalDivider(color = GodjiColors.CardBorder)
        is RichBlock.Table -> TableView(block)
        is RichBlock.Image -> ImageBlockView(block.url, block.caption)
        is RichBlock.Collage -> CollageView(block.urls)
        is RichBlock.Slideshow -> SlideshowView(block.urls)
        is RichBlock.MapBlock -> MapButtonView(block)
        is RichBlock.Details -> DetailsView(block)
    }
}

@Composable
private fun TextBlockView(block: RichBlock.TextBlock) {
    val baseStyle = TextStyle(
        color = GodjiColors.TextPrimary,
        fontWeight = FontWeight.Medium,
        fontSize = 12.5.sp,
        lineHeight = 17.sp
    )
    val style = when (block.kind) {
        TextKind.H1 -> baseStyle.copy(fontSize = 18.sp, fontWeight = FontWeight.Bold, lineHeight = 23.sp)
        TextKind.H2 -> baseStyle.copy(fontSize = 16.sp, fontWeight = FontWeight.Bold, lineHeight = 21.sp)
        TextKind.H3 -> baseStyle.copy(fontSize = 14.5.sp, fontWeight = FontWeight.Bold, lineHeight = 19.sp)
        TextKind.H4, TextKind.H5, TextKind.H6 -> baseStyle.copy(fontWeight = FontWeight.Bold)
        TextKind.QUOTE -> baseStyle.copy(color = GodjiColors.TextSecondary, fontStyle = FontStyle.Italic)
        else -> baseStyle
    }
    if (block.kind == TextKind.QUOTE) {
        Row(Modifier.fillMaxWidth()) {
            Box(Modifier.width(3.dp).background(GodjiColors.Teal))
            Spacer(Modifier.width(8.dp))
            ParsedInlineText(block.parsed, style, Modifier.weight(1f))
        }
        return
    }
    val prefix = when (block.kind) {
        TextKind.UL -> "•"
        TextKind.OL -> "${block.ordinal ?: 1}."
        TextKind.CHECK_UNCHECKED -> "☐"
        TextKind.CHECK_CHECKED -> "☑"
        else -> null
    }
    if (prefix != null) {
        Row(Modifier.fillMaxWidth()) {
            Text(prefix, style = style, modifier = Modifier.padding(end = 6.dp))
            ParsedInlineText(block.parsed, style, Modifier.weight(1f))
        }
    } else {
        ParsedInlineText(block.parsed, style, Modifier.fillMaxWidth())
    }
}

/** Текст с поддержкой тап-по-спойлеру (раскрывает сразу все спойлеры внутри этого блока —
 *  упрощение вместо независимого раскрытия каждого спойлера по отдельности) и тап-по-ссылке. */
@Composable
private fun ParsedInlineText(parsed: ParsedInline, style: TextStyle, modifier: Modifier = Modifier) {
    var revealed by remember(parsed) { mutableStateOf(false) }
    var layoutResult by remember(parsed) { mutableStateOf<TextLayoutResult?>(null) }
    val context = LocalContext.current
    val displayed = remember(parsed, revealed) {
        if (revealed || parsed.spoilerRanges.isEmpty()) parsed.annotated
        else buildAnnotatedString {
            append(parsed.annotated)
            parsed.spoilerRanges.forEach { r ->
                if (r.first in 0..parsed.annotated.length && r.last < parsed.annotated.length) {
                    addStyle(SpanStyle(color = Color.Transparent, background = GodjiColors.Ink), r.first, r.last + 1)
                }
            }
        }
    }
    Text(
        text = displayed,
        style = style,
        modifier = modifier.pointerInput(parsed, revealed) {
            detectTapGestures { offset ->
                val layout = layoutResult ?: return@detectTapGestures
                val pos = layout.getOffsetForPosition(offset)
                val url = parsed.annotated.getStringAnnotations("URL", pos, pos).firstOrNull()?.item
                when {
                    url != null -> runCatching { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url))) }
                    !revealed && parsed.spoilerRanges.any { pos in it } -> revealed = true
                }
            }
        },
        onTextLayout = { layoutResult = it }
    )
}

@Composable
private fun TableView(table: RichBlock.Table) {
    Column(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState())) {
        if (table.header.isNotEmpty()) {
            Row { table.header.forEach { TableCell(it, bold = true) } }
            HorizontalDivider(color = GodjiColors.CardBorder)
        }
        table.rows.forEach { row ->
            Row { row.forEach { TableCell(it) } }
            HorizontalDivider(color = GodjiColors.CardBorder)
        }
    }
}

@Composable
private fun TableCell(text: String, bold: Boolean = false) {
    Text(
        text,
        modifier = Modifier.widthIn(min = 84.dp).padding(6.dp),
        color = GodjiColors.TextPrimary,
        fontWeight = if (bold) FontWeight.Bold else FontWeight.Medium,
        fontSize = 11.sp
    )
}

@Composable
private fun ImageBlockView(url: String, caption: String?) {
    Column(Modifier.fillMaxWidth()) {
        AsyncImage(
            model = url,
            contentDescription = caption,
            contentScale = ContentScale.FillWidth,
            modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp))
        )
        if (!caption.isNullOrBlank()) {
            Spacer(Modifier.height(4.dp))
            Text(caption, color = GodjiColors.TextSecondary, fontSize = 10.sp)
        }
    }
}

@Composable
private fun CollageView(urls: List<String>) {
    if (urls.isEmpty()) return
    LazyVerticalGrid(
        columns = GridCells.Fixed(2),
        modifier = Modifier.fillMaxWidth().heightIn(max = 400.dp),
        horizontalArrangement = Arrangement.spacedBy(4.dp),
        verticalArrangement = Arrangement.spacedBy(4.dp),
        userScrollEnabled = false
    ) {
        items(urls.size) { i ->
            AsyncImage(
                model = urls[i],
                contentDescription = null,
                contentScale = ContentScale.Crop,
                modifier = Modifier.aspectRatio(1f).clip(RoundedCornerShape(10.dp))
            )
        }
    }
}

@Composable
private fun SlideshowView(urls: List<String>) {
    if (urls.isEmpty()) return
    val pagerState = rememberPagerState(pageCount = { urls.size })
    Column(Modifier.fillMaxWidth()) {
        HorizontalPager(state = pagerState, modifier = Modifier.fillMaxWidth().heightIn(max = 260.dp)) { page ->
            AsyncImage(
                model = urls[page],
                contentDescription = null,
                contentScale = ContentScale.FillWidth,
                modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp))
            )
        }
        if (urls.size > 1) {
            Spacer(Modifier.height(6.dp))
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.Center) {
                repeat(urls.size) { i ->
                    Box(
                        Modifier
                            .padding(horizontal = 3.dp)
                            .size(6.dp)
                            .clip(RoundedCornerShape(50))
                            .background(if (i == pagerState.currentPage) GodjiColors.TealDeep else GodjiColors.CardBorder)
                    )
                }
            }
        }
    }
}

@Composable
private fun MapButtonView(block: RichBlock.MapBlock) {
    val context = LocalContext.current
    Row(
        Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(GodjiColors.TealTint)
            .clickable {
                val zoomPart = block.zoom?.let { "?z=$it" }.orEmpty()
                val uri = Uri.parse("geo:${block.lat},${block.lon}$zoomPart")
                runCatching { context.startActivity(Intent(Intent.ACTION_VIEW, uri)) }
            }
            .padding(12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        Icon(Icons.Filled.LocationOn, contentDescription = null, tint = GodjiColors.TealDeep, modifier = Modifier.size(17.dp))
        Text("${block.lat}, ${block.lon}", color = GodjiColors.TealDeep, fontWeight = FontWeight.Bold, fontSize = 11.5.sp)
    }
}

@Composable
private fun DetailsView(block: RichBlock.Details) {
    var open by remember { mutableStateOf(false) }
    Column(Modifier.fillMaxWidth()) {
        Row(
            Modifier.fillMaxWidth().clickable { open = !open },
            verticalAlignment = Alignment.CenterVertically
        ) {
            Icon(
                if (open) Icons.Filled.ExpandMore else Icons.AutoMirrored.Filled.KeyboardArrowRight,
                contentDescription = null, tint = GodjiColors.TealDeep,
                modifier = Modifier.size(16.dp).padding(end = 4.dp)
            )
            ParsedInlineText(
                block.summary,
                TextStyle(color = GodjiColors.TextPrimary, fontWeight = FontWeight.Bold, fontSize = 12.5.sp),
                Modifier.weight(1f)
            )
        }
        if (open) {
            Spacer(Modifier.height(6.dp))
            Column(Modifier.padding(start = 14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                block.blocks.forEach { RichBlockView(it) }
            }
        }
    }
}

// ── Парсинг ───────────────────────────────────────────────────────────────

private val imageRegex = Regex("!\\[([^\\]]*)]\\(([^\\s)]+)(?:\\s+\"([^\"]*)\")?\\)")
private val linkRegex = Regex("\\[([^\\]]+)]\\(([^)\\s]+)\\)")
private val footnoteDefRegex = Regex("^\\[\\^([^\\]]+)]:\\s*(.+)$", RegexOption.MULTILINE)
private val collageRegex = Regex("<tg-collage>([\\s\\S]*?)</tg-collage>", RegexOption.IGNORE_CASE)
private val slideshowRegex = Regex("<tg-slideshow>([\\s\\S]*?)</tg-slideshow>", RegexOption.IGNORE_CASE)
private val detailsRegex = Regex("<details>\\s*<summary>([\\s\\S]*?)</summary>([\\s\\S]*?)</details>", RegexOption.IGNORE_CASE)
private val mapRegex = Regex("<tg-map\\s+(-?[0-9.]+)\\s+(-?[0-9.]+)(?:\\s+(\\d+))?\\s*/>", RegexOption.IGNORE_CASE)
private val htmlOpenTagRegex = Regex("^<([a-zA-Z][a-zA-Z0-9-]*)((?:\\s+[a-zA-Z-]+=\"[^\"]*\")*)\\s*/?>")

fun parseRichContent(raw: String): RichDoc {
    val (withoutFootnotes, footnotes) = extractFootnotes(raw.replace("\r\n", "\n"))
    val segments = splitSpecialBlocks(withoutFootnotes)
    val blocks = segments.flatMap { seg ->
        when (seg) {
            is RawSegment.Plain -> parsePlainBlocks(seg.text)
            is RawSegment.Collage -> listOf(RichBlock.Collage(seg.urls))
            is RawSegment.Slideshow -> listOf(RichBlock.Slideshow(seg.urls))
            is RawSegment.MapSeg -> listOf(RichBlock.MapBlock(seg.lat, seg.lon, seg.zoom))
            is RawSegment.Details -> listOf(RichBlock.Details(parseInlineText(seg.summary), parsePlainBlocks(seg.body)))
        }
    }
    return RichDoc(blocks, footnotes)
}

private fun extractFootnotes(raw: String): Pair<String, Map<String, String>> {
    val map = linkedMapOf<String, String>()
    val cleaned = footnoteDefRegex.replace(raw) { m -> map[m.groupValues[1]] = m.groupValues[2].trim(); "" }
    return cleaned to map
}

private sealed class RawSegment {
    data class Plain(val text: String) : RawSegment()
    data class Collage(val urls: List<String>) : RawSegment()
    data class Slideshow(val urls: List<String>) : RawSegment()
    data class Details(val summary: String, val body: String) : RawSegment()
    data class MapSeg(val lat: Double, val lon: Double, val zoom: Int?) : RawSegment()
}

private fun splitSpecialBlocks(raw: String): List<RawSegment> {
    data class Match(val range: IntRange, val seg: RawSegment)
    val matches = mutableListOf<Match>()
    collageRegex.findAll(raw).forEach { matches += Match(it.range, RawSegment.Collage(extractImageUrls(it.groupValues[1]))) }
    slideshowRegex.findAll(raw).forEach { matches += Match(it.range, RawSegment.Slideshow(extractImageUrls(it.groupValues[1]))) }
    detailsRegex.findAll(raw).forEach { matches += Match(it.range, RawSegment.Details(it.groupValues[1].trim(), it.groupValues[2].trim())) }
    mapRegex.findAll(raw).forEach { m ->
        val lat = m.groupValues[1].toDoubleOrNull()
        val lon = m.groupValues[2].toDoubleOrNull()
        if (lat != null && lon != null) matches += Match(m.range, RawSegment.MapSeg(lat, lon, m.groupValues[3].toIntOrNull()))
    }
    matches.sortBy { it.range.first }
    // Не поддерживаем вложенность спецблоков — отбрасываем совпадения, начинающиеся внутри
    // уже занятого диапазона (например <tg-map> случайно найденный внутри <details>).
    val filtered = mutableListOf<Match>()
    var lastEnd = -1
    for (m in matches) {
        if (m.range.first > lastEnd) {
            filtered += m
            lastEnd = m.range.last
        }
    }
    val segments = mutableListOf<RawSegment>()
    var cursor = 0
    for (m in filtered) {
        if (m.range.first > cursor) segments += RawSegment.Plain(raw.substring(cursor, m.range.first))
        segments += m.seg
        cursor = m.range.last + 1
    }
    if (cursor < raw.length) segments += RawSegment.Plain(raw.substring(cursor))
    return segments
}

private fun extractImageUrls(inner: String): List<String> {
    val urls = imageRegex.findAll(inner).map { it.groupValues[2] }.toMutableList()
    if (urls.isEmpty()) {
        inner.lines().map { it.trim() }.filterTo(urls) { it.startsWith("http") }
    }
    return urls
}

private val headingRegex = Regex("^#{1,6}\\s+.*")
private val checkRegex = Regex("^-\\s+\\[[ xX]]\\s+.*")
private val orderedRegex = Regex("^\\d+\\.\\s+.*")
private val tableSeparatorCell = Regex("^:?-{2,}:?$")

private fun parsePlainBlocks(text: String): List<RichBlock> {
    val lines = text.split("\n")
    val blocks = mutableListOf<RichBlock>()
    val paragraph = StringBuilder()

    fun flushParagraph() {
        val content = paragraph.toString().trim()
        if (content.isNotEmpty()) blocks += RichBlock.TextBlock(TextKind.PARAGRAPH, parseInlineText(content))
        paragraph.clear()
    }

    var i = 0
    while (i < lines.size) {
        val raw = lines[i]
        val trimmed = raw.trim()
        when {
            trimmed.isEmpty() -> { flushParagraph(); i++ }
            trimmed == "---" || trimmed == "***" || trimmed == "___" -> { flushParagraph(); blocks += RichBlock.Divider; i++ }
            headingRegex.matches(trimmed) -> {
                flushParagraph()
                val level = trimmed.takeWhile { it == '#' }.length.coerceIn(1, 6)
                val content = trimmed.drop(level).trim()
                val kind = listOf(TextKind.H1, TextKind.H2, TextKind.H3, TextKind.H4, TextKind.H5, TextKind.H6)[level - 1]
                blocks += RichBlock.TextBlock(kind, parseInlineText(content))
                i++
            }
            trimmed.startsWith(">") -> {
                flushParagraph()
                blocks += RichBlock.TextBlock(TextKind.QUOTE, parseInlineText(trimmed.removePrefix(">").trim()))
                i++
            }
            checkRegex.matches(trimmed) -> {
                flushParagraph()
                val checked = Regex("\\[[xX]]").containsMatchIn(trimmed)
                val content = trimmed.replaceFirst(Regex("^-\\s+\\[[ xX]]\\s+"), "")
                blocks += RichBlock.TextBlock(if (checked) TextKind.CHECK_CHECKED else TextKind.CHECK_UNCHECKED, parseInlineText(content))
                i++
            }
            trimmed.startsWith("![") -> {
                flushParagraph()
                val m = imageRegex.find(trimmed)
                if (m != null) blocks += RichBlock.Image(m.groupValues[2], m.groupValues[3].takeIf { it.isNotBlank() })
                i++
            }
            trimmed.startsWith("- ") -> {
                flushParagraph()
                blocks += RichBlock.TextBlock(TextKind.UL, parseInlineText(trimmed.removePrefix("- ").trim()))
                i++
            }
            orderedRegex.matches(trimmed) -> {
                flushParagraph()
                val num = trimmed.substringBefore(".").toIntOrNull()
                val content = trimmed.replaceFirst(Regex("^\\d+\\.\\s+"), "")
                blocks += RichBlock.TextBlock(TextKind.OL, parseInlineText(content), ordinal = num)
                i++
            }
            trimmed.startsWith("|") && trimmed.endsWith("|") && trimmed.length > 1 -> {
                flushParagraph()
                val tableLines = mutableListOf(trimmed)
                var j = i + 1
                while (j < lines.size && lines[j].trim().let { it.startsWith("|") && it.endsWith("|") && it.length > 1 }) {
                    tableLines += lines[j].trim()
                    j++
                }
                val rows = tableLines.map { l -> l.trim('|').split("|").map { it.trim() } }
                val header = rows.getOrNull(0).orEmpty()
                val dataRows = rows.drop(1).filterNot { row -> row.all { tableSeparatorCell.matches(it) } }
                blocks += RichBlock.Table(header, dataRows)
                i = j
            }
            else -> { paragraph.appendLine(raw); i++ }
        }
    }
    flushParagraph()
    return blocks
}

private fun parseInlineText(text: String): ParsedInline {
    val builder = AnnotatedString.Builder()
    val spoilers = mutableListOf<IntRange>()
    parseInlineInto(text, builder, spoilers)
    return ParsedInline(builder.toAnnotatedString(), spoilers)
}

private val spoilerBg = Color(0x332FB39A)
private val highlightBg = Color(0x552FB39A)
private val codeBg = Color(0x22000000)

private fun parseInlineInto(text: String, b: AnnotatedString.Builder, spoilers: MutableList<IntRange>) {
    var i = 0
    while (i < text.length) {
        val c = text[i]
        val htmlEnd = if (c == '<') tryHtmlTag(text, i, b, spoilers) else null
        when {
            htmlEnd != null -> i = htmlEnd
            text.startsWith("**", i) -> {
                val end = text.indexOf("**", i + 2)
                if (end < 0) { b.append(c); i++ } else {
                    b.pushStyle(SpanStyle(fontWeight = FontWeight.Bold))
                    parseInlineInto(text.substring(i + 2, end), b, spoilers)
                    b.pop(); i = end + 2
                }
            }
            text.startsWith("~~", i) -> {
                val end = text.indexOf("~~", i + 2)
                if (end < 0) { b.append(c); i++ } else {
                    b.pushStyle(SpanStyle(textDecoration = TextDecoration.LineThrough))
                    parseInlineInto(text.substring(i + 2, end), b, spoilers)
                    b.pop(); i = end + 2
                }
            }
            text.startsWith("||", i) -> {
                val end = text.indexOf("||", i + 2)
                if (end < 0) { b.append(c); i++ } else {
                    val start = b.length
                    parseInlineInto(text.substring(i + 2, end), b, spoilers)
                    spoilers += start..(b.length - 1)
                    i = end + 2
                }
            }
            text.startsWith("==", i) -> {
                val end = text.indexOf("==", i + 2)
                if (end < 0) { b.append(c); i++ } else {
                    b.pushStyle(SpanStyle(background = highlightBg))
                    parseInlineInto(text.substring(i + 2, end), b, spoilers)
                    b.pop(); i = end + 2
                }
            }
            c == '`' -> {
                val end = text.indexOf('`', i + 1)
                if (end < 0) { b.append(c); i++ } else {
                    b.pushStyle(SpanStyle(fontFamily = FontFamily.Monospace, background = codeBg))
                    b.append(text.substring(i + 1, end))
                    b.pop(); i = end + 1
                }
            }
            text.startsWith("[^", i) -> {
                val end = text.indexOf(']', i + 2)
                if (end < 0) { b.append(c); i++ } else {
                    val id = text.substring(i + 2, end)
                    b.pushStyle(SpanStyle(fontSize = 9.sp, baselineShift = BaselineShift.Superscript, color = GodjiColors.TealDeep))
                    b.append("[$id]")
                    b.pop(); i = end + 1
                }
            }
            c == '[' && linkRegex.matchAt(text, i) != null -> {
                val m = linkRegex.matchAt(text, i)!!
                val start = b.length
                b.pushStyle(SpanStyle(color = GodjiColors.TealDeep, textDecoration = TextDecoration.Underline))
                b.append(m.groupValues[1])
                b.pop()
                b.addStringAnnotation("URL", m.groupValues[2], start, b.length)
                i = m.range.last + 1
            }
            c == '*' -> {
                val end = text.indexOf('*', i + 1)
                if (end < 0) { b.append(c); i++ } else {
                    b.pushStyle(SpanStyle(fontStyle = FontStyle.Italic))
                    parseInlineInto(text.substring(i + 1, end), b, spoilers)
                    b.pop(); i = end + 1
                }
            }
            else -> { b.append(c); i++ }
        }
    }
}

/** Возвращает индекс сразу после обработанного HTML-тега (открывающего+содержимого+
 *  закрывающего), или null, если по этой позиции распознаваемого тега нет. */
private fun tryHtmlTag(text: String, i: Int, b: AnnotatedString.Builder, spoilers: MutableList<IntRange>): Int? {
    val openMatch = htmlOpenTagRegex.find(text.substring(i)) ?: return null
    val tag = openMatch.groupValues[1].lowercase()
    val attrs = openMatch.groupValues[2]
    val afterOpen = i + openMatch.value.length
    if (tag == "br") { b.append("\n"); return afterOpen }
    if (openMatch.value.endsWith("/>")) return afterOpen
    val closeTag = "</$tag>"
    val closeIdx = text.indexOf(closeTag, afterOpen, ignoreCase = true)
    if (closeIdx < 0) return afterOpen
    val inner = text.substring(afterOpen, closeIdx)
    val next = closeIdx + closeTag.length
    when (tag) {
        "b", "strong" -> { b.pushStyle(SpanStyle(fontWeight = FontWeight.Bold)); parseInlineInto(inner, b, spoilers); b.pop() }
        "i", "em" -> { b.pushStyle(SpanStyle(fontStyle = FontStyle.Italic)); parseInlineInto(inner, b, spoilers); b.pop() }
        "u", "ins" -> { b.pushStyle(SpanStyle(textDecoration = TextDecoration.Underline)); parseInlineInto(inner, b, spoilers); b.pop() }
        "s", "strike", "del" -> { b.pushStyle(SpanStyle(textDecoration = TextDecoration.LineThrough)); parseInlineInto(inner, b, spoilers); b.pop() }
        "code" -> { b.pushStyle(SpanStyle(fontFamily = FontFamily.Monospace, background = codeBg)); b.append(inner); b.pop() }
        "pre" -> { b.pushStyle(SpanStyle(fontFamily = FontFamily.Monospace)); b.append(inner); b.pop() }
        "tg-spoiler", "spoiler" -> {
            val start = b.length
            parseInlineInto(inner, b, spoilers)
            spoilers += start..(b.length - 1)
        }
        "a" -> {
            val href = Regex("href=\"([^\"]*)\"", RegexOption.IGNORE_CASE).find(attrs)?.groupValues?.get(1)
            val start = b.length
            b.pushStyle(SpanStyle(color = GodjiColors.TealDeep, textDecoration = TextDecoration.Underline))
            parseInlineInto(inner, b, spoilers)
            b.pop()
            if (href != null) b.addStringAnnotation("URL", href, start, b.length)
        }
        else -> parseInlineInto(inner, b, spoilers)
    }
    return next
}
