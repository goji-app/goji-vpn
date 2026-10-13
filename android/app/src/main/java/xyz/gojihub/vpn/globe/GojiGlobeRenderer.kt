package xyz.gojihub.vpn.globe

import android.content.Context
import android.opengl.GLES20
import android.opengl.GLSurfaceView
import android.opengl.Matrix
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import java.nio.ByteBuffer
import java.nio.ByteOrder
import javax.microedition.khronos.egl.EGLConfig
import javax.microedition.khronos.opengles.GL10
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.acos
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.min
import kotlin.math.pow
import kotlin.math.round
import kotlin.math.sin
import kotlin.random.Random

data class GlobeNode(val id: String, val lat: Double, val lon: Double, val country: String)

/** Палитра — 1:1 с THEMES из handoff-1.0.77/reference/goji-globe.js. */
data class GlobeTheme(
    val ocean: FloatArray, val oceanOp: Float,
    val land: FloatArray, val landOp: Float,
    val grid: FloatArray, val gridOp: Float,
    val home: FloatArray, val hi: FloatArray, val arc: FloatArray, val atmo: FloatArray, val dot: FloatArray
) {
    companion object {
        val Dark = GlobeTheme(
            ocean = hex(0x0a201d), oceanOp = 0.9f,
            land = hex(0x2f6f66), landOp = 0.75f,
            grid = hex(0x00d4c4), gridOp = 0.07f,
            home = hex(0x8b7cf6), hi = hex(0x00e7d4), arc = hex(0x00e7d4), atmo = hex(0x00d4c4), dot = hex(0x4a625d)
        )
        val Light = GlobeTheme(
            ocean = hex(0xe7e0cf), oceanOp = 1f,
            land = hex(0x0f4d45), landOp = 0.55f,
            grid = hex(0x0f4d45), gridOp = 0.06f,
            home = hex(0xd9714b), hi = hex(0x00897e), arc = hex(0xd9714b), atmo = hex(0x00a79b), dot = hex(0xa9a08a)
        )
        private fun hex(v: Int) = floatArrayOf(
            ((v shr 16) and 0xFF) / 255f, ((v shr 8) and 0xFF) / 255f, (v and 0xFF) / 255f
        )
    }
}

private const val R = GlobeMath.RADIUS
private const val HOME_LAT = 55.75
private const val HOME_LON = 37.62
private const val CAM_DIST = 5.1f
private const val FOV_Y = 38f
private const val STREAM = 9
// Перелёт при смене узла: сегментов дуги, длительность полёта и затухания (в единицах t, ~1с = 1).
private const val FLIGHT_SEGMENTS = 96
private const val FLIGHT_DUR = 1.6f
private const val FLIGHT_FADE = 0.8f
private val FALLBACK_NODE = GlobeNode("auto", 60.17, 24.94, "Finland") // GOJI_NODES.auto
private val WHITE = floatArrayOf(1f, 1f, 1f)
private val SAT_BODY = floatArrayOf(0xE9 / 255f, 0xEE / 255f, 0xF2 / 255f)
private val SAT_RED = floatArrayOf(0xFF / 255f, 0x5A / 255f, 0x4E / 255f)

// MeshBasicMaterial — плоский цвет без освещения.
private const val MESH_VS = """
    uniform mat4 uMVP;
    attribute vec3 aPos;
    void main() { gl_Position = uMVP * vec4(aPos, 1.0); }
"""
private const val FLAT_FS = """
    precision mediump float;
    uniform vec4 uColor;
    void main() { gl_FragColor = uColor; }
"""
// Линия заданной толщины: сегмент разворачивается в экранный прямоугольник. Толщина — либо в
// пикселях (1px-линии three.js при pixelRatio 2 = 0.5dp), либо в мировых единицах (радиус
// TubeGeometry), пересчитанных через глубину точки.
private const val LINE_VS = """
    uniform mat4 uMVP;
    uniform vec2 uViewport;
    uniform float uHalfPx;
    uniform float uHalfWorld;
    uniform float uProjY;
    attribute vec3 aPos;
    attribute vec3 aOther;
    attribute vec2 aSideDir;
    void main() {
        vec4 cp = uMVP * vec4(aPos, 1.0);
        vec4 co = uMVP * vec4(aOther, 1.0);
        vec2 hv = 0.5 * uViewport;
        vec2 sp = cp.xy / cp.w * hv;
        vec2 so = co.xy / co.w * hv;
        vec2 d = (so - sp) * aSideDir.y;
        float len = length(d);
        d = len > 0.0001 ? d / len : vec2(1.0, 0.0);
        vec2 n = vec2(-d.y, d.x);
        float hw = uHalfPx + uHalfWorld * hv.y * uProjY / cp.w;
        vec2 off = (n * aSideDir.x - d * aSideDir.y) * hw;
        gl_Position = vec4(cp.xy + off / hv * cp.w, cp.z, cp.w);
    }
"""

private class Vbo(val id: Int, val count: Int)

private class Satellite(
    val r: Float, val rotX: Float, val rotY: Float, val rotZ: Float,
    var a: Float, val sp: Float, val ph: Float, val s: Float, val blinkRed: Boolean, val orbit: Boolean
)

private class SatFrame(val sat: Satellite, val plane: FloatArray, val body: FloatArray, val panel1: FloatArray, val panel2: FloatArray, val blink: FloatArray)

private class TransparentItem(val z: Float, val draw: () -> Unit)

/**
 * Глобус — порт handoff-1.0.77/reference/goji-globe.js (three.js) 1:1: камера, палитра,
 * геометрия, анимации, порядок отрисовки прозрачных объектов (как сортирует three.js).
 */
class GojiGlobeRenderer(private val context: Context, initialTheme: GlobeTheme = GlobeTheme.Light) : GLSurfaceView.Renderer {

    @Volatile var theme: GlobeTheme = initialTheme
    @Volatile var status: String = "off"
    @Volatile var currentNode: GlobeNode? = null
    /** satellites="on" — только на экране входа. */
    @Volatile var satellites: Boolean = false

    /** Экранные координаты (0..1 от размера вьюпорта) и видимость подписи узла. */
    var onLabelUpdate: ((visible: Boolean, x: Float, y: Float, title: String) -> Unit)? = null

    private val hairPx = 0.25f * context.resources.displayMetrics.density

    private var meshProg = 0
    private var mPos = 0; private var mMVP = 0; private var mColor = 0
    private var lineProg = 0
    private var lPos = 0; private var lOther = 0; private var lSide = 0
    private var lMVP = 0; private var lColor = 0; private var lViewport = 0
    private var lHalfPx = 0; private var lHalfWorld = 0; private var lProjY = 0

    private lateinit var oceanVbo: Vbo
    private lateinit var atmoVbo: Vbo
    private lateinit var unitSphereVbo: Vbo
    private lateinit var cubeVbo: Vbo
    private lateinit var annulusVbo: Vbo
    private lateinit var gridVbo: Vbo
    private lateinit var orbitVbo: Vbo
    private var coastVbo: Vbo? = null
    private var borderVbo: Vbo? = null
    private var highlightVbo: Vbo? = null
    private var arcVbo: Vbo? = null

    @Volatile private var geo: GeoData? = null
    private var geoRequested = false
    private var geoUploaded = false
    private var highlightBuilt = false
    private var highlightKey: String? = null
    private var arcKey: String? = null
    private val arcA = GlobeMath.toVec(HOME_LAT, HOME_LON, R * 1.012f)
    private var arcB = FloatArray(3)
    private var arcControl = FloatArray(3)

    // Перелёт при смене узла: дуга от прежней страны к новой прорисовывается за FLIGHT_DUR,
    // по ней летит светящаяся "голова", затем дуга гаснет за FLIGHT_FADE (единицы — t).
    private var flightVbo: Vbo? = null
    private var flightFrom = FloatArray(3)
    private var flightTo = FloatArray(3)
    private var flightCtrl = FloatArray(3)
    private var flightStart = -1f
    private var lastRealNode: GlobeNode? = null

    private val sats = ArrayList<Satellite>()

    private val projection = FloatArray(16)
    private val view = FloatArray(16)
    private val vp = FloatArray(16)
    private val model = FloatArray(16)
    private val mvp = FloatArray(16)
    private val tmp = FloatArray(16)

    private var width = 1
    private var height = 1
    private var rotX = 0f
    private var rotY = 0f
    private var targetX = -0.2f
    private var targetY = 0f
    private var locked = false
    private var firstFrame = true
    private var lastNodeId: String? = null
    private var t = 0f
    private var lastFrameNanos = 0L
    private var frameScale = 1f
    private var lastLabelVisible: Boolean? = null
    private var lastLabelX = 0f
    private var lastLabelY = 0f
    private var lastLabelTitle = ""

    /** Читается рендер-потоком (GojiGlobeView): true — рисовать 60 кадров/с, false — 30. */
    @Volatile var wantsSmoothFrames: Boolean = true

    private val homePos = GlobeMath.toVec(HOME_LAT, HOME_LON, R * 1.012f)

    override fun onSurfaceCreated(gl: GL10?, config: EGLConfig?) {
        GLES20.glClearColor(0f, 0f, 0f, 0f)
        GLES20.glEnable(GLES20.GL_BLEND)
        // NormalBlending three.js при прозрачном холсте: цвет — src·a + dst·(1−a), альфа — a + dst·(1−a).
        GLES20.glBlendFuncSeparate(GLES20.GL_SRC_ALPHA, GLES20.GL_ONE_MINUS_SRC_ALPHA, GLES20.GL_ONE, GLES20.GL_ONE_MINUS_SRC_ALPHA)
        GLES20.glEnable(GLES20.GL_DEPTH_TEST)
        GLES20.glDepthFunc(GLES20.GL_LEQUAL)
        GLES20.glEnable(GLES20.GL_CULL_FACE)
        GLES20.glCullFace(GLES20.GL_BACK)
        GLES20.glFrontFace(GLES20.GL_CCW)

        meshProg = buildProgram(MESH_VS, FLAT_FS)
        mPos = GLES20.glGetAttribLocation(meshProg, "aPos")
        mMVP = GLES20.glGetUniformLocation(meshProg, "uMVP")
        mColor = GLES20.glGetUniformLocation(meshProg, "uColor")

        lineProg = buildProgram(LINE_VS, FLAT_FS)
        lPos = GLES20.glGetAttribLocation(lineProg, "aPos")
        lOther = GLES20.glGetAttribLocation(lineProg, "aOther")
        lSide = GLES20.glGetAttribLocation(lineProg, "aSideDir")
        lMVP = GLES20.glGetUniformLocation(lineProg, "uMVP")
        lColor = GLES20.glGetUniformLocation(lineProg, "uColor")
        lViewport = GLES20.glGetUniformLocation(lineProg, "uViewport")
        lHalfPx = GLES20.glGetUniformLocation(lineProg, "uHalfPx")
        lHalfWorld = GLES20.glGetUniformLocation(lineProg, "uHalfWorld")
        lProjY = GLES20.glGetUniformLocation(lineProg, "uProjY")

        oceanVbo = upload(sphereTriangles(R, 64, 48), 3)
        atmoVbo = upload(sphereTriangles(R * 1.16f, 40, 28), 3)
        unitSphereVbo = upload(sphereTriangles(1f, 16, 12), 3)
        cubeVbo = upload(cubeTriangles(), 3)
        annulusVbo = upload(annulusStrip(0.038f, 0.044f, 40), 3)
        gridVbo = upload(lineQuads(wireframeSphere(R * 1.001f, 24, 12)), 8)
        orbitVbo = upload(lineQuads(unitCircleXZ(96)), 8)

        // Новый GL-контекст — всё, что лежало в буферах старого, пересоздаём.
        coastVbo = null; borderVbo = null; highlightVbo = null; arcVbo = null; flightVbo = null
        geoUploaded = false; highlightBuilt = false; arcKey = null; flightStart = -1f

        if (sats.isEmpty()) buildSatellites()
        if (!geoRequested) {
            geoRequested = true
            CoroutineScope(Dispatchers.IO).launch { geo = GeoData.load(context, R * 1.004f) }
        }
    }

    override fun onSurfaceChanged(gl: GL10?, w: Int, h: Int) {
        width = w; height = h
        GLES20.glViewport(0, 0, w, h)
        Matrix.perspectiveM(projection, 0, FOV_Y, w.toFloat() / h.toFloat(), 0.1f, 100f)
    }

    override fun onDrawFrame(gl: GL10?) {
        GLES20.glDepthMask(true)
        GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT or GLES20.GL_DEPTH_BUFFER_BIT or GLES20.GL_STENCIL_BUFFER_BIT)

        val g = geo
        if (!geoUploaded && g != null) {
            coastVbo = upload(lineQuads(g.coastLines), 8)
            borderVbo = upload(lineQuads(g.borderLines), 8)
            geoUploaded = true
            highlightBuilt = false
        }

        // Время, а не кадры: эталон (three.js, rAF) рассчитан на 60 кадров/с и прибавляет 0.016
        // за кадр. Рендер-поток теперь снижает частоту, когда картинка почти статична (см.
        // wantsSmoothFrames и GojiGlobeView), поэтому все приращения масштабируются на долю
        // "кадров по 1/60 с", прошедших с прошлой отрисовки — скорость анимаций не меняется.
        val now = System.nanoTime()
        frameScale = if (lastFrameNanos == 0L) 1f else ((now - lastFrameNanos) / 16_666_667f).coerceIn(0.25f, 4f)
        lastFrameNanos = now
        t += 0.016f * frameScale
        val th = theme
        val st = status
        val on = st == "on"
        val connecting = st == "connecting"
        val showMarker = st != "off"
        val nd = currentNode ?: FALLBACK_NODE
        val nodePos = GlobeMath.toVec(nd.lat, nd.lon, R * 1.014f)

        // _apply(): кадрирование на узел, сброс "замирания"
        val f = GlobeMath.normalize(nodePos)
        targetY = -atan2(f[0], f[2])
        targetX = atan2(f[1].toDouble(), hypot(f[0].toDouble(), f[2].toDouble())).toFloat()
        if (firstFrame) { rotY = targetY; rotX = targetX; firstFrame = false }
        if (!on || lastNodeId != nd.id) locked = false
        lastNodeId = nd.id

        // Смена местоположения узла (другой узел или уточнённая реальная страна выхода у того
        // же узла) при видимом маркере — запускаем перелёт и разрешаем глобусу довернуться.
        val real = currentNode
        val prev = lastRealNode
        if (real != null && prev != null && (prev.lat != real.lat || prev.lon != real.lon)) {
            locked = false
            if (showMarker) startFlight(prev, real)
        }
        if (real != null) lastRealNode = real
        val flightP = flightProgress()

        val wantCountry = if (showMarker) nd.country else null
        if (geoUploaded && (!highlightBuilt || highlightKey != wantCountry)) {
            deleteVbo(highlightVbo)
            highlightVbo = if (wantCountry != null && g != null) upload(lineQuads(highlightSegments(g, wantCountry)), 8) else null
            highlightKey = wantCountry
            highlightBuilt = true
        }
        if (arcKey != nd.id) {
            deleteVbo(arcVbo)
            arcB = GlobeMath.toVec(nd.lat, nd.lon, R * 1.012f)
            arcControl = GlobeMath.midControlPoint(arcA, arcB, R * 1.32f)
            arcVbo = upload(lineQuads(bezierSegments(arcA, arcControl, arcB, 96)), 8)
            arcKey = nd.id
        }

        // loop(): вращение
        val wrapY = run { val d = targetY - rotY; d - (round(d / (2 * PI)) * 2 * PI).toFloat() }
        if (on) {
            val dx = targetX - rotX
            if (abs(wrapY) < 0.002f && abs(dx) < 0.002f) locked = true
            if (!locked) { rotY += wrapY * ease(0.06f); rotX += dx * ease(0.06f) }
        } else if (connecting) {
            rotY += wrapY * ease(0.05f)
            rotX += (targetX - rotX) * ease(0.05f)
        } else {
            rotY += 0.0013f * frameScale
            rotX += (-0.16f - rotX) * ease(0.02f)
        }

        Matrix.setLookAtM(view, 0, 0f, 0f, CAM_DIST, 0f, 0f, 0f, 0f, 1f, 0f)
        Matrix.multiplyMM(vp, 0, projection, 0, view, 0)
        Matrix.setIdentityM(model, 0)
        Matrix.rotateM(model, 0, Math.toDegrees(rotX.toDouble()).toFloat(), 1f, 0f, 0f)
        Matrix.rotateM(model, 0, Math.toDegrees(rotY.toDouble()).toFloat(), 0f, 1f, 0f)
        Matrix.multiplyMM(mvp, 0, vp, 0, model, 0)

        val satFrames = if (satellites) satelliteFrames() else emptyList()

        // ── непрозрачные объекты (three.js рисует их первыми) ──
        if (showMarker) {
            sphereAt(homePos, 0.015f, th.home, 1f)
            sphereAt(nodePos, 0.02f, WHITE, 1f)
        }
        for (sf in satFrames) drawMesh(cubeVbo, GLES20.GL_TRIANGLES, world(sf.body), SAT_BODY, 1f)

        // ── прозрачные объекты со своей позицией: сортировка по глубине, как в three.js ──
        val items = ArrayList<TransparentItem>()
        if (showMarker) {
            val z = modelZ(nodePos)
            val haloA = (if (on) 0.42f else 0.25f) + sin(t * 2.4f) * 0.08f
            items += TransparentItem(z) { sphereAt(nodePos, 0.036f, th.hi, haloA) }
            for (i in 0 until 2) {
                val p = ((t * 0.55f) + i * 0.5f) % 1f
                val ringA = (if (on) 0.75f else 0.5f) * (1f - p)
                items += TransparentItem(z) { annulusAt(nodePos, 0.6f + p * 1.9f, th.hi, ringA) }
            }
        }
        if (on || connecting) {
            val head = (t * (if (on) 0.45f else 0.25f)) % 1f
            for (i in 0 until STREAM) {
                val p = head - i * 0.022f
                if (p < 0f || p > 1f) continue
                val pos = FloatArray(3)
                GlobeMath.quadBezier(arcA, arcControl, arcB, p, pos)
                val a = (1f - i.toFloat() / STREAM) * sin(p * PI.toFloat()) * (if (on) 1f else 0.7f)
                val r = if (i == 0) 0.013f else 0.009f - i * 0.0005f
                val c = if (i == 0) WHITE else th.arc
                items += TransparentItem(modelZ(pos)) { sphereAt(pos, r, c, a) }
            }
        }
        // "Голова" перелёта — белое ядро и мягкое свечение цвета выделения.
        if (flightP != null && flightP < 1f) {
            val head = FloatArray(3)
            GlobeMath.quadBezier(flightFrom, flightCtrl, flightTo, easeInOut(flightP), head)
            val hz = modelZ(head)
            items += TransparentItem(hz) { sphereAt(head, 0.034f, th.hi, 0.35f) }
            items += TransparentItem(hz + 0.0001f) { sphereAt(head, 0.014f, WHITE, 1f) }
        }
        for (sf in satFrames) {
            val blinkA = if (sin(t * 5f + sf.sat.ph) > 0.6f) 1f else 0.15f
            val blinkC = if (sf.sat.blinkRed) SAT_RED else WHITE
            items += TransparentItem(sf.panel1[14]) { drawMesh(cubeVbo, GLES20.GL_TRIANGLES, world(sf.panel1), th.hi, 0.9f) }
            items += TransparentItem(sf.panel2[14]) { drawMesh(cubeVbo, GLES20.GL_TRIANGLES, world(sf.panel2), th.hi, 0.9f) }
            items += TransparentItem(sf.blink[14]) { drawMesh(unitSphereVbo, GLES20.GL_TRIANGLES, world(sf.blink), blinkC, blinkA) }
        }
        items.filter { it.z < 0f }.sortedBy { it.z }.forEach { it.draw() }

        // ── объекты в центре глобуса — в порядке создания (как у three.js при равной глубине) ──
        drawMesh(oceanVbo, GLES20.GL_TRIANGLES, mvp, th.ocean, th.oceanOp)
        drawLines(gridVbo, mvp, th.grid, th.gridOp, hairPx, 0f)
        GLES20.glCullFace(GLES20.GL_FRONT) // atmo: side: THREE.BackSide
        drawMesh(atmoVbo, GLES20.GL_TRIANGLES, vp, th.atmo, if (on) 0.09f else 0.05f)
        GLES20.glCullFace(GLES20.GL_BACK)
        for (sf in satFrames) {
            if (!sf.sat.orbit) continue
            System.arraycopy(sf.plane, 0, tmp, 0, 16)
            Matrix.scaleM(tmp, 0, sf.sat.r, sf.sat.r, sf.sat.r)
            drawLines(orbitVbo, world(tmp), th.hi, 0.12f, hairPx, 0f)
        }
        if (on || connecting) {
            drawLines(arcVbo, mvp, th.arc, if (on) 0.35f else 0.15f + sin(t * 5f) * 0.1f, 0f, 0.0032f)
        }
        val fv = flightVbo
        if (fv != null && flightP != null) {
            // Прорисованная часть дуги растёт вместе с "головой"; после прилёта — затухание.
            val shown = (easeInOut(min(flightP, 1f)) * FLIGHT_SEGMENTS).toInt().coerceIn(0, FLIGHT_SEGMENTS)
            val fade = if (flightP <= 1f) 1f else (1f - (flightP - 1f) * FLIGHT_DUR / FLIGHT_FADE).coerceIn(0f, 1f)
            drawLines(Vbo(fv.id, shown * 6), mvp, th.hi, 0.85f * fade, 0f, 0.0042f)
            GLES20.glDepthMask(false)
            drawLines(Vbo(fv.id, shown * 6), mvp, th.hi, 0.22f * fade, 0f, 0.012f)
            GLES20.glDepthMask(true)
        }
        drawLines(coastVbo, mvp, th.land, th.landOp, hairPx, 0f)
        drawLines(borderVbo, mvp, th.land, th.landOp * 0.5f, hairPx, 0f)
        if (showMarker) {
            val coreA = if (on) 1f else 0.5f + sin(t * 4f) * 0.3f
            val glowA = (if (on) 0.3f else 0.15f) + sin(t * 2.2f) * 0.1f
            drawLines(highlightVbo, mvp, th.hi, coreA, 0f, 0.0055f)
            GLES20.glDepthMask(false) // hlGlow: depthWrite: false
            drawLines(highlightVbo, mvp, th.hi, glowA, 0f, 0.016f)
            GLES20.glDepthMask(true)
        }

        items.filter { it.z >= 0f }.sortedBy { it.z }.forEach { it.draw() }

        if (showMarker) {
            val screen = project(nodePos)
            postLabel(screen != null, screen?.get(0) ?: 0f, screen?.get(1) ?: 0f, nd.country)
        } else {
            postLabel(false, 0f, 0f, "")
        }

        // Плавные 60 кадров/с нужны, только пока что-то заметно движется: идёт подключение,
        // глобус доворачивается к узлу, летит перелёт или на экране входа летают спутники.
        // В остальное время (медленное вращение "выключено", застывший кадр "подключено")
        // хватает 30 — вдвое меньше работы GPU/CPU без видимой разницы.
        wantsSmoothFrames = connecting || (on && !locked) || flightP != null || satellites
    }

    // ── перелёт ───────────────────────────────────────────────────

    private fun startFlight(from: GlobeNode, to: GlobeNode) {
        val a = GlobeMath.toVec(from.lat, from.lon, R * 1.012f)
        val b = GlobeMath.toVec(to.lat, to.lon, R * 1.012f)
        val na = GlobeMath.normalize(a); val nb = GlobeMath.normalize(b)
        val dot = (na[0] * nb[0] + na[1] * nb[1] + na[2] * nb[2]).coerceIn(-1f, 1f)
        val angle = acos(dot)
        if (angle < 0.02f) return // ~1° — соседние точки, лететь некуда
        flightFrom = a
        flightTo = b
        // Чем дальше лететь, тем выше дуга.
        flightCtrl = GlobeMath.midControlPoint(a, b, R * (1.18f + 0.32f * angle / PI.toFloat()))
        deleteVbo(flightVbo)
        flightVbo = upload(lineQuads(bezierSegments(flightFrom, flightCtrl, flightTo, FLIGHT_SEGMENTS)), 8)
        flightStart = t
    }

    /** 0..1 — полёт, 1..(1+FADE/DUR) — затухание дуги, null — перелёта нет. */
    private fun flightProgress(): Float? {
        if (flightStart < 0f || flightVbo == null) return null
        val p = (t - flightStart) / FLIGHT_DUR
        if (p > 1f + FLIGHT_FADE / FLIGHT_DUR) {
            deleteVbo(flightVbo)
            flightVbo = null
            flightStart = -1f
            return null
        }
        return p
    }

    /** Коэффициент экспоненциального сглаживания "k за кадр 60 Гц" для текущего шага времени. */
    private fun ease(k: Float): Float = 1f - (1f - k).pow(frameScale)

    /** Подпись страны поверх глобуса — в главный поток только при заметном изменении, а не
     *  60 раз в секунду: каждая отправка — запись в Compose-состояние и перекомпоновка. */
    private fun postLabel(visible: Boolean, x: Float, y: Float, title: String) {
        if (visible == lastLabelVisible && title == lastLabelTitle &&
            abs(x - lastLabelX) < 0.0015f && abs(y - lastLabelY) < 0.0015f) return
        lastLabelVisible = visible; lastLabelX = x; lastLabelY = y; lastLabelTitle = title
        onLabelUpdate?.invoke(visible, x, y, title)
    }

    private fun easeInOut(x: Float): Float = if (x < 0.5f) 2f * x * x else 1f - (-2f * x + 2f).let { it * it } / 2f

    // ── отрисовка ─────────────────────────────────────────────────

    private fun world(m: FloatArray): FloatArray {
        val out = FloatArray(16)
        Matrix.multiplyMM(out, 0, vp, 0, m, 0)
        return out
    }

    /** Z точки после поворота глобуса — для сортировки прозрачных объектов (дальние — раньше). */
    private fun modelZ(p: FloatArray): Float {
        val out = FloatArray(4)
        Matrix.multiplyMV(out, 0, model, 0, floatArrayOf(p[0], p[1], p[2], 1f), 0)
        return out[2]
    }

    private fun sphereAt(pos: FloatArray, radius: Float, color: FloatArray, alpha: Float) {
        val m = FloatArray(16)
        Matrix.setIdentityM(m, 0)
        Matrix.translateM(m, 0, pos[0], pos[1], pos[2])
        Matrix.scaleM(m, 0, radius, radius, radius)
        val out = FloatArray(16)
        Matrix.multiplyMM(out, 0, mvp, 0, m, 0)
        drawMesh(unitSphereVbo, GLES20.GL_TRIANGLES, out, color, alpha)
    }

    /** RingGeometry маркера, повёрнутая плашмя к поверхности (marker.lookAt(at·2)), DoubleSide. */
    private fun annulusAt(pos: FloatArray, scale: Float, color: FloatArray, alpha: Float) {
        val n = GlobeMath.normalize(pos)
        val up = if (abs(n[1]) > 0.999f) floatArrayOf(1f, 0f, 0f) else floatArrayOf(0f, 1f, 0f)
        val x = GlobeMath.normalize(cross(up, n))
        val y = cross(n, x)
        val m = floatArrayOf(
            x[0], x[1], x[2], 0f,
            y[0], y[1], y[2], 0f,
            n[0], n[1], n[2], 0f,
            pos[0], pos[1], pos[2], 1f
        )
        Matrix.scaleM(m, 0, scale, scale, scale)
        val out = FloatArray(16)
        Matrix.multiplyMM(out, 0, mvp, 0, m, 0)
        GLES20.glDisable(GLES20.GL_CULL_FACE)
        drawMesh(annulusVbo, GLES20.GL_TRIANGLE_STRIP, out, color, alpha)
        GLES20.glEnable(GLES20.GL_CULL_FACE)
    }

    private fun drawMesh(v: Vbo, mode: Int, mvpM: FloatArray, color: FloatArray, alpha: Float) {
        if (v.count == 0) return
        GLES20.glUseProgram(meshProg)
        GLES20.glBindBuffer(GLES20.GL_ARRAY_BUFFER, v.id)
        GLES20.glEnableVertexAttribArray(mPos)
        GLES20.glVertexAttribPointer(mPos, 3, GLES20.GL_FLOAT, false, 12, 0)
        GLES20.glUniformMatrix4fv(mMVP, 1, false, mvpM, 0)
        GLES20.glUniform4f(mColor, color[0], color[1], color[2], alpha.coerceIn(0f, 1f))
        GLES20.glDrawArrays(mode, 0, v.count)
        GLES20.glDisableVertexAttribArray(mPos)
        GLES20.glBindBuffer(GLES20.GL_ARRAY_BUFFER, 0)
    }

    /** Линия одним проходом; stencil не даёт соседним сегментам (стык "шапок") смешаться дважды. */
    private fun drawLines(v: Vbo?, mvpM: FloatArray, color: FloatArray, alpha: Float, halfPx: Float, halfWorld: Float) {
        if (v == null || v.count == 0 || alpha <= 0f) return
        GLES20.glUseProgram(lineProg)
        GLES20.glBindBuffer(GLES20.GL_ARRAY_BUFFER, v.id)
        GLES20.glEnableVertexAttribArray(lPos)
        GLES20.glEnableVertexAttribArray(lOther)
        GLES20.glEnableVertexAttribArray(lSide)
        GLES20.glVertexAttribPointer(lPos, 3, GLES20.GL_FLOAT, false, 32, 0)
        GLES20.glVertexAttribPointer(lOther, 3, GLES20.GL_FLOAT, false, 32, 12)
        GLES20.glVertexAttribPointer(lSide, 2, GLES20.GL_FLOAT, false, 32, 24)
        GLES20.glUniformMatrix4fv(lMVP, 1, false, mvpM, 0)
        GLES20.glUniform4f(lColor, color[0], color[1], color[2], alpha.coerceIn(0f, 1f))
        GLES20.glUniform2f(lViewport, width.toFloat(), height.toFloat())
        GLES20.glUniform1f(lHalfPx, halfPx)
        GLES20.glUniform1f(lHalfWorld, halfWorld)
        GLES20.glUniform1f(lProjY, projection[5])

        GLES20.glDisable(GLES20.GL_CULL_FACE)
        GLES20.glClear(GLES20.GL_STENCIL_BUFFER_BIT)
        GLES20.glEnable(GLES20.GL_STENCIL_TEST)
        GLES20.glStencilFunc(GLES20.GL_EQUAL, 0, 0xFF)
        GLES20.glStencilOp(GLES20.GL_KEEP, GLES20.GL_KEEP, GLES20.GL_INCR)
        GLES20.glDrawArrays(GLES20.GL_TRIANGLES, 0, v.count)
        GLES20.glDisable(GLES20.GL_STENCIL_TEST)
        GLES20.glEnable(GLES20.GL_CULL_FACE)

        GLES20.glDisableVertexAttribArray(lPos)
        GLES20.glDisableVertexAttribArray(lOther)
        GLES20.glDisableVertexAttribArray(lSide)
        GLES20.glBindBuffer(GLES20.GL_ARRAY_BUFFER, 0)
    }

    private fun project(v: FloatArray): FloatArray? {
        val rotated = FloatArray(4)
        Matrix.multiplyMV(rotated, 0, model, 0, floatArrayOf(v[0], v[1], v[2], 1f), 0)
        val clip = FloatArray(4)
        Matrix.multiplyMV(clip, 0, vp, 0, rotated, 0)
        if (clip[3] <= 0f) return null
        val n = GlobeMath.normalize(floatArrayOf(rotated[0], rotated[1], rotated[2]))
        if (n[2] < 0.16f) return null
        return floatArrayOf(clip[0] / clip[3] * 0.5f + 0.5f, -clip[1] / clip[3] * 0.5f + 0.5f)
    }

    // ── спутники ──────────────────────────────────────────────────

    private fun buildSatellites() {
        val rnd = Random(1)
        for (i in 0 until 16) {
            val r = R * (1.16f + (i % 4) * 0.07f + rnd.nextFloat() * 0.03f)
            val rx = (rnd.nextFloat() - 0.5f) * 2.2f
            val ry = rnd.nextFloat() * (2f * PI.toFloat())
            val rz = (rnd.nextFloat() - 0.5f) * 1.2f
            val s = 0.6f + rnd.nextFloat() * 0.5f
            sats += Satellite(
                r = r,
                rotX = Math.toDegrees(rx.toDouble()).toFloat(),
                rotY = Math.toDegrees(ry.toDouble()).toFloat(),
                rotZ = Math.toDegrees(rz.toDouble()).toFloat(),
                a = rnd.nextFloat() * (2f * PI.toFloat()),
                sp = (0.0025f + rnd.nextFloat() * 0.004f) * (if (i % 3 == 0) -1f else 1f),
                ph = rnd.nextFloat() * 6f,
                s = s,
                blinkRed = i % 3 != 0,
                orbit = i % 2 == 0
            )
        }
    }

    /** satLayer в сцене, не в globe — спутники не вращаются вместе с планетой. */
    private fun satelliteFrames(): List<SatFrame> = sats.map { o ->
        o.a += o.sp * frameScale
        val plane = FloatArray(16)
        Matrix.setIdentityM(plane, 0)
        Matrix.rotateM(plane, 0, o.rotX, 1f, 0f, 0f) // Euler XYZ: Rx·Ry·Rz
        Matrix.rotateM(plane, 0, o.rotY, 0f, 1f, 0f)
        Matrix.rotateM(plane, 0, o.rotZ, 0f, 0f, 1f)
        val sat = plane.copyOf()
        Matrix.translateM(sat, 0, cos(o.a) * o.r, 0f, sin(o.a) * o.r)
        Matrix.rotateM(sat, 0, Math.toDegrees(-o.a.toDouble()).toFloat(), 0f, 1f, 0f)
        val s = o.s
        val body = sat.copyOf().also { Matrix.scaleM(it, 0, 0.022f * s, 0.022f * s, 0.03f * s) }
        val p1 = sat.copyOf().also { Matrix.translateM(it, 0, 0.04f * s, 0f, 0f); Matrix.scaleM(it, 0, 0.05f * s, 0.003f, 0.022f * s) }
        val p2 = sat.copyOf().also { Matrix.translateM(it, 0, -0.04f * s, 0f, 0f); Matrix.scaleM(it, 0, 0.05f * s, 0.003f, 0.022f * s) }
        val blink = sat.copyOf().also { Matrix.translateM(it, 0, 0f, 0.016f * s, 0f); Matrix.scaleM(it, 0, 0.006f * s, 0.006f * s, 0.006f * s) }
        SatFrame(o, plane, body, p1, p2, blink)
    }

    // ── геометрия ─────────────────────────────────────────────────

    /** SphereGeometry: треугольники с обходом против часовой снаружи (FrontSide). */
    private fun sphereTriangles(r: Float, lonSeg: Int, latSeg: Int): FloatArray {
        val out = FloatArray(lonSeg * latSeg * 18)
        var o = 0
        fun v(i: Int, j: Int) = GlobeMath.toVec(90.0 - i * 180.0 / latSeg, -180.0 + j * 360.0 / lonSeg, r)
        fun put(p: FloatArray) { out[o++] = p[0]; out[o++] = p[1]; out[o++] = p[2] }
        for (i in 0 until latSeg) for (j in 0 until lonSeg) {
            val a = v(i, j); val b = v(i + 1, j); val c = v(i, j + 1); val d = v(i + 1, j + 1)
            put(a); put(b); put(c)
            put(c); put(b); put(d)
        }
        return out
    }

    /** WireframeGeometry(SphereGeometry(r, w, h)) — все рёбра треугольников, как в three.js. */
    private fun wireframeSphere(r: Float, w: Int, h: Int): FloatArray {
        val verts = ArrayList<FloatArray>()
        val grid = Array(h + 1) { IntArray(w + 1) }
        for (iy in 0..h) for (ix in 0..w) {
            val phi = ix.toDouble() / w * 2 * PI
            val theta = iy.toDouble() / h * PI
            verts += floatArrayOf(
                (-r * cos(phi) * sin(theta)).toFloat(),
                (r * cos(theta)).toFloat(),
                (r * sin(phi) * sin(theta)).toFloat()
            )
            grid[iy][ix] = verts.size - 1
        }
        val edges = LinkedHashSet<Long>()
        fun edge(a: Int, b: Int) { edges += (minOf(a, b).toLong() shl 32) or maxOf(a, b).toLong() }
        fun tri(a: Int, b: Int, c: Int) { edge(a, b); edge(b, c); edge(c, a) }
        for (iy in 0 until h) for (ix in 0 until w) {
            val a = grid[iy][ix + 1]; val b = grid[iy][ix]; val c = grid[iy + 1][ix]; val d = grid[iy + 1][ix + 1]
            if (iy != 0) tri(a, b, d)
            if (iy != h - 1) tri(b, c, d)
        }
        val out = FloatArray(edges.size * 6)
        var o = 0
        for (e in edges) {
            val p = verts[(e shr 32).toInt()]; val q = verts[(e and 0xFFFFFFFFL).toInt()]
            out[o++] = p[0]; out[o++] = p[1]; out[o++] = p[2]
            out[o++] = q[0]; out[o++] = q[1]; out[o++] = q[2]
        }
        return out
    }

    /** Орбита спутника: окружность r=1 в плоскости XZ (масштабируется до r матрицей). */
    private fun unitCircleXZ(segments: Int): FloatArray {
        val out = FloatArray(segments * 6)
        for (k in 0 until segments) {
            val a0 = k.toDouble() / segments * 2 * PI
            val a1 = (k + 1).toDouble() / segments * 2 * PI
            out[k * 6] = cos(a0).toFloat(); out[k * 6 + 1] = 0f; out[k * 6 + 2] = sin(a0).toFloat()
            out[k * 6 + 3] = cos(a1).toFloat(); out[k * 6 + 4] = 0f; out[k * 6 + 5] = sin(a1).toFloat()
        }
        return out
    }

    /** RingGeometry(inner, outer) в плоскости XY. */
    private fun annulusStrip(inner: Float, outer: Float, segments: Int): FloatArray {
        val out = FloatArray((segments + 1) * 6)
        for (i in 0..segments) {
            val a = 2.0 * PI * i / segments
            val cx = cos(a).toFloat(); val sy = sin(a).toFloat()
            out[i * 6] = cx * outer; out[i * 6 + 1] = sy * outer; out[i * 6 + 2] = 0f
            out[i * 6 + 3] = cx * inner; out[i * 6 + 4] = sy * inner; out[i * 6 + 5] = 0f
        }
        return out
    }

    /** BoxGeometry 1×1×1, грани против часовой снаружи. */
    private fun cubeTriangles(): FloatArray {
        val h = 0.5f
        val faces = arrayOf(
            floatArrayOf(h, -h, h, h, -h, -h, h, h, -h, h, h, h),
            floatArrayOf(-h, -h, -h, -h, -h, h, -h, h, h, -h, h, -h),
            floatArrayOf(-h, h, h, h, h, h, h, h, -h, -h, h, -h),
            floatArrayOf(-h, -h, -h, h, -h, -h, h, -h, h, -h, -h, h),
            floatArrayOf(-h, -h, h, h, -h, h, h, h, h, -h, h, h),
            floatArrayOf(h, -h, -h, -h, -h, -h, -h, h, -h, h, h, -h)
        )
        val out = FloatArray(6 * 18)
        var o = 0
        for (f in faces) for (idx in intArrayOf(0, 1, 2, 0, 2, 3)) {
            out[o++] = f[idx * 3]; out[o++] = f[idx * 3 + 1]; out[o++] = f[idx * 3 + 2]
        }
        return out
    }

    /** Контур страны — все кольца (MultiPolygon, острова) длиннее 3 точек, на R·1.014. */
    private fun highlightSegments(g: GeoData, country: String): FloatArray {
        val rings = g.countryRingsLonLat[country] ?: return FloatArray(0)
        val out = ArrayList<Float>()
        for (ring in rings) {
            val n = ring.size / 2
            if (n <= 3) continue
            for (i in 0 until n - 1) {
                val a = GlobeMath.toVec(ring[i * 2 + 1], ring[i * 2], R * 1.014f)
                val b = GlobeMath.toVec(ring[(i + 1) * 2 + 1], ring[(i + 1) * 2], R * 1.014f)
                out += a[0]; out += a[1]; out += a[2]; out += b[0]; out += b[1]; out += b[2]
            }
        }
        return out.toFloatArray()
    }

    private fun bezierSegments(a: FloatArray, c: FloatArray, b: FloatArray, segments: Int): FloatArray {
        val out = FloatArray(segments * 6)
        val p = FloatArray(3); val q = FloatArray(3)
        for (i in 0 until segments) {
            GlobeMath.quadBezier(a, c, b, i.toFloat() / segments, p)
            GlobeMath.quadBezier(a, c, b, (i + 1).toFloat() / segments, q)
            out[i * 6] = p[0]; out[i * 6 + 1] = p[1]; out[i * 6 + 2] = p[2]
            out[i * 6 + 3] = q[0]; out[i * 6 + 4] = q[1]; out[i * 6 + 5] = q[2]
        }
        return out
    }

    /** Сегменты [a,b] → по 6 вершин (два треугольника) формата pos3·other3·side/dir2. */
    private fun lineQuads(segments: FloatArray): FloatArray {
        val n = segments.size / 6
        val out = FloatArray(n * 48)
        var o = 0
        fun put(s: Int, fromA: Boolean, side: Float) {
            val p = if (fromA) s else s + 3
            val q = if (fromA) s + 3 else s
            out[o++] = segments[p]; out[o++] = segments[p + 1]; out[o++] = segments[p + 2]
            out[o++] = segments[q]; out[o++] = segments[q + 1]; out[o++] = segments[q + 2]
            out[o++] = side; out[o++] = if (fromA) 1f else -1f
        }
        for (i in 0 until n) {
            val s = i * 6
            put(s, true, 1f); put(s, true, -1f); put(s, false, 1f)
            put(s, true, -1f); put(s, false, -1f); put(s, false, 1f)
        }
        return out
    }

    private fun cross(a: FloatArray, b: FloatArray) = floatArrayOf(
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0]
    )

    private fun upload(data: FloatArray, floatsPerVertex: Int): Vbo {
        if (data.isEmpty()) return Vbo(0, 0)
        val ids = IntArray(1)
        GLES20.glGenBuffers(1, ids, 0)
        val buf = ByteBuffer.allocateDirect(data.size * 4).order(ByteOrder.nativeOrder()).asFloatBuffer().apply {
            put(data); position(0)
        }
        GLES20.glBindBuffer(GLES20.GL_ARRAY_BUFFER, ids[0])
        GLES20.glBufferData(GLES20.GL_ARRAY_BUFFER, data.size * 4, buf, GLES20.GL_STATIC_DRAW)
        GLES20.glBindBuffer(GLES20.GL_ARRAY_BUFFER, 0)
        return Vbo(ids[0], data.size / floatsPerVertex)
    }

    private fun deleteVbo(v: Vbo?) {
        if (v != null && v.id != 0) GLES20.glDeleteBuffers(1, intArrayOf(v.id), 0)
    }

    private fun buildProgram(vsSrc: String, fsSrc: String): Int {
        val prog = GLES20.glCreateProgram()
        GLES20.glAttachShader(prog, compile(GLES20.GL_VERTEX_SHADER, vsSrc))
        GLES20.glAttachShader(prog, compile(GLES20.GL_FRAGMENT_SHADER, fsSrc))
        GLES20.glLinkProgram(prog)
        return prog
    }

    private fun compile(type: Int, src: String): Int {
        val shader = GLES20.glCreateShader(type)
        GLES20.glShaderSource(shader, src)
        GLES20.glCompileShader(shader)
        return shader
    }
}
