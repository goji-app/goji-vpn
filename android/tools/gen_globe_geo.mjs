// Генерирует app/src/main/assets/geo_globe.json из того же датасета, что
// использует goji-globe.js (world-atlas@2 countries-110m), той же логикой
// topojson-client (mesh для берегов/границ, feature для полигонов подсветки).
// Запускать один раз локально: `node tools/gen_globe_geo.mjs`. Не часть сборки APK.
import * as topojson from "topojson-client";
import fetch from "node-fetch";
import { writeFileSync } from "fs";
import { fileURLToPath } from "url";
import { dirname, join } from "path";

const ATLAS = "https://cdn.jsdelivr.net/npm/world-atlas@2/countries-110m.json";

function ringsFromCoords(lines) {
  // [[ [lon,lat],[lon,lat] ], ...] -> отрезки [ [lon1,lat1,lon2,lat2], ... ]
  const segs = [];
  for (const line of lines) {
    for (let i = 0; i < line.length - 1; i++) {
      const a = line[i], b = line[i + 1];
      segs.push([round(a[0]), round(a[1]), round(b[0]), round(b[1])]);
    }
  }
  return segs;
}

function round(n) {
  return Math.round(n * 100) / 100;
}

async function main() {
  console.log("Fetching", ATLAS);
  const topo = await fetch(ATLAS).then(r => r.json());
  const countries = topo.objects.countries;

  const borders = topojson.mesh(topo, countries, (a, b) => a !== b);
  const coast = topojson.mesh(topo, countries, (a, b) => a === b);
  const features = topojson.feature(topo, countries).features;

  const borderSegs = ringsFromCoords(borders.coordinates);
  const coastSegs = ringsFromCoords(coast.coordinates);

  const countryPolys = {};
  for (const f of features) {
    const name = f.properties && f.properties.name;
    if (!name) continue;
    const polys = f.geometry.type === "Polygon" ? [f.geometry.coordinates] : f.geometry.coordinates;
    const rings = [];
    for (const poly of polys) {
      for (const ring of poly) {
        rings.push(ring.map(([lon, lat]) => [round(lon), round(lat)]));
      }
    }
    countryPolys[name] = rings;
  }

  const out = { coast: coastSegs, borders: borderSegs, countries: countryPolys };

  const __dirname = dirname(fileURLToPath(import.meta.url));
  const outPath = join(__dirname, "..", "app", "src", "main", "assets", "geo_globe.json");
  writeFileSync(outPath, JSON.stringify(out));
  console.log("Wrote", outPath);
  console.log("coast segs:", coastSegs.length, "border segs:", borderSegs.length, "countries:", Object.keys(countryPolys).length);
}

main().catch(e => { console.error(e); process.exit(1); });
