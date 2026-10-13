// <goji-globe> — three.js globe with real country outlines (world-atlas 110m).
// Attributes:
//   status  "off" | "connecting" | "on"   — dim / blink / locked & lit
//   node    node id (see GOJI_NODES)
//   theme   "dark" (default) | "light"
//   label   "on" — floating country label at the connected node (off by default)
//   satellites "on" — orbiting satellites (login screen only; read once at boot)
//
// Windows-порт: эталон handoff-1.0.77/reference/goji-globe.js (дизайн v5 «Стекло») плюс
// три доработки — библиотеки из локальной vendor/ вместо CDN, реальный узел подписки
// (setNode) и перелёт маркера при смене узла (как GojiGlobeRenderer.kt в Android).
const GOJI_NODES = {
  nl: { lat: 52.37, lon: 4.9, country: 'Netherlands', city: 'Амстердам', title: 'Нидерланды' },
  de: { lat: 50.11, lon: 8.68, country: 'Germany', city: 'Франкфурт', title: 'Германия' },
  fi: { lat: 60.17, lon: 24.94, country: 'Finland', city: 'Хельсинки', title: 'Финляндия' },
  spb: { lat: 59.94, lon: 30.31, country: 'Russia', city: 'Санкт-Петербург', title: 'Россия' },
  tr: { lat: 41.01, lon: 28.98, country: 'Turkey', city: 'Стамбул', title: 'Турция' },
  us: { lat: 40.71, lon: -74.01, country: 'United States of America', city: 'Нью-Йорк', title: 'США' },
  jp: { lat: 35.68, lon: 139.77, country: 'Japan', city: 'Токио', title: 'Япония' },
  auto: { lat: 60.17, lon: 24.94, country: 'Finland', city: 'Хельсинки', title: 'Финляндия' }
};
const HOME = { lat: 55.75, lon: 37.62 };
// Живые CDN-URL (cdn.jsdelivr.net/esm.sh) заменены локальными копиями: без интернета или при
// блокировке этих доменов глобус молча не рисовал береговые линии. three.js/topojson-client/
// world-atlas лежат в vendor/ и отдаются тем же виртуальным хостом godji.local, что и
// globe.html (см. GlobeHost.xaml.cs → SetVirtualHostNameToFolderMapping).
const ATLAS = './vendor/countries-110m.json';

const THEMES = {
  dark: { ocean: 0x0a201d, oceanOp: 0.9, land: 0x2f6f66, landOp: 0.75, grid: 0x00d4c4, gridOp: 0.07, hi: 0x00e7d4, arc: 0x00e7d4, home: 0x8b7cf6, atmo: 0x00d4c4, dot: 0x4a625d, labelBg: 'rgba(10,20,18,.82)', labelFg: '#EAF4F2', labelBd: 'rgba(0,231,212,.45)' },
  light: { ocean: 0xe7e0cf, oceanOp: 1, land: 0x0f4d45, landOp: 0.55, grid: 0x0f4d45, gridOp: 0.06, hi: 0x00897e, arc: 0xd9714b, home: 0xd9714b, atmo: 0x00a79b, dot: 0xa9a08a, labelBg: 'rgba(255,253,247,.94)', labelFg: '#12312C', labelBd: 'rgba(0,167,155,.5)' }
};

let atlasPromise = null;
const loadAtlas = async () => {
  if (!atlasPromise) {
    atlasPromise = (async () => {
      const [topo, tj] = await Promise.all([
        fetch(ATLAS).then(r => r.json()),
        import('./vendor/topojson-client.js')
      ]);
      return {
        borders: tj.mesh(topo, topo.objects.countries, (a, b) => a !== b),
        coast: tj.mesh(topo, topo.objects.countries, (a, b) => a === b),
        features: tj.feature(topo, topo.objects.countries).features
      };
    })().catch(() => null);
  }
  return atlasPromise;
};

class GojiGlobe extends HTMLElement {
  static get observedAttributes() { return ['status', 'node', 'theme']; }

  connectedCallback() {
    this.style.display = 'block';
    this.style.position = 'absolute';
    this.style.inset = '0';
    this.style.width = '100%';
    this.style.height = '100%';
    if (!this._booted) { this._booted = true; this.boot(); }
  }

  attributeChangedCallback() { if (this._apply) this._apply(); }

  // Реальный узел подписки {lat, lon, country, city, title}: country должен совпадать с
  // properties.name слоя world-atlas (для контура страны). Вызывается из C# через
  // CoreWebView2.ExecuteScriptAsync (window.godjiSetNode в globe.html).
  setNode(data) {
    this._dynamicNode = data;
    this._dynamicNodeChanged = true;
    if (this._apply) this._apply();
  }

  // Пауза рендера, пока глобус не виден (другая вкладка, свёрнутое окно, трей) — вызывается
  // из C# (window.godjiSetPaused в globe.html). Без неё three.js рисовал 30–60 кадров в
  // секунду в невидимый холст, а WPF ещё и перекладывал каждый кадр в своё окно.
  setPaused(paused) {
    this._paused = !!paused;
    if (this._paused) { cancelAnimationFrame(this._raf); this._raf = 0; }
    else if (!this._raf && this._loop) { this._last = 0; this._raf = requestAnimationFrame(this._loop); }
  }

  disconnectedCallback() {
    cancelAnimationFrame(this._raf);
    this._ro && this._ro.disconnect();
    this._renderer && this._renderer.dispose();
  }

  size() {
    const r = this.getBoundingClientRect();
    let w = Math.round(r.width), h = Math.round(r.height);
    if (!w || !h) {
      const p = this.parentElement && this.parentElement.getBoundingClientRect();
      if (p) { w = w || Math.round(p.width); h = h || Math.round(p.height); }
    }
    return { w: w || 380, h: h || 260 };
  }

  async boot() {
    let THREE;
    try { THREE = await import('./vendor/three.module.js'); } catch (e) { return; }
    if (!this.isConnected) return;

    const R = 1.28;
    const themeName = () => (this.getAttribute('theme') === 'light' ? 'light' : 'dark');
    let T = THEMES[themeName()];

    const { w, h } = this.size();
    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(38, w / h, 0.1, 100);
    camera.position.set(0, 0, 5.1);

    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    renderer.setSize(w, h);
    Object.assign(renderer.domElement.style, { display: 'block', width: '100%', height: '100%' });
    this.appendChild(renderer.domElement);
    this._renderer = renderer;

    const globe = new THREE.Group();
    scene.add(globe);

    const toVec = (lat, lon, r = R) => {
      const p = (90 - lat) * Math.PI / 180, t = (lon + 180) * Math.PI / 180;
      return new THREE.Vector3(-r * Math.sin(p) * Math.cos(t), r * Math.cos(p), r * Math.sin(p) * Math.sin(t));
    };

    const ocean = new THREE.Mesh(
      new THREE.SphereGeometry(R, 64, 48),
      new THREE.MeshBasicMaterial({ color: T.ocean, transparent: true, opacity: T.oceanOp })
    );
    globe.add(ocean);

    const grid = new THREE.LineSegments(
      new THREE.WireframeGeometry(new THREE.SphereGeometry(R * 1.001, 24, 12)),
      new THREE.LineBasicMaterial({ color: T.grid, transparent: true, opacity: T.gridOp })
    );
    globe.add(grid);

    const atmo = new THREE.Mesh(
      new THREE.SphereGeometry(R * 1.16, 40, 28),
      new THREE.MeshBasicMaterial({ color: T.atmo, transparent: true, opacity: 0.06, side: THREE.BackSide })
    );
    scene.add(atmo);

    // ── real geography ───────────────────────────────────────────
    const lineFromCoords = (lines, color, opacity, r) => {
      const pts = [];
      lines.forEach(line => {
        for (let i = 0; i < line.length - 1; i++) {
          pts.push(toVec(line[i][1], line[i][0], r), toVec(line[i + 1][1], line[i + 1][0], r));
        }
      });
      const g = new THREE.BufferGeometry().setFromPoints(pts);
      return new THREE.LineSegments(g, new THREE.LineBasicMaterial({ color, transparent: true, opacity }));
    };

    let coastLines = null, borderLines = null, highlight = null, atlas = null;
    loadAtlas().then(a => {
      if (!a || !this.isConnected) return;
      atlas = a;
      coastLines = lineFromCoords(a.coast.coordinates, T.land, T.landOp, R * 1.004);
      borderLines = lineFromCoords(a.borders.coordinates, T.land, T.landOp * 0.5, R * 1.004);
      globe.add(coastLines, borderLines);
      this._apply();
    });

    let hlName = null, hlCore = null, hlGlow = null;
    const setHighlight = name => {
      if (name === hlName && (highlight || !atlas)) return;
      if (highlight) {
        globe.remove(highlight);
        highlight.traverse(o => { if (o.geometry) o.geometry.dispose(); });
        highlight = null;
      }
      hlName = name;
      if (!atlas || !name) return;
      const f = atlas.features.find(x => x.properties && x.properties.name === name);
      if (!f) return;
      const polys = f.geometry.type === 'Polygon' ? [f.geometry.coordinates] : f.geometry.coordinates;
      const rings = [];
      polys.forEach(p => p.forEach(ring => { if (ring.length > 3) rings.push(ring); }));
      hlCore = new THREE.MeshBasicMaterial({ color: T.hi, transparent: true, opacity: 1 });
      hlGlow = new THREE.MeshBasicMaterial({ color: T.hi, transparent: true, opacity: 0.28, depthWrite: false });
      highlight = new THREE.Group();
      rings.forEach(ring => {
        const path = new THREE.CurvePath();
        for (let i = 0; i < ring.length - 1; i++) {
          path.add(new THREE.LineCurve3(toVec(ring[i][1], ring[i][0], R * 1.014), toVec(ring[i + 1][1], ring[i + 1][0], R * 1.014)));
        }
        const segs = Math.min(600, Math.max(24, ring.length * 2));
        highlight.add(new THREE.Mesh(new THREE.TubeGeometry(path, segs, 0.0055, 5, false), hlCore));
        highlight.add(new THREE.Mesh(new THREE.TubeGeometry(path, segs, 0.016, 6, false), hlGlow));
      });
      globe.add(highlight);
    };

    // ── pins ─────────────────────────────────────────────────────
    const pins = {};
    Object.keys(GOJI_NODES).forEach(id => {
      if (id === 'auto') return;
      const m = new THREE.Mesh(
        new THREE.SphereGeometry(0.028, 12, 12),
        new THREE.MeshBasicMaterial({ color: T.dot })
      );
      m.position.copy(toVec(GOJI_NODES[id].lat, GOJI_NODES[id].lon, R * 1.012));
      globe.add(m);
      pins[id] = m;
    });

    const home = new THREE.Mesh(
      new THREE.SphereGeometry(0.015, 14, 14),
      new THREE.MeshBasicMaterial({ color: T.home })
    );
    home.position.copy(toVec(HOME.lat, HOME.lon, R * 1.012));
    globe.add(home);

    // connection marker: solid core + two expanding rings, laid flat on the surface
    const marker = new THREE.Group();
    const core = new THREE.Mesh(
      new THREE.SphereGeometry(0.02, 16, 16),
      new THREE.MeshBasicMaterial({ color: 0xffffff })
    );
    const halo = new THREE.Mesh(
      new THREE.SphereGeometry(0.036, 16, 16),
      new THREE.MeshBasicMaterial({ color: T.hi, transparent: true, opacity: 0.4 })
    );
    const ringGeo = new THREE.RingGeometry(0.038, 0.044, 40);
    const rings = [0, 1].map(() => new THREE.Mesh(
      ringGeo, new THREE.MeshBasicMaterial({ color: T.hi, transparent: true, opacity: 0.7, side: THREE.DoubleSide })
    ));
    marker.add(core, halo, rings[0], rings[1]);
    marker.visible = false;
    globe.add(marker);

    let arc = null, curve = null;
    const buildArc = id => {
      if (arc) { globe.remove(arc); arc.geometry.dispose(); arc = null; }
      const n = this._dynamicNode || GOJI_NODES[id] || GOJI_NODES.auto;
      const a = toVec(HOME.lat, HOME.lon, R * 1.012), b = toVec(n.lat, n.lon, R * 1.012);
      const mid = a.clone().add(b).multiplyScalar(0.5).normalize().multiplyScalar(R * 1.32);
      curve = new THREE.QuadraticBezierCurve3(a, mid, b);
      arc = new THREE.Mesh(
        new THREE.TubeGeometry(curve, 96, 0.0032, 6, false),
        new THREE.MeshBasicMaterial({ color: T.arc, transparent: true, opacity: 0.35 })
      );
      globe.add(arc);
    };

    // ── flight (Windows port, как e3012ab в Android) ─────────────
    // При смене местоположения узла, пока маркер виден, от прежней страны к новой
    // прорисовывается дуга (тем выше, чем дальше лететь), по ней летит светящаяся голова,
    // затем дуга гаснет: 1.6 с полёт + 0.8 с затухание, ease-in-out.
    let flight = null;
    const FLIGHT_SEGS = 96, FLY = 1.6, FADE = 0.8;
    const clearFlight = () => {
      if (!flight) return;
      globe.remove(flight.line); globe.remove(flight.head);
      flight.line.geometry.dispose(); flight.line.material.dispose();
      flight.head.geometry.dispose(); flight.head.material.dispose();
      flight = null;
    };
    const startFlight = (from, to) => {
      clearFlight();
      const a = toVec(from.lat, from.lon, R * 1.014), b = toVec(to.lat, to.lon, R * 1.014);
      const angle = a.angleTo(b);
      if (angle < 0.01) return;
      const mid = a.clone().add(b).multiplyScalar(0.5).normalize().multiplyScalar(R * (1.18 + 0.32 * angle / Math.PI));
      const c = new THREE.QuadraticBezierCurve3(a, mid, b);
      const geo = new THREE.BufferGeometry().setFromPoints(c.getPoints(FLIGHT_SEGS));
      geo.setDrawRange(0, 0);
      const line = new THREE.Line(geo, new THREE.LineBasicMaterial({ color: T.hi, transparent: true, opacity: 0.9 }));
      const head = new THREE.Mesh(new THREE.SphereGeometry(0.018, 12, 12),
        new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 1 }));
      globe.add(line); globe.add(head);
      flight = { curve: c, line, head, start: t };
    };
    const ease = x => (x < 0.5 ? 2 * x * x : 1 - Math.pow(-2 * x + 2, 2) / 2);

    // data stream: a train of tiny beads flowing A → B, brightest at the head
    const STREAM = 9;
    const beads = Array.from({ length: STREAM }, (_, i) => {
      const m = new THREE.Mesh(
        new THREE.SphereGeometry(i === 0 ? 0.013 : 0.009 - i * 0.0005, 10, 10),
        new THREE.MeshBasicMaterial({ color: i === 0 ? 0xffffff : T.arc, transparent: true, opacity: 0 })
      );
      m.visible = false; globe.add(m); return m;
    });
    const packet = beads[0];

    // ── satellites (opt-in: satellites="on") ──────────────────────
    const sats = [];
    const satLayer = new THREE.Group();
    scene.add(satLayer);
    if (this.getAttribute('satellites') === 'on') {
      const bodyMat = new THREE.MeshBasicMaterial({ color: 0xE9EEF2 });
      const panelMat = new THREE.MeshBasicMaterial({ color: T.hi, transparent: true, opacity: 0.9 });
      const orbitMat = new THREE.LineBasicMaterial({ color: T.hi, transparent: true, opacity: 0.12 });
      const N = 16;
      for (let i = 0; i < N; i++) {
        const r = R * (1.16 + (i % 4) * 0.07 + Math.random() * 0.03);
        const plane = new THREE.Group();
        plane.rotation.set((Math.random() - 0.5) * 2.2, Math.random() * Math.PI * 2, (Math.random() - 0.5) * 1.2);
        const pts = [];
        for (let k = 0; k <= 96; k++) { const a = k / 96 * Math.PI * 2; pts.push(new THREE.Vector3(Math.cos(a) * r, 0, Math.sin(a) * r)); }
        if (i % 2 === 0) plane.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(pts), orbitMat));
        const sat = new THREE.Group();
        const s = 0.6 + Math.random() * 0.5;
        sat.add(new THREE.Mesh(new THREE.BoxGeometry(0.022 * s, 0.022 * s, 0.03 * s), bodyMat));
        const pg = new THREE.BoxGeometry(0.05 * s, 0.003, 0.022 * s);
        const p1 = new THREE.Mesh(pg, panelMat); p1.position.x = 0.04 * s;
        const p2 = new THREE.Mesh(pg, panelMat); p2.position.x = -0.04 * s;
        const blink = new THREE.Mesh(new THREE.SphereGeometry(0.006 * s, 6, 6), new THREE.MeshBasicMaterial({ color: i % 3 ? 0xFF5A4E : 0xFFFFFF, transparent: true }));
        blink.position.y = 0.016 * s;
        sat.add(p1, p2, blink);
        plane.add(sat);
        satLayer.add(plane);
        sats.push({ sat, blink, r, a: Math.random() * Math.PI * 2, sp: (0.0025 + Math.random() * 0.004) * (i % 3 === 0 ? -1 : 1), ph: Math.random() * 6 });
      }
      this._satMats = { panelMat, orbitMat };
    }

    // ── floating label ───────────────────────────────────────────
    const tag = document.createElement('div');
    Object.assign(tag.style, {
      position: 'absolute', left: '0', top: '0', transform: 'translate(-50%,-140%)',
      padding: '5px 9px', borderRadius: '10px', font: '700 11px/1.2 Manrope, system-ui, sans-serif',
      whiteSpace: 'nowrap', pointerEvents: 'none', opacity: '0', transition: 'opacity .3s ease',
      display: 'flex', alignItems: 'center', gap: '6px', backdropFilter: 'blur(8px)'
    });
    this.appendChild(tag);

    let status = 'off', nodeId = 'auto', targetY = 0, targetX = -0.2, t = 0, locked = false, shownNode = null;

    this._apply = () => {
      const nt = THEMES[themeName()];
      if (nt !== T) {
        T = nt;
        ocean.material.color.setHex(T.ocean); ocean.material.opacity = T.oceanOp;
        grid.material.color.setHex(T.grid); grid.material.opacity = T.gridOp;
        atmo.material.color.setHex(T.atmo);
        if (coastLines) { coastLines.material.color.setHex(T.land); coastLines.material.opacity = T.landOp; }
        if (borderLines) { borderLines.material.color.setHex(T.land); borderLines.material.opacity = T.landOp * 0.5; }
        home.material.color.setHex(T.home);
        halo.material.color.setHex(T.hi); rings.forEach(r => r.material.color.setHex(T.hi));
        if (arc) arc.material.color.setHex(T.arc);
        if (hlCore) { hlCore.color.setHex(T.hi); hlGlow.color.setHex(T.hi); }
        if (this._satMats) { this._satMats.panelMat.color.setHex(T.hi); this._satMats.orbitMat.color.setHex(T.hi); }
        if (flight) flight.line.material.color.setHex(T.hi);
      }
      const prevStatus = status;
      status = this.getAttribute('status') || 'off';
      const n = this.getAttribute('node') || 'auto';
      const prevNode = shownNode;
      if (n !== nodeId || !arc || this._dynamicNodeChanged) {
        nodeId = n; this._dynamicNodeChanged = false; buildArc(nodeId); locked = false;
      }
      const nd = this._dynamicNode || GOJI_NODES[nodeId] || GOJI_NODES.auto;
      shownNode = nd;
      // Перелёт — только если маркер уже был виден и место реально сменилось.
      if (prevNode && prevStatus !== 'off' && status !== 'off' &&
          (Math.abs(prevNode.lat - nd.lat) > 0.01 || Math.abs(prevNode.lon - nd.lon) > 0.01)) {
        startFlight(prevNode, nd);
      }
      if (status === 'off') clearFlight();

      Object.keys(pins).forEach(id => {
        const active = id === nodeId || (nodeId === 'auto' && id === 'fi');
        pins[id].visible = false;
        pins[id].material.color.setHex(T.dot);
      });

      const at = toVec(nd.lat, nd.lon, R * 1.014);
      marker.position.copy(at);
      marker.lookAt(at.clone().multiplyScalar(2));
      marker.visible = status !== 'off';
      home.visible = status !== 'off';
      setHighlight(status === 'off' ? null : nd.country);

      // frame the node (biased toward it, home still in view) and lock once connected
      const framing = at.clone().normalize();
      targetY = -Math.atan2(framing.x, framing.z);
      const y0 = framing.y, z0 = Math.hypot(framing.x, framing.z);
      targetX = Math.atan2(y0, z0);
      if (status !== 'on') locked = false;
      if (prevStatus !== status && status === 'off') { tag.style.opacity = '0'; }

      tag.innerHTML = '<span style="width:6px;height:6px;border-radius:50%;background:' +
        (status === 'on' ? '#00E7D4' : '#E8B84B') + '"></span>' + nd.title + ' · ' + nd.city;
      tag.style.background = T.labelBg;
      tag.style.color = T.labelFg;
      tag.style.border = '1px solid ' + T.labelBd;
    };
    this._apply();
    globe.rotation.y = targetY;
    globe.rotation.x = targetX;

    const v = new THREE.Vector3();
    let lastW = 0, lastH = 0, frame = 0;
    const fit = () => {
      const s = this.size();
      if (!s.w || !s.h || (s.w === lastW && s.h === lastH)) return;
      lastW = s.w; lastH = s.h;
      camera.aspect = s.w / s.h; camera.updateProjectionMatrix(); renderer.setSize(s.w, s.h);
    };
    // Не чаще ~24 кадров в секунду: глобусу хватает, а нагрузка на процессор (и рендер
    // WebView2, и перенос каждого кадра в окно WPF) в 2,5 раза ниже, чем на 60 Гц. Всё движение ниже
    // рассчитано на шаг 16 мс, поэтому за один показанный кадр делаем столько шагов, сколько
    // их прошло (обычно два) — скорость вращения и пульсаций остаётся прежней.
    const loop = (now) => {
      this._raf = requestAnimationFrame(loop);
      if (this._last && now - this._last < 40) return;
      const steps = this._last ? Math.min(5, Math.max(1, Math.round((now - this._last) / 16.7))) : 1;
      this._last = now;
      for (let i = 0; i < steps; i++) step();
      renderer.render(scene, camera);
    };
    this._loop = loop;
    const step = () => {
      if ((frame++ % 10) === 0) fit();
      t += 0.016;
      const on = status === 'on', connecting = status === 'connecting';

      const wrapY = () => { const d = targetY - globe.rotation.y; return d - Math.round(d / (Math.PI * 2)) * Math.PI * 2; };
      if (on) {
        const dy = wrapY(), dx = targetX - globe.rotation.x;
        if (Math.abs(dy) < 0.002 && Math.abs(dx) < 0.002) { locked = true; }
        if (!locked) { globe.rotation.y += dy * 0.06; globe.rotation.x += dx * 0.06; }
      } else if (connecting) {
        globe.rotation.y += wrapY() * 0.05;
        globe.rotation.x += (targetX - globe.rotation.x) * 0.05;
      } else {
        globe.rotation.y += 0.0013;
        globe.rotation.x += (-0.16 - globe.rotation.x) * 0.02;
      }

      if (arc) { arc.visible = on || connecting; arc.material.opacity = on ? 0.35 : 0.15 + Math.sin(t * 5) * 0.1; }
      if (curve) {
        const head = (t * (on ? 0.45 : 0.25)) % 1;
        beads.forEach((bd, i) => {
          bd.visible = on || connecting;
          const p = head - i * 0.022;
          if (p < 0 || p > 1) { bd.material.opacity = 0; return; }
          bd.position.copy(curve.getPoint(p));
          bd.material.opacity = (1 - i / STREAM) * Math.sin(p * Math.PI) * (on ? 1 : 0.7);
        });
      }
      rings.forEach((r, i) => {
        const p = ((t * 0.55) + i * 0.5) % 1;
        r.scale.setScalar(0.6 + p * 1.9);
        r.material.opacity = (on ? 0.75 : 0.5) * (1 - p);
      });
      halo.material.opacity = (on ? 0.42 : 0.25) + Math.sin(t * 2.4) * 0.08;
      if (highlight && hlCore) {
        hlCore.opacity = on ? 1 : 0.5 + Math.sin(t * 4) * 0.3;
        hlGlow.opacity = (on ? 0.3 : 0.15) + Math.sin(t * 2.2) * 0.1;
      }
      atmo.material.opacity = on ? 0.09 : 0.05;

      // label follows the marker, hidden when it swings behind the globe
      if (marker.visible) {
        v.copy(marker.position).applyMatrix4(globe.matrixWorld);
        const front = v.clone().normalize().dot(camera.position.clone().normalize()) > 0.16;
        const s = this.size();
        v.project(camera);
        tag.style.left = ((v.x * 0.5 + 0.5) * s.w) + 'px';
        tag.style.top = ((-v.y * 0.5 + 0.5) * s.h) + 'px';
        tag.style.opacity = front && this.getAttribute('label') === 'on' ? '1' : '0';
      } else tag.style.opacity = '0';

      if (flight) {
        const el = t - flight.start;
        const p = ease(Math.min(1, el / FLY));
        flight.line.geometry.setDrawRange(0, Math.max(2, Math.round(p * FLIGHT_SEGS) + 1));
        flight.head.position.copy(flight.curve.getPoint(p));
        const fade = el <= FLY ? 1 : Math.max(0, 1 - (el - FLY) / FADE);
        flight.line.material.opacity = 0.9 * fade;
        flight.head.material.opacity = fade;
        if (el > FLY + FADE) clearFlight();
      }

      sats.forEach(o => {
        o.a += o.sp;
        o.sat.position.set(Math.cos(o.a) * o.r, 0, Math.sin(o.a) * o.r);
        o.sat.rotation.y = -o.a;
        o.blink.material.opacity = Math.sin(t * 5 + o.ph) > 0.6 ? 1 : 0.15;
      });
    };
    globe.updateMatrixWorld();
    step();
    renderer.render(scene, camera);
    if (!this._paused) this._raf = requestAnimationFrame(loop);

    const resize = () => fit();
    resize();
    this._ro = new ResizeObserver(resize);
    this._ro.observe(this);
    if (this.parentElement) this._ro.observe(this.parentElement);
  }
}
if (!customElements.get('goji-globe')) customElements.define('goji-globe', GojiGlobe);
