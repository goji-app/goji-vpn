import SwiftUI
import simd

/// Контуры материков/границ/стран из geo_globe.json (тот же файл, что в Android-клиенте).
final class GlobeGeometry {
    static let shared = GlobeGeometry()

    /// Отрезки в радианах: (lon1, lat1, lon2, lat2).
    private(set) var coast: [SIMD4<Double>] = []
    private(set) var borders: [SIMD4<Double>] = []
    private(set) var countries: [String: [[SIMD2<Double>]]] = [:]
    private(set) var loaded = false

    private init() {}

    func loadIfNeeded() {
        guard !loaded else { return }
        loaded = true
        guard let url = Bundle.main.url(forResource: "geo_globe", withExtension: "json"),
              let data = try? Data(contentsOf: url),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return }
        let rad = Double.pi / 180
        func segments(_ key: String) -> [SIMD4<Double>] {
            (root[key] as? [[NSNumber]] ?? []).compactMap { s in
                guard s.count == 4 else { return nil }
                return SIMD4(s[0].doubleValue * rad, s[1].doubleValue * rad, s[2].doubleValue * rad, s[3].doubleValue * rad)
            }
        }
        coast = segments("coast")
        borders = segments("borders")
        for (name, value) in root["countries"] as? [String: Any] ?? [:] {
            let rings = (value as? [[[NSNumber]]] ?? []).map { ring in
                ring.compactMap { p in p.count == 2 ? SIMD2(p[0].doubleValue * rad, p[1].doubleValue * rad) : nil }
            }
            countries[name] = rings
        }
    }
}

/// Глобус на «Главной»: ортографическая проекция, вращение, подсветка страны узла и дуга
/// маршрута «дом → узел» с бегущим бликом, пока идёт подключение или оно установлено.
struct GlobeView: View {
    enum Status { case off, connecting, on }

    let status: Status
    let node: CountryGeo?
    /// Откуда «выходит» пользователь (точка «дом»). Без геолокации — по часовому поясу.
    var home: (lat: Double, lon: Double) = GlobeView.homeGuess

    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        TimelineView(.animation(minimumInterval: 1.0 / 30, paused: reduceMotion && status == .off)) { timeline in
            Canvas { context, size in
                draw(context: &context, size: size, time: timeline.date.timeIntervalSinceReferenceDate)
            }
        }
        .onAppear { GlobeGeometry.shared.loadIfNeeded() }
        .accessibilityHidden(true)
    }

    private func draw(context: inout GraphicsContext, size: CGSize, time: TimeInterval) {
        let geo = GlobeGeometry.shared
        let radius = min(size.width, size.height) * 0.46
        let center = CGPoint(x: size.width / 2, y: size.height / 2)
        let rad = Double.pi / 180
        let dark = scheme == .dark

        // Центр вида: при подключении — середина маршрута, иначе медленное вращение.
        var lon0: Double
        var lat0: Double
        if let node, status != .off {
            lon0 = midLongitude(home.lon, node.lon) * rad
            lat0 = ((home.lat + node.lat) / 2 - 8) * rad
        } else {
            lon0 = (reduceMotion ? 30 : (time * 6).truncatingRemainder(dividingBy: 360)) * rad
            lat0 = 25 * rad
        }
        let sinLat0 = sin(lat0), cosLat0 = cos(lat0)

        func project(_ lon: Double, _ lat: Double) -> CGPoint? {
            let cosLat = cos(lat)
            let dLon = lon - lon0
            let cosC = sinLat0 * sin(lat) + cosLat0 * cosLat * cos(dLon)
            guard cosC > -0.02 else { return nil }
            let x = cosLat * sin(dLon)
            let y = cosLat0 * sin(lat) - sinLat0 * cosLat * cos(dLon)
            return CGPoint(x: center.x + radius * x, y: center.y - radius * y)
        }

        // Шар
        let sphere = Path(ellipseIn: CGRect(x: center.x - radius, y: center.y - radius, width: radius * 2, height: radius * 2))
        context.fill(sphere, with: .radialGradient(
            Gradient(colors: dark
                ? [Color(white: 0.16, opacity: 0.55), Color(white: 0.05, opacity: 0.35)]
                : [Color.white.opacity(0.75), Color.white.opacity(0.25)]),
            center: CGPoint(x: center.x - radius * 0.35, y: center.y - radius * 0.4),
            startRadius: 0, endRadius: radius * 1.4))
        context.stroke(sphere, with: .color(GojiColor.stroke), lineWidth: 1)

        // Подсветка страны узла
        if let node, status != .off, let rings = geo.countries[node.country] {
            var fill = Path()
            for ring in rings {
                var started = false
                for p in ring {
                    guard let pt = project(p.x, p.y) else { started = false; continue }
                    if started { fill.addLine(to: pt) } else { fill.move(to: pt); started = true }
                }
                fill.closeSubpath()
            }
            context.fill(fill, with: .color(GojiColor.teal.opacity(status == .on ? 0.35 : 0.2)))
        }

        // Береговые линии и границы
        var coastPath = Path()
        for s in geo.coast {
            guard let a = project(s.x, s.y), let b = project(s.z, s.w) else { continue }
            coastPath.move(to: a)
            coastPath.addLine(to: b)
        }
        context.stroke(coastPath, with: .color(GojiColor.textPrimary.opacity(dark ? 0.55 : 0.45)), lineWidth: 0.8)

        var borderPath = Path()
        for s in geo.borders {
            guard let a = project(s.x, s.y), let b = project(s.z, s.w) else { continue }
            borderPath.move(to: a)
            borderPath.addLine(to: b)
        }
        context.stroke(borderPath, with: .color(GojiColor.textPrimary.opacity(0.18)), lineWidth: 0.5)

        guard let node, status != .off else { return }

        // Дуга маршрута по большому кругу, приподнятая над поверхностью
        let a = unitVector(lat: home.lat * rad, lon: home.lon * rad)
        let b = unitVector(lat: node.lat * rad, lon: node.lon * rad)
        let steps = 48
        var arcPoints: [CGPoint] = []
        for i in 0...steps {
            let t = Double(i) / Double(steps)
            var v = slerp(a, b, t)
            let lift = 1 + 0.18 * sin(Double.pi * t)
            v = v * lift
            let lat = asin(max(-1, min(1, v.y / simd_length(v))))
            let lon = atan2(v.x, v.z)
            if let pt = project(lon, lat) {
                // поднимаем точку пропорционально высоте дуги
                let dx = pt.x - center.x, dy = pt.y - center.y
                arcPoints.append(CGPoint(x: center.x + dx * lift, y: center.y + dy * lift))
            }
        }
        if arcPoints.count > 1 {
            var arc = Path()
            arc.addLines(arcPoints)
            context.stroke(arc, with: .color(GojiColor.teal.opacity(0.85)),
                           style: StrokeStyle(lineWidth: 2, lineCap: .round, dash: status == .connecting ? [4, 5] : []))
            // Бегущий блик
            let speed = status == .connecting ? 0.9 : 0.45
            let phase = (time * speed).truncatingRemainder(dividingBy: 1)
            let index = min(arcPoints.count - 1, Int(phase * Double(arcPoints.count - 1)))
            let comet = arcPoints[index]
            context.fill(Path(ellipseIn: CGRect(x: comet.x - 4, y: comet.y - 4, width: 8, height: 8)), with: .color(GojiColor.teal))
            context.fill(Path(ellipseIn: CGRect(x: comet.x - 9, y: comet.y - 9, width: 18, height: 18)), with: .color(GojiColor.accentGlow))
        }

        // Точки «дом» и «узел»
        if let h = project(home.lon * rad, home.lat * rad) {
            context.fill(Path(ellipseIn: CGRect(x: h.x - 4, y: h.y - 4, width: 8, height: 8)), with: .color(GojiColor.terracotta))
        }
        if let n = project(node.lon * rad, node.lat * rad) {
            let pulse = status == .on ? 1 + 0.35 * sin(time * 3) : 1
            let r = 10 * pulse
            context.fill(Path(ellipseIn: CGRect(x: n.x - r, y: n.y - r, width: r * 2, height: r * 2)), with: .color(GojiColor.accentGlow))
            context.fill(Path(ellipseIn: CGRect(x: n.x - 5, y: n.y - 5, width: 10, height: 10)), with: .color(GojiColor.teal))
        }
    }

    private func unitVector(lat: Double, lon: Double) -> SIMD3<Double> {
        SIMD3(cos(lat) * sin(lon), sin(lat), cos(lat) * cos(lon))
    }

    private func slerp(_ a: SIMD3<Double>, _ b: SIMD3<Double>, _ t: Double) -> SIMD3<Double> {
        let dot = max(-1, min(1, simd_dot(a, b)))
        let omega = acos(dot)
        guard omega > 1e-6 else { return a }
        let s = sin(omega)
        return a * (sin((1 - t) * omega) / s) + b * (sin(t * omega) / s)
    }

    private func midLongitude(_ a: Double, _ b: Double) -> Double {
        var d = b - a
        if d > 180 { d -= 360 }
        if d < -180 { d += 360 }
        return a + d / 2
    }

    /// Грубая оценка «дома» по часовому поясу устройства (без запроса геолокации).
    static var homeGuess: (lat: Double, lon: Double) {
        let id = TimeZone.current.identifier
        let table: [String: (Double, Double)] = [
            "Europe/Moscow": (55.75, 37.62), "Europe/Kaliningrad": (54.71, 20.51), "Europe/Samara": (53.2, 50.15),
            "Asia/Yekaterinburg": (56.84, 60.6), "Asia/Omsk": (54.99, 73.37), "Asia/Novosibirsk": (55.03, 82.92),
            "Asia/Krasnoyarsk": (56.01, 92.85), "Asia/Irkutsk": (52.29, 104.28), "Asia/Vladivostok": (43.12, 131.89),
            "Europe/Minsk": (53.9, 27.56), "Asia/Almaty": (43.24, 76.95), "Europe/Kiev": (50.45, 30.52),
            "Europe/Kyiv": (50.45, 30.52), "Asia/Tbilisi": (41.72, 44.79), "Asia/Yerevan": (40.18, 44.51)
        ]
        if let hit = table[id] { return (hit.0, hit.1) }
        // По смещению UTC: долгота ≈ 15° на час, широта — средняя для населённых мест.
        let hours = Double(TimeZone.current.secondsFromGMT()) / 3600
        return (50, hours * 15)
    }
}
