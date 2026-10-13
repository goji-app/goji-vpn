package xyz.gojihub.vpn.globe

import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.sin

/** Порт toVec(lat, lon, r) из goji-globe.js — та же ориентация оси, тот же радиус по умолчанию. */
object GlobeMath {
    const val RADIUS = 1.28f

    fun toVec(lat: Double, lon: Double, r: Float = RADIUS): FloatArray {
        val p = (90.0 - lat) * PI / 180.0
        val t = (lon + 180.0) * PI / 180.0
        val x = -r * sin(p) * cos(t)
        val y = r * cos(p)
        val z = r * sin(p) * sin(t)
        return floatArrayOf(x.toFloat(), y.toFloat(), z.toFloat())
    }

    /** Точка на квадратичной кривой Безье a→control→b, t в [0,1]. */
    fun quadBezier(a: FloatArray, control: FloatArray, b: FloatArray, t: Float, out: FloatArray) {
        val u = 1f - t
        for (i in 0..2) {
            out[i] = u * u * a[i] + 2f * u * t * control[i] + t * t * b[i]
        }
    }

    fun midControlPoint(a: FloatArray, b: FloatArray, scale: Float): FloatArray {
        val mx = (a[0] + b[0]) * 0.5f
        val my = (a[1] + b[1]) * 0.5f
        val mz = (a[2] + b[2]) * 0.5f
        val len = hypot(hypot(mx.toDouble(), my.toDouble()), mz.toDouble()).toFloat().coerceAtLeast(1e-5f)
        val f = scale / len
        return floatArrayOf(mx * f, my * f, mz * f)
    }

    fun normalize(v: FloatArray): FloatArray {
        val len = hypot(hypot(v[0].toDouble(), v[1].toDouble()), v[2].toDouble()).toFloat().coerceAtLeast(1e-5f)
        return floatArrayOf(v[0] / len, v[1] / len, v[2] / len)
    }
}
