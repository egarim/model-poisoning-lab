/* Daemon renderer: draws any daemon spec (specs/*.json) on a Canvas 2D context.
   Pure functions of (spec, state, time). Sizes in body radii R; side s = -1 left, +1 right.
   state: { A, I, stage 1-5, mode idle|listening|consulting|speaking, calm, look{x,y}, blink 0-1, sigil, motes[] }
   Ported later to React Native Skia: only the ctx calls need replacing. */
(function (root) {
  "use strict";
  const TAU = Math.PI * 2;
  const ELEMENTS = { fire: { h: 22, s: 88 }, water: { h: 192, s: 70 }, earth: { h: 104, s: 46 }, air: { h: 268, s: 55 }, moon: { h: 222, s: 26 } };
  const hsla = (h, s, l, a) => `hsla(${((h % 360) + 360) % 360},${Math.max(0, s)}%,${Math.max(0, Math.min(100, l))}%,${a})`;

  // ---------- sigil (seeded by the daemon's name) ----------
  function hash(s) { let h = 2166136261; for (const c of s) { h ^= c.codePointAt(0); h = Math.imul(h, 16777619); } return h >>> 0; }
  function rng(seed) { let a = seed; return () => { a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
  function makeSigil(name, opts) {
    const r = rng(hash((name || "daemon").trim().toLowerCase()));
    const n = opts && opts.points ? opts.points : 5 + Math.floor(r() * 5);
    const k = Math.max(2, 2 + Math.floor(r() * (Math.floor(n / 2) - 1)));
    const ticks = Array.from({ length: 10 + Math.floor(r() * 10) }, () => ({ a: r() * TAU, len: 0.05 + r() * 0.09, kind: Math.floor(r() * 3) }));
    return { n, k, ticks, hueShift: (r() - 0.5) * 24, rot: r() * TAU };
  }

  function palette(spec, st) {
    const e = ELEMENTS[spec.element] || ELEMENTS.water;
    const h = e.h + (spec.hueShift || 0) + (st.sigil ? st.sigil.hueShift : 0);
    const s = Math.max(10, e.s * (1 - 0.45 * st.I) * (st.calm ? 0.5 : 1));
    const l = 52 + 10 * st.A - 8 * st.I;
    // coat: real fur/feather colours as [h,s,l]; without one, the coat is the element's colour (spirit look)
    const c = spec.coat, dl = 6 * st.A - 6 * st.I, ds = (st.calm ? 0.6 : 1) * (1 - 0.3 * st.I);
    const adj = v => [v[0], v[1] * ds, v[2] + dl];
    const coat = c ? {
      base: adj(c.base), light: adj(c.light || [c.base[0], c.base[1] * 0.5, Math.min(96, c.base[2] + 30)]),
      dark: adj(c.dark || [c.base[0], c.base[1], Math.max(8, c.base[2] - 30)]), accent: adj(c.accent || c.base),
      accent2: adj(c.accent2 || c.accent || c.base), nose: c.nose || [350, 25, 25]
    } : {
      base: [h, s, l], light: [h, s * 0.7, l + 26], dark: [h, s, l - 30], accent: [h, Math.max(45, s), l + 6], accent2: [h + 40, Math.max(45, s), l + 6], nose: [h, s * 0.6, l - 34]
    };
    return { h, s, l, coat, real: !!c };
  }
  function col(p, part, name, alpha = 0.92) {
    const n = name || (part && part.color) || (part && part.hue !== undefined ? "accent" : "body");
    if (Array.isArray(n)) return hsla(n[0], n[1], n[2], alpha);
    const k = p.coat;
    if (n === "light") return hsla(...k.light, alpha);
    if (n === "dark" || (part && part.dark && n === "body")) return hsla(...k.dark, alpha);
    if (n === "accent") return p.real ? hsla(...k.accent, alpha) : hsla(p.h + ((part && part.hue) || 0), Math.max(45, p.s), p.l + 6, alpha);
    if (n === "accent2") return hsla(...k.accent2, alpha);
    if (n === "nose") return hsla(...k.nose, alpha);
    return hsla(k.base[0], k.base[1], k.base[2] - (p.real ? 5 : 12), alpha);
  }

  // ---------- primitives ----------
  const P = {};

  P.ear = (c, g, part) => {
    const { cx, cy, R, k, m, p } = g;
    const ang = part.angle + (part.droop || 0) * m.I * 0.7 - 0.08 * m.A;
    for (const s of [-1, 1]) {
      const base = -Math.PI / 2 + s * ang;
      if (part.style === "floppy") {
        const swing = Math.sin(m.t * 1.5 + s) * 0.08 * (m.calm ? 0.3 : 1) + 0.25 * m.I - 0.2 * m.A;
        const ax = cx + s * R * 0.62, ay = cy - R * 0.72, L = R * part.size * k;
        c.save(); c.translate(ax, ay); c.rotate(s * (0.35 + swing));
        c.fillStyle = col(p, part, part.color || "dark"); c.beginPath();
        c.moveTo(-s * R * 0.12, 0); c.bezierCurveTo(s * R * 0.45, -R * 0.1, s * R * 0.4, L * 0.8, s * R * 0.12, L);
        c.bezierCurveTo(-s * R * 0.12, L * 0.95, -s * R * 0.15, L * 0.3, -s * R * 0.12, 0); c.fill();
        c.strokeStyle = col(p, part, "light", 0.35); c.lineWidth = Math.max(1.2, R * 0.025); c.stroke(); c.restore();
        continue;
      }
      if (part.style === "round") {
        const ex = cx + Math.cos(base) * R * 0.98, ey = cy + Math.sin(base) * R * 1.02;
        c.fillStyle = col(p, part, part.outer); c.beginPath(); c.arc(ex, ey, R * part.size * k, 0, TAU); c.fill();
        c.fillStyle = col(p, part, part.inner || "light", 0.5); c.beginPath(); c.arc(ex, ey + R * 0.03, R * part.size * 0.55 * k, 0, TAU); c.fill();
        continue;
      }
      const w = part.width || 0.3;
      const b1 = base - w, b2 = base + w;
      const x1 = cx + Math.cos(b1) * R * 0.9, y1 = cy + Math.sin(b1) * R * 0.98;
      const x2 = cx + Math.cos(b2) * R * 0.9, y2 = cy + Math.sin(b2) * R * 0.98;
      const bent = part.bent === s;
      const tipA = base + s * ((0.06 + 0.04 * Math.sin(m.t * 1.3 + s)) * (m.calm ? 0.3 : 1) + (bent ? 0.55 : 0));
      const sz = part.size * (bent ? 0.82 : 1);
      const tx = cx + Math.cos(tipA) * R * sz * k, ty = cy + Math.sin(tipA) * R * sz * k;
      const ear = (shrink, fill) => {
        const ix1 = x1 + (tx - x1) * shrink * 0.25, iy1 = y1 + (ty - y1) * shrink * 0.25;
        const ix2 = x2 + (tx - x2) * shrink * 0.25, iy2 = y2 + (ty - y2) * shrink * 0.25;
        const itx = tx + (cx - tx) * shrink * 0.18, ity = ty + (cy - ty) * shrink * 0.18;
        c.fillStyle = fill; c.beginPath(); c.moveTo(ix1, iy1);
        c.quadraticCurveTo((ix1 + itx) / 2 - s * R * 0.06, (iy1 + ity) / 2, itx, ity);
        c.quadraticCurveTo((ix2 + itx) / 2 + s * R * 0.08, (iy2 + ity) / 2, ix2, iy2); c.closePath(); c.fill();
      };
      ear(0, col(p, part));
      if (part.inner) ear(1, col(p, part, part.inner, 0.55));
    }
  };

  P.tail = (c, g, part) => {
    const { cx, cy, R, k, m, p } = g;
    if (part.style === "plume") { // fan of long feathers below/behind
      const n = part.count || 5;
      for (let i = 0; i < n; i++) {
        const u = n === 1 ? 0 : i / (n - 1) - 0.5;
        const sway = Math.sin(m.t * 1.2 + i) * 0.05 * (m.calm ? 0.3 : 1);
        const a = Math.PI / 2 + u * 1.3 + sway;
        const L = R * part.length * k * (1 - Math.abs(u) * 0.35);
        const x0 = cx, y0 = cy + R * 0.6, x1 = x0 + Math.cos(a) * L, y1 = y0 + Math.sin(a) * L * 0.8;
        c.strokeStyle = col(p, part, "body", 0.8); c.lineWidth = Math.max(2, R * 0.07); c.lineCap = "round";
        c.beginPath(); c.moveTo(x0, y0); c.quadraticCurveTo((x0 + x1) / 2 + u * R * 0.3, (y0 + y1) / 2, x1, y1); c.stroke();
        if (part.tip === "eye") {
          c.fillStyle = col(p, part, "accent", 0.9); c.beginPath(); c.ellipse(x1, y1, R * 0.12, R * 0.16, a - Math.PI / 2, 0, TAU); c.fill();
          c.fillStyle = hsla(p.h + 180, 60, 45, 0.9); c.beginPath(); c.arc(x1, y1, R * 0.05, 0, TAU); c.fill();
        } else { c.fillStyle = col(p, part, part.tip || "light", 0.85); c.beginPath(); c.ellipse(x1, y1, R * 0.07, R * 0.12, a - Math.PI / 2, 0, TAU); c.fill(); }
      }
      return;
    }
    const s = part.side || 1;
    const wag = Math.sin(m.t * (1.2 + 3 * m.A)) * 0.22 * (m.calm ? 0.2 : 1);
    if (part.style === "stub") { c.fillStyle = col(p, part, part.color); c.beginPath(); c.arc(cx + s * R * 0.85, cy + R * 0.7, R * 0.2, 0, TAU); c.fill(); return; }
    if (part.style === "curl" || part.style === "thin") {
      const L = part.length * k, lw = R * (part.style === "curl" ? 0.2 : 0.11);
      c.strokeStyle = col(p, part, part.color); c.lineWidth = lw; c.lineCap = "round"; c.beginPath();
      const x0 = cx + s * R * 0.7, y0 = cy + R * 0.6;
      if (part.style === "curl") { c.moveTo(x0, y0); c.bezierCurveTo(x0 + s * R * 0.7, y0 - R * 0.1, x0 + s * R * (0.75 + wag), y0 - R * 0.9 * L, x0 + s * R * (0.25 + wag), y0 - R * 0.75 * L); }
      else { c.moveTo(x0, y0); c.bezierCurveTo(x0 + s * R * 0.9 * L, y0 + R * 0.2, x0 + s * R * (0.5 + wag) * L, y0 - R * 0.9 * L + tuckY(m), x0 + s * R * (1.0 + wag) * L, y0 - R * 1.1 * L + tuckY(m)); }
      c.stroke();
      return;
    }
    const tuck = 0.5 * m.I;
    const L = part.length * k;
    const ex = cx + s * R * (L * 0.85 + wag), ey = cy + R * (-0.55 * L + 0.4 + tuck * L);
    const thick = part.style === "bushy" ? 0.32 : 0.08;
    c.fillStyle = col(p, part); c.beginPath();
    c.moveTo(cx + s * R * 0.55, cy + R * 0.72);
    c.bezierCurveTo(cx + s * R * (L * 1.05), cy + R * (0.95 + thick), cx + s * R * (L * 1.15 + wag), ey + R * 0.5, ex, ey);
    c.bezierCurveTo(cx + s * R * (L * 0.85 + wag - thick), ey + R * 0.55, cx + s * R * (0.9 + thick), cy + R * 0.6, cx + s * R * 0.62, cy + R * 0.42);
    c.closePath(); c.fill();
    if (part.tip) { c.fillStyle = col(p, part, part.tip, 0.9); c.beginPath(); c.arc(ex, ey, R * (part.style === "bushy" ? 0.15 : 0.07), 0, TAU); c.fill(); }
    if (part.rings) { // red panda: stripes across the bushy tail
      c.strokeStyle = col(p, part, part.rings, 0.85); c.lineWidth = R * 0.08;
      for (let i = 1; i <= 4; i++) {
        const u = i / 5, bx = cx + s * R * (0.6 + (L * 0.85 + wag - 0.6) * u), by = cy + R * (0.6 + (-0.55 * L - 0.2 + tuck * L) * u);
        c.beginPath(); c.moveTo(bx - R * 0.16, by - s * R * 0.1 * 0); c.lineTo(bx + R * 0.16 * s * 0.3, by + R * 0.18); c.stroke();
      }
    }
  };
  const tuckY = m => m.I * 0.5;

  P.mane = (c, g, part) => { // ruff of spikes around the lower face, behind the body
    const { cx, cy, R, k, m, p } = g;
    const n = part.count || 9;
    c.fillStyle = col(p, part, "dark", 0.8);
    c.beginPath();
    for (let i = 0; i <= n; i++) {
      const a = Math.PI * 0.1 + (i / n) * Math.PI * 0.8;
      const bump = 1 + part.length * k * (0.8 + 0.2 * Math.sin(m.t * 2 + i)) * (1 + 0.3 * m.A);
      const a0 = a - Math.PI * 0.8 / n / 2;
      c.lineTo(cx + Math.cos(a0) * R * 0.95, cy + Math.sin(a0) * R * 1.0);
      c.lineTo(cx + Math.cos(a) * R * bump, cy + Math.sin(a) * R * bump * 1.02);
    }
    c.lineTo(cx + Math.cos(Math.PI * 0.9) * R * 0.95, cy + Math.sin(Math.PI * 0.9) * R);
    c.closePath(); c.fill();
    for (const s of [-1, 1]) { // cheek tufts
      c.beginPath(); c.moveTo(cx + s * R * 0.85, cy - R * 0.1);
      c.lineTo(cx + s * R * (1.05 + part.length * k), cy + R * 0.1); c.lineTo(cx + s * R * 0.9, cy + R * 0.35); c.fill();
    }
  };

  P.muzzle = (c, g, part) => {
    const { cx, cy, R, p } = g;
    c.fillStyle = col(p, part, "light", 0.55); c.beginPath(); c.ellipse(cx, cy + R * 0.32, R * 0.36, R * 0.26, 0, 0, TAU); c.fill();
    c.fillStyle = col(p, part, "dark", 0.9); c.beginPath(); c.ellipse(cx, cy + R * 0.18, R * 0.09, R * 0.06, 0, 0, TAU); c.fill();
  };

  P.beard = (c, g, part) => {
    const { cx, cy, R, k, m, p } = g;
    const n = part.strands || 8, L = part.length * k;
    c.fillStyle = col(p, part, part.color || "light", 0.9);
    c.beginPath(); c.moveTo(cx - R * 0.62, cy + R * 0.12);
    for (let i = 0; i <= n; i++) {
      const u = i / n, x = cx + (u - 0.5) * R * 1.24;
      const sway = Math.sin(m.t * 1.1 + i * 0.7) * R * 0.03 * (m.calm ? 0.3 : 1);
      const depth = R * (0.35 + L * (1 - Math.pow(2 * u - 1, 2)));
      c.lineTo(x + sway, cy + R * 0.3 + depth);
      if (i < n) c.lineTo(x + R * 1.24 / n / 2, cy + R * 0.3 + depth * 0.82);
    }
    c.lineTo(cx + R * 0.62, cy + R * 0.12);
    c.quadraticCurveTo(cx, cy + R * 0.42, cx - R * 0.62, cy + R * 0.12); c.fill();
    c.fillStyle = col(p, part, "dark", 0.8); c.beginPath(); c.ellipse(cx, cy + R * 0.2, R * 0.12, R * 0.08, 0, 0, TAU); c.fill(); // nose
  };

  P.hat = (c, g, part) => { // the domovoi's soft felt cap, from stage 3
    const { cx, cy, R, k, m, p } = g;
    const H = R * part.height * k, flop = Math.sin(m.t * 0.8) * R * 0.06;
    c.fillStyle = hsla(p.h + 160, 45, 38, 0.95); c.beginPath();
    c.moveTo(cx - R * 0.75, cy - R * 0.7);
    c.quadraticCurveTo(cx - R * 0.2, cy - R * 0.7 - H, cx + R * 0.45 + flop, cy - R * 0.65 - H * 0.85);
    c.quadraticCurveTo(cx + R * 0.3, cy - R * 0.85, cx + R * 0.75, cy - R * 0.7);
    c.quadraticCurveTo(cx, cy - R * 0.98, cx - R * 0.75, cy - R * 0.7); c.fill();
    c.fillStyle = hsla(p.h + 160, 30, 85, 0.95); c.beginPath(); c.arc(cx + R * 0.45 + flop, cy - R * 0.65 - H * 0.85, R * 0.09, 0, TAU); c.fill();
  };

  P.wing = (c, g, part) => {
    const { cx, cy, R, k, m, p } = g;
    const spread = Math.max(0.45, 0.8 + 0.3 * m.A - 0.35 * m.I);
    const fast = m.mode === "consulting" ? 3 : 1;
    for (const s of [-1, 1]) {
      c.save();
      if (part.style === "butterfly") {
        const flap = spread * (0.8 + 0.2 * Math.sin(m.t * 1.8 * fast));
        c.translate(cx + s * R * 0.35, cy - R * 0.05); c.scale(flap, 1);
        for (const [ox, oy, rx, ry, rot] of [[1.15, -0.62, 0.95, 0.62, 0.55], [0.95, 0.42, 0.6, 0.45, -0.45]]) {
          c.save(); c.translate(s * R * ox * part.span * k * 0.8, R * oy * k); c.rotate(s * rot);
          const gr = c.createRadialGradient(0, 0, 0, 0, 0, R * rx * k);
          gr.addColorStop(0, hsla(p.h + 25, p.s, p.l + 18, 0.8)); gr.addColorStop(1, hsla(p.h, p.s, p.l - 10, 0.6));
          c.fillStyle = gr; c.beginPath(); c.ellipse(0, 0, R * rx * k, R * ry * k, 0, 0, TAU); c.fill();
          if (part.veins) {
            c.strokeStyle = hsla(p.h, p.s, p.l - 35, 0.5); c.lineWidth = 1;
            for (let i = -2; i <= 2; i++) { c.beginPath(); c.moveTo(-s * R * rx * 0.9 * k, 0); c.quadraticCurveTo(0, i * R * ry * 0.3 * k, s * R * rx * 0.95 * k, i * R * ry * 0.38 * k); c.stroke(); }
            c.lineWidth = 2; c.beginPath(); c.ellipse(0, 0, R * rx * k, R * ry * k, 0, 0, TAU); c.stroke();
          }
          if (part.spots) { c.fillStyle = hsla(p.h, 30, 94, 0.75); for (const f of [0.55, 0.75]) { c.beginPath(); c.arc(s * R * rx * f * k, -R * ry * 0.4 * k * f, R * 0.05 * k, 0, TAU); c.fill(); } }
          c.restore();
        }
      } else if (part.style === "bat") {
        c.translate(cx + s * R * 0.6, cy - R * 0.25); c.rotate(s * (0.16 * Math.sin(m.t * 1.5 * fast) * (m.calm ? 0.2 : 1) + (spread - 0.8) * 0.5));
        c.fillStyle = col(p, part, "dark"); c.beginPath(); c.moveTo(0, 0);
        const S = part.span * k; c.lineTo(s * R * S, -R * 0.75 * S);
        for (let i = 1; i <= 4; i++) { const x = s * R * S * (1 - i * 0.24), y = R * S * (-0.75 + i * 0.32); c.quadraticCurveTo(s * R * S * (1 - (i - 0.5) * 0.24), y - R * 0.05, x, y); }
        c.lineTo(s * R * 0.05, R * 0.55); c.closePath(); c.fill();
        c.strokeStyle = col(p, part, "body", 0.6); c.lineWidth = 1.2;
        for (let i = 1; i <= 3; i++) { c.beginPath(); c.moveTo(0, 0); c.lineTo(s * R * S * (1 - i * 0.24), R * S * (-0.75 + i * 0.32)); c.stroke(); }
      } else if (part.style === "feather") {
        const n = part.feathers || 6, S = part.span * k;
        c.translate(cx + s * R * 0.55, cy - R * 0.1); c.rotate(s * (-0.15 + 0.12 * Math.sin(m.t * 1.4 * fast) * (m.calm ? 0.2 : 1) - (spread - 0.8) * 0.6));
        for (let i = n - 1; i >= 0; i--) {
          const u = i / (n - 1), a = -0.9 + u * 1.5, L = R * S * (1.05 - u * 0.45);
          c.save(); c.rotate(s * a); c.fillStyle = part.colors ? col(p, part, part.colors[Math.min(part.colors.length - 1, Math.floor(u * part.colors.length))], 0.95) : part.dark ? (p.real ? hsla(p.coat.dark[0], p.coat.dark[1], p.coat.dark[2] + 6 + u * 8, 0.95) : hsla(p.h + 15, p.s + 10, p.l - 20 + u * 10, 0.95)) : p.real ? hsla(p.coat.base[0] + u * 6, p.coat.base[1], p.coat.base[2] - 14 + u * 14, 0.95) : hsla(p.h + u * 20, p.s, p.l - 14 + u * 14, 0.92);
          c.beginPath(); c.ellipse(s * L * 0.55, 0, L * 0.55, R * 0.13 * k, 0, 0, TAU); c.fill(); c.restore();
        }
      } else if (part.style === "blur") { // hummingbird wings: fast translucent smear
        const S = part.span * k;
        c.translate(cx + s * R * 0.7, cy - R * 0.2);
        for (let j = 0; j < 3; j++) {
          const a = s * (-0.6 + Math.sin(m.t * 30 * (m.calm ? 0.1 : 1) + j * 2) * 0.35);
          c.save(); c.rotate(a); c.fillStyle = hsla(p.h + 30, p.s, p.l + 20, 0.22);
          c.beginPath(); c.ellipse(s * R * S * 0.5, 0, R * S * 0.55, R * 0.16 * k, 0, 0, TAU); c.fill(); c.restore();
        }
      }
      c.restore();
    }
  };

  P.antennae = (c, g, part) => {
    const { cx, cy, R, k, m, p } = g;
    c.strokeStyle = col(p, part, "light"); c.lineWidth = Math.max(1.5, R * 0.025); c.fillStyle = col(p, part, "light");
    for (const s of [-1, 1]) {
      const L = part.length * k, sway = Math.sin(m.t * 1.7 + s) * 0.08;
      const x1 = cx + s * R * (0.3 + L * 0.5 + sway), y1 = cy - R * (0.95 + L);
      c.beginPath(); c.moveTo(cx + s * R * 0.18, cy - R * 0.93); c.quadraticCurveTo(cx + s * R * 0.25, cy - R * (1.0 + L * 0.8), x1, y1); c.stroke();
      c.beginPath(); c.arc(x1, y1, R * 0.055, 0, TAU); c.fill();
    }
  };

  P.crest = (c, g, part) => {
    const { cx, cy, R, k, m, p } = g;
    const n = part.count || 5, half = (n - 1) / 2;
    const lift = 0.7 + 0.5 * m.A - 0.35 * m.I;
    for (let i = 0; i < n; i++) {
      const u = i - half;
      if (part.style === "flame") {
        const fl = (0.75 + 0.25 * Math.sin(m.t * 7 + i * 1.7) + 0.1 * Math.sin(m.t * 13 + i)) * (m.calm ? 0.6 : 1);
        const x0 = cx + u * R * 0.19, y0 = cy - R * 0.86 + Math.abs(u) * R * 0.1, h = R * part.height * (1 - Math.abs(u) / (half + 1) * 0.6) * fl * k * lift;
        const gr = c.createLinearGradient(x0, y0, x0, y0 - h);
        gr.addColorStop(0, hsla(p.h, 95, 58, 0.95)); gr.addColorStop(0.6, hsla(p.h + 18, 100, 62, 0.8)); gr.addColorStop(1, hsla(50, 100, 78, 0.05));
        c.fillStyle = gr; c.beginPath(); c.moveTo(x0 - R * 0.12, y0);
        c.quadraticCurveTo(x0 - R * 0.11, y0 - h * 0.6, x0 + u * R * 0.06, y0 - h);
        c.quadraticCurveTo(x0 + R * 0.11, y0 - h * 0.6, x0 + R * 0.12, y0); c.fill();
      } else {
        const a = -Math.PI / 2 + u * (part.style === "plume" ? 0.22 : 0.3) + Math.sin(m.t * 1.3 + i) * 0.04;
        const L = R * part.height * k * lift * (1 - Math.abs(u) * 0.12);
        const x0 = cx + u * R * 0.08, y0 = cy - R * 0.9, x1 = x0 + Math.cos(a) * L, y1 = y0 + Math.sin(a) * L;
        c.strokeStyle = part.hue !== undefined ? col(p, part, "accent") : (part.dark ? col(p, part, "dark") : col(p, part, "light"));
        c.lineWidth = Math.max(1.5, R * (part.style === "plume" ? 0.03 : 0.05)); c.lineCap = "round";
        c.beginPath(); c.moveTo(x0, y0); c.quadraticCurveTo(x0 + u * R * 0.15, (y0 + y1) / 2, x1, y1); c.stroke();
        c.fillStyle = c.strokeStyle; c.beginPath(); c.ellipse(x1, y1, R * 0.06, R * 0.1, a + Math.PI / 2, 0, TAU); c.fill();
      }
    }
  };

  P.antlers = (c, g, part) => {
    const { cx, cy, R, k, p } = g;
    c.strokeStyle = col(p, part, "light"); c.lineWidth = Math.max(1.8, R * 0.04); c.lineCap = "round";
    for (const s of [-1, 1]) {
      const H = part.height * k, x0 = cx + s * R * 0.3, y0 = cy - R * 0.92;
      const tip = [x0 + s * R * 0.25 * H, y0 - R * 1.0 * H];
      c.beginPath(); c.moveTo(x0, y0); c.quadraticCurveTo(x0 + s * R * 0.32 * H, y0 - R * 0.5 * H, tip[0], tip[1]); c.stroke();
      for (let i = 1; i <= (part.tines || 3); i++) {
        const u = i / ((part.tines || 3) + 1), bx = x0 + s * R * 0.3 * H * u, by = y0 - R * H * u;
        const dir = i % 2 ? s : -s;
        c.beginPath(); c.moveTo(bx, by); c.quadraticCurveTo(bx + dir * R * 0.15 * H, by - R * 0.05, bx + dir * R * 0.3 * H, by - R * 0.22 * H); c.stroke();
      }
    }
  };

  P.wreath = (c, g, part) => { // ring of leaves or flowers across the brow
    const { cx, cy, R, k, m, p } = g;
    const n = part.count || 7;
    for (let i = 0; i < n; i++) {
      const a = -Math.PI / 2 + (i / (n - 1) - 0.5) * 2.0;
      const x = cx + Math.cos(a) * R * 0.95, y = cy + Math.sin(a) * R * 1.0;
      if (part.style === "berries") {
        c.fillStyle = hsla(110, 40, 32, 0.9); c.beginPath(); c.ellipse(x, y - R * 0.04, R * 0.05 * k, R * 0.12 * k, a + Math.PI / 2, 0, TAU); c.fill();
        for (const [dx, dy] of [[-0.05, 0.03], [0.05, 0.03], [0, -0.03], [0, 0.08]]) { c.fillStyle = hsla(part.hue || 8, 85, 50, 0.97); c.beginPath(); c.arc(x + dx * R * k, y + dy * R * k, R * 0.045 * k, 0, TAU); c.fill(); }
        continue;
      }
      if (part.style === "leaves") {
        c.save(); c.translate(x, y); c.rotate(a + Math.PI / 2 + Math.sin(m.t + i) * 0.1);
        c.fillStyle = hsla(p.h + (i % 2 ? 10 : -15), 55, 38 + (i % 3) * 6, 0.95);
        c.beginPath(); c.ellipse(0, -R * 0.12 * k, R * 0.08 * k, R * 0.17 * k, 0, 0, TAU); c.fill(); c.restore();
      } else {
        const hue = part.absHue !== undefined ? part.absHue + (i % 3) * 8 : p.h + (part.hue || 0) + (i % 3) * 25;
        for (let j = 0; j < 5; j++) {
          const b = j / 5 * TAU + m.t * 0.2;
          c.fillStyle = hsla(hue, part.sat || 70, part.lum || 78, 0.95); c.beginPath(); c.arc(x + Math.cos(b) * R * 0.065 * k, y + Math.sin(b) * R * 0.065 * k, R * 0.055 * k, 0, TAU); c.fill();
        }
        c.fillStyle = hsla(48, 90, 62, 1); c.beginPath(); c.arc(x, y, R * 0.04 * k, 0, TAU); c.fill();
      }
    }
  };

  P.fronds = (c, g, part) => { // axolotl gills
    const { cx, cy, R, k, m, p } = g;
    c.strokeStyle = hsla(p.h + (part.hue || 150), 70, 72, 0.92); c.lineCap = "round";
    const perk = 1 + 0.25 * m.A - 0.3 * m.I;
    for (const s of [-1, 1]) for (let i = 0; i < (part.count || 3); i++) {
      const sway = 0.08 * Math.sin(m.t * 1.4 + i + s) * (m.calm ? 0.3 : 1);
      const x0 = cx + s * R * 0.8, y0 = cy - R * (0.55 - i * 0.28);
      const x1 = x0 + s * R * (part.length + 0.1 * i) * k, y1 = y0 - R * (0.4 - i * 0.24 + sway) * k * perk;
      c.lineWidth = Math.max(2, R * 0.05); c.beginPath(); c.moveTo(x0, y0); c.quadraticCurveTo((x0 + x1) / 2, y1 + R * 0.1, x1, y1); c.stroke();
      c.lineWidth = Math.max(1, R * 0.022);
      for (let j = 1; j <= 5; j++) { const u = j / 6, px = x0 + (x1 - x0) * u, py = y0 + (y1 - y0) * u; c.beginPath(); c.moveTo(px, py); c.lineTo(px + s * R * 0.04, py - R * 0.11); c.stroke(); }
    }
  };

  P.hair = (c, g, part) => { // long flowing strands behind (river spirit)
    const { cx, cy, R, k, m, p } = g;
    const n = part.strands || 10;
    c.lineCap = "round";
    for (let i = 0; i < n; i++) {
      const u = i / (n - 1) - 0.5, a = -Math.PI / 2 + u * 2.6;
      const x0 = cx + Math.cos(a) * R * 0.8, y0 = cy + Math.sin(a) * R * 0.8;
      const L = R * part.length * k, wave = Math.sin(m.t * 1.2 + i * 0.6) * R * 0.12 * (m.calm ? 0.3 : 1);
      const x1 = cx + u * R * 2.4 + wave, y1 = cy + L * 0.9;
      c.strokeStyle = hsla(p.h + 20, p.s * 0.6, p.l + 18 - Math.abs(u) * 10, 0.75); c.lineWidth = Math.max(2, R * 0.07);
      c.beginPath(); c.moveTo(x0, y0); c.bezierCurveTo(x0 + u * R * 1.6, y0 + R * 0.2, x1 - wave, cy + L * 0.4, x1, y1); c.stroke();
    }
  };

  P.fangs = (c, g, part) => {
    const { cx, cy, R, p } = g;
    c.fillStyle = hsla(p.h, 10, 96, 0.95);
    for (const s of [-1, 1]) { c.beginPath(); c.moveTo(cx + s * R * 0.08, cy + R * 0.3); c.lineTo(cx + s * R * 0.16, cy + R * 0.3); c.lineTo(cx + s * R * 0.12, cy + R * 0.44); c.closePath(); c.fill(); }
  };

  P.beak = (c, g, part) => {
    const { cx, cy, R, k, m, p } = g;
    if (part.style === "hooked") {
      const L = R * part.length; c.fillStyle = col(p, part, part.color || [40, 15, 85]);
      c.beginPath(); c.moveTo(cx - R * 0.14, cy + R * 0.02); c.quadraticCurveTo(cx + R * 0.05, cy - R * 0.08, cx + R * 0.16, cy + R * 0.05);
      c.quadraticCurveTo(cx + R * 0.12, cy + L * 0.8, cx, cy + L); c.quadraticCurveTo(cx - R * 0.05, cy + L * 0.5, cx - R * 0.14, cy + R * 0.02); c.fill();
      return;
    }
    const L = R * part.length * (0.8 + 0.2 * k), w = R * (part.slim ? 0.07 : part.style === "stubby" ? 0.12 : 0.14);
    const open = m.mode === "speaking" ? Math.abs(Math.sin(m.t * 9)) * R * 0.05 : 0;
    c.fillStyle = part.color ? col(p, part, part.color, 0.97) : hsla(45, 70, part.slim ? 42 : 30, 0.97);
    c.beginPath(); c.moveTo(cx - w, cy + R * 0.12); c.lineTo(cx, cy + R * 0.12 + L - open * 0.2); c.lineTo(cx + w, cy + R * 0.12); c.quadraticCurveTo(cx, cy + R * 0.02, cx - w, cy + R * 0.12); c.fill();
    if (open) { c.fillStyle = hsla(45, 60, 24, 0.97); c.beginPath(); c.moveTo(cx - w * 0.8, cy + R * 0.14 + open); c.lineTo(cx, cy + R * 0.14 + L * 0.7 + open); c.lineTo(cx + w * 0.8, cy + R * 0.14 + open); c.fill(); }
  };

  P.dome = (c, g, part) => { // turtle shell behind the body
    const { cx, cy, R, k, p } = g;
    const rx = R * (1.15 + 0.35 * k), ry = R * (1.05 + 0.3 * k);
    c.save(); c.translate(cx, cy - R * 0.05);
    c.fillStyle = hsla(p.h + 30, p.s * 0.7, p.l - 18, 0.9); c.beginPath(); c.ellipse(0, 0, rx, ry, 0, Math.PI, TAU); c.closePath(); c.fill();
    c.strokeStyle = hsla(p.h + 30, p.s * 0.6, p.l + 15, 0.8); c.lineWidth = 1.4;
    const n = part.plates || 6;
    for (let i = 1; i < n; i++) { const a = Math.PI + i / n * Math.PI; c.beginPath(); c.moveTo(Math.cos(a) * R * 0.9, Math.sin(a) * R * 0.9); c.lineTo(Math.cos(a) * rx, Math.sin(a) * ry); c.stroke(); }
    c.beginPath(); c.ellipse(0, 0, (rx + R * 0.9) / 2, (ry + R * 0.9) / 2, 0, Math.PI, TAU); c.stroke();
    c.restore();
  };

  P.flippers = (c, g, part) => {
    const { cx, cy, R, k, m, p } = g;
    for (const s of [-1, 1]) {
      const row = Math.sin(m.t * 1.3 + (s > 0 ? 0 : Math.PI)) * 0.25 * (m.calm ? 0.3 : 1);
      c.save(); c.translate(cx + s * R * 0.8, cy + R * 0.35); c.rotate(s * (0.5 + row));
      c.fillStyle = col(p, part); c.beginPath(); c.ellipse(s * R * part.span * 0.5 * k, 0, R * part.span * 0.55 * k, R * 0.17, 0, 0, TAU); c.fill();
      c.restore();
    }
  };

  P.whiskers = (c, g, part) => {
    const { cx, cy, R, m, p } = g;
    c.strokeStyle = col(p, part, part.color || "light", 0.7); c.lineWidth = Math.max(1, R * 0.015);
    for (const s of [-1, 1]) for (let i = -1; i <= 1; i++) {
      const tw = Math.sin(m.t * 2 + i) * R * 0.02;
      c.beginPath(); c.moveTo(cx + s * R * 0.2, cy + R * 0.24 + i * R * 0.05);
      c.quadraticCurveTo(cx + s * R * 0.55, cy + R * 0.2 + i * R * 0.09 + tw, cx + s * R * 0.85, cy + R * 0.18 + i * R * 0.15 + tw); c.stroke();
    }
  };

  P.nose = (c, g, part) => {
    const { cx, cy, R, m, p } = g;
    if (part.muzzle) { c.fillStyle = col(p, part, part.muzzle, 0.9); c.beginPath(); c.ellipse(cx, cy + R * 0.3, R * 0.3, R * 0.2, 0, 0, TAU); c.fill(); }
    c.fillStyle = col(p, part, "nose", 0.97); c.beginPath();
    if (part.style === "cat") { c.moveTo(cx - R * 0.07, cy + R * 0.17); c.lineTo(cx + R * 0.07, cy + R * 0.17); c.lineTo(cx, cy + R * 0.25); c.closePath(); }
    else c.ellipse(cx, cy + R * 0.2, R * (part.style === "big" ? 0.12 : 0.08), R * (part.style === "big" ? 0.08 : 0.055), 0, 0, TAU);
    c.fill();
    c.strokeStyle = col(p, part, "nose", 0.8); c.lineWidth = Math.max(1, R * 0.02); c.lineCap = "round"; c.beginPath();
    const mouthY = cy + R * 0.32, smile = R * (0.02 + 0.06 * m.A - 0.04 * m.I);
    c.moveTo(cx, cy + R * 0.25); c.lineTo(cx, mouthY);
    c.moveTo(cx - R * 0.1, mouthY - smile * 0.2); c.quadraticCurveTo(cx - R * 0.05, mouthY + smile, cx, mouthY);
    c.quadraticCurveTo(cx + R * 0.05, mouthY + smile, cx + R * 0.1, mouthY - smile * 0.2); c.stroke();
    if (part.tongue && m.A > 0.55 && !m.calm) { c.fillStyle = hsla(350, 70, 68, 0.95); c.beginPath(); c.ellipse(cx, mouthY + R * 0.08, R * 0.06, R * 0.09 * (m.A - 0.4) * 2, 0, 0, TAU); c.fill(); }
  };

  P.paws = (c, g, part) => {
    const { cx, cy, R, m, p } = g;
    const wave = m.mode === "speaking" ? Math.sin(m.t * 4) * R * 0.04 : 0;
    for (const s of [-1, 1]) {
      c.fillStyle = col(p, part, part.color || "body", 0.97);
      c.beginPath(); c.ellipse(cx + s * R * 0.42, cy + R * 0.92 - (s > 0 ? wave : 0), R * 0.2, R * 0.14, 0, 0, TAU); c.fill();
      if (part.beans) { c.fillStyle = hsla(350, 45, 75, 0.8); c.beginPath(); c.arc(cx + s * R * 0.42, cy + R * 0.95 - (s > 0 ? wave : 0), R * 0.05, 0, TAU); c.fill(); }
    }
  };

  P.tentacles = (c, g, part) => {
    const { cx, cy, R, k, m, p } = g;
    const n = part.count || 6; c.lineCap = "round";
    for (let i = 0; i < n; i++) {
      const u = i / (n - 1) - 0.5, x0 = cx + u * R * 1.3, y0 = cy + R * 0.7;
      const L = R * part.length * k, curl = (u > 0 ? 1 : -1) * (0.6 + 0.4 * Math.sin(m.t * 1.3 + i)) * (m.calm ? 0.4 : 1);
      const x1 = x0 + u * R * 1.4 + curl * R * 0.35, y1 = y0 + L * (0.75 - 0.3 * m.I);
      c.strokeStyle = col(p, part, "body", 0.95); c.lineWidth = R * 0.17;
      c.beginPath(); c.moveTo(x0, y0); c.bezierCurveTo(x0 + u * R * 0.3, y0 + L * 0.4, x1 - curl * R * 0.5, y1, x1, y1 - R * 0.12); c.stroke();
      c.fillStyle = col(p, part, "light", 0.7);
      for (let j = 1; j <= 3; j++) { const v = j / 4; c.beginPath(); c.arc(x0 + (x1 - x0) * v * 0.9, y0 + (y1 - y0) * v * 0.9, R * 0.03, 0, TAU); c.fill(); }
    }
  };

  P.tendrils = (c, g, part) => { // jellyfish: long glowing threads
    const { cx, cy, R, k, m, p } = g;
    const n = part.count || 7; c.lineCap = "round";
    for (let i = 0; i < n; i++) {
      const u = i / (n - 1) - 0.5, x0 = cx + u * R * 1.4, y0 = cy + R * 0.5, L = R * part.length * k * (1 - Math.abs(u) * 0.4);
      c.strokeStyle = hsla(p.coat.light[0] + i * 6, 70, 80, 0.55); c.lineWidth = Math.max(1.2, R * (i % 2 ? 0.025 : 0.05));
      c.beginPath(); c.moveTo(x0, y0);
      for (let j = 1; j <= 8; j++) { const v = j / 8; c.lineTo(x0 + Math.sin(m.t * 1.5 + i + v * 5) * R * 0.12 * v, y0 + L * v); }
      c.stroke();
    }
  };

  P.fin = (c, g, part) => { // dorsal fin / fish fins on the sides
    const { cx, cy, R, k, m, p } = g;
    const fl = Math.sin(m.t * 2.5) * 0.15;
    for (const s of [-1, 1]) {
      c.save(); c.translate(cx + s * R * 0.9, cy + R * 0.15); c.rotate(s * (0.3 + fl));
      c.fillStyle = col(p, part, part.color || "accent", 0.85); c.beginPath(); c.moveTo(0, -R * 0.2);
      c.quadraticCurveTo(s * R * 0.6 * k, -R * 0.4, s * R * 0.55 * k, R * 0.25); c.quadraticCurveTo(s * R * 0.2, R * 0.1, 0, R * 0.2); c.fill(); c.restore();
    }
  };

  P.charm = (c, g, part) => { // birthplace talisman hanging on the chest
    const { cx, cy, R, m, p } = g;
    const sw = Math.sin(m.t * 1.4) * R * 0.03, x = cx + sw, y = cy + R * 0.72;
    c.strokeStyle = hsla(40, 40, 70, 0.8); c.lineWidth = 1.2; c.beginPath(); c.moveTo(cx - R * 0.45, cy + R * 0.48); c.quadraticCurveTo(cx, cy + R * 0.66, cx + R * 0.45, cy + R * 0.48); c.stroke();
    c.fillStyle = hsla(part.hue, part.sat || 80, part.lum || 58, 0.97);
    if (part.shape === "flower") for (let j = 0; j < 6; j++) { const b = j / 6 * TAU; c.beginPath(); c.arc(x + Math.cos(b) * R * 0.06, y + Math.sin(b) * R * 0.06, R * 0.055, 0, TAU); c.fill(); }
    else if (part.shape === "berries") for (const [dx, dy] of [[-0.05, 0], [0.05, 0], [0, -0.06], [0, 0.06]]) { c.beginPath(); c.arc(x + dx * R, y + dy * R, R * 0.045, 0, TAU); c.fill(); }
    else if (part.shape === "star") { c.beginPath(); for (let j = 0; j <= 10; j++) { const b = -Math.PI / 2 + j / 10 * TAU, rr = j % 2 ? R * 0.045 : R * 0.1; c.lineTo(x + Math.cos(b) * rr, y + Math.sin(b) * rr); } c.fill(); }
    else { c.beginPath(); c.arc(x, y, R * 0.08, 0, TAU); c.fill(); }
    c.fillStyle = hsla(48, 90, 70, 1); c.beginPath(); c.arc(x, y, R * 0.03, 0, TAU); c.fill();
  };

  // ---------- markings (drawn on the body) ----------
  function blobClip(c, g, spec) { blobPath(c, g.cx, g.cy, g.R, g.m.t, spec, g.m.calm); c.clip(); }
  function markings(c, g, spec) {
    const { cx, cy, R, p } = g;
    for (const mk of spec.markings || []) {
      if (mk.style === "rosettes") {
        c.strokeStyle = hsla(p.h, p.s, p.l - 32, 0.5); c.lineWidth = Math.max(1.2, R * 0.028);
        const pts = [[-0.55, 0.35, 0.09], [0.6, 0.3, 0.08], [-0.3, 0.65, 0.07], [0.32, 0.62, 0.09], [0.05, 0.82, 0.06], [-0.74, -0.05, 0.06], [0.76, -0.08, 0.06], [-0.5, -0.55, 0.05], [0.52, -0.58, 0.05]];
        for (const [x, y, r] of pts.slice(0, mk.count || 7)) { c.beginPath(); c.arc(cx + x * R, cy + y * R, r * R, 0.3, TAU - 0.4); c.stroke(); }
      } else if (mk.style === "bark") {
        c.strokeStyle = hsla(p.h - 60, 35, p.l - 30, 0.35); c.lineWidth = Math.max(1, R * 0.02);
        for (let i = 0; i < (mk.count || 5); i++) { const x = cx + (i / 4 - 0.5) * R * 1.3; c.beginPath(); c.moveTo(x, cy + R * 0.35); c.quadraticCurveTo(x + R * 0.06, cy + R * 0.6, x - R * 0.02, cy + R * 0.92); c.stroke(); }
      } else if (mk.style === "eyepatch") { // panda
        c.fillStyle = col(p, null, "dark", 0.95);
        for (const s of [-1, 1]) { c.beginPath(); c.ellipse(cx + s * R * 0.36, cy - R * 0.06, R * 0.24, R * 0.3, s * -0.5, 0, TAU); c.fill(); }
      } else if (mk.style === "mask") { // red panda: white cheeks, brow dots, tear lines
        c.fillStyle = col(p, null, "light", 0.95);
        c.beginPath(); c.ellipse(cx, cy + R * 0.3, R * 0.5, R * 0.3, 0, 0, TAU); c.fill();
        for (const s of [-1, 1]) {
          c.beginPath(); c.ellipse(cx + s * R * 0.36, cy - R * 0.4, R * 0.12, R * 0.08, 0, 0, TAU); c.fill();
          c.fillStyle = col(p, null, "dark", 0.75); c.beginPath(); c.ellipse(cx + s * R * 0.4, cy + R * 0.12, R * 0.05, R * 0.2, s * -0.2, 0, TAU); c.fill();
          c.fillStyle = col(p, null, "light", 0.95);
        }
      } else if (mk.style === "tabby") {
        c.strokeStyle = col(p, null, "dark", 0.55); c.lineWidth = R * 0.06; c.lineCap = "round";
        for (const dx of [-0.18, 0, 0.18]) { c.beginPath(); c.moveTo(cx + dx * R, cy - R * 0.98); c.lineTo(cx + dx * R * 0.8, cy - R * 0.6); c.stroke(); }
        for (const s of [-1, 1]) for (const dy of [0.1, 0.35, 0.6]) { c.beginPath(); c.moveTo(cx + s * R * 0.98, cy + dy * R); c.quadraticCurveTo(cx + s * R * 0.8, cy + (dy + 0.05) * R, cx + s * R * 0.68, cy + (dy + 0.02) * R); c.stroke(); }
      } else if (mk.style === "calico") {
        c.save(); blobClip(c, g, spec);
        c.fillStyle = col(p, null, "accent", 0.9); c.beginPath(); c.ellipse(cx - R * 0.55, cy - R * 0.5, R * 0.55, R * 0.45, 0.4, 0, TAU); c.fill();
        c.fillStyle = col(p, null, "dark", 0.92); c.beginPath(); c.ellipse(cx + R * 0.7, cy + R * 0.45, R * 0.45, R * 0.4, -0.3, 0, TAU); c.fill();
        c.restore();
      } else if (mk.style === "points") { // siamese / husky mask: darker centre of the face
        const gr = c.createRadialGradient(cx, cy + R * 0.2, R * 0.05, cx, cy + R * 0.2, R * 0.6);
        gr.addColorStop(0, col(p, null, mk.color || "dark", 0.75)); gr.addColorStop(1, col(p, null, mk.color || "dark", 0));
        c.fillStyle = gr; c.beginPath(); c.arc(cx, cy + R * 0.2, R * 0.6, 0, TAU); c.fill();
      } else if (mk.style === "spots") {
        c.fillStyle = col(p, null, mk.color || "dark", 0.5);
        for (const [x, y, r] of [[-0.6, 0.4, 0.06], [0.55, 0.5, 0.05], [-0.2, 0.75, 0.04], [0.25, -0.65, 0.05], [-0.45, -0.6, 0.04], [0.75, 0.05, 0.05], [0.1, 0.85, 0.035]]) { c.beginPath(); c.arc(cx + x * R, cy + y * R, r * R, 0, TAU); c.fill(); }
      } else if (mk.style === "belly" || mk.style === "breast") {
        c.save(); blobClip(c, g, spec);
        c.fillStyle = col(p, null, mk.color || "light", 0.9); c.beginPath(); c.ellipse(cx, cy + R * (mk.style === "breast" ? 0.55 : 0.75), R * (mk.style === "breast" ? 0.8 : 0.6), R * 0.55, 0, 0, TAU); c.fill();
        c.restore();
      } else if (mk.style === "cap") { // bullfinch black cap
        c.save(); blobClip(c, g, spec);
        c.fillStyle = col(p, null, mk.color || "dark", 0.96); c.beginPath(); c.ellipse(cx, cy - R * 0.95, R * 1.0, R * 0.5, 0, 0, TAU); c.fill();
        c.restore();
      } else if (mk.style === "disc") { // owl facial disc
        c.fillStyle = col(p, null, "light", 0.85);
        for (const s of [-1, 1]) { c.beginPath(); c.arc(cx + s * R * 0.32, cy - R * 0.1, R * 0.36, 0, TAU); c.fill(); }
        c.strokeStyle = col(p, null, "dark", 0.5); c.lineWidth = R * 0.03; c.beginPath(); c.moveTo(cx, cy - R * 0.5); c.lineTo(cx, cy + R * 0.25); c.stroke();
      } else if (mk.style === "blush") {
        c.fillStyle = hsla(345, 70, 72, 0.35);
        for (const s of [-1, 1]) { c.beginPath(); c.ellipse(cx + s * R * 0.52, cy + R * 0.12, R * 0.13, R * 0.08, 0, 0, TAU); c.fill(); }
      }
    }
  }

  // ---------- body, eyes, aura ----------
  function blobPath(c, cx, cy, R, t, spec, calm) {
    const wob = calm ? 0.4 : 1, b = spec.body || {};
    c.beginPath();
    for (let i = 0; i <= 72; i++) {
      const a = i / 72 * TAU;
      let w = 0; for (const [f, amp, sp] of b.wobble || [[3, 0.035, 0.9], [5, 0.022, -1.4], [8, 0.012, 2.1]]) w += amp * Math.sin(f * a + t * sp);
      const sa = Math.sin(a), ca = Math.cos(a);
      const dTop = Math.atan2(Math.sin(a + Math.PI / 2), Math.cos(a + Math.PI / 2));
      const cheek = (b.cheeks || 0) * (Math.exp(-((a - 0.62) ** 2) / 0.06) + Math.exp(-((a - (Math.PI - 0.62)) ** 2) / 0.06));
      const r = R * (1 + wob * w + (b.top || 0) * Math.exp(-(dTop * dTop) / 0.06) + cheek);
      let x = cx + ca * r * (b.w || 0.96) * (1 + (b.bottom || 0) * Math.max(0, sa));
      let y = cy + sa * r * (b.squash || 1.07) + (sa > 0 ? sa * R * 0.06 : 0);
      if (b.bell && sa > 0.25) y = Math.min(y, cy + R * 0.5 + Math.sin(a * 14 + t * 2) * R * 0.035);
      i ? c.lineTo(x, y) : c.moveTo(x, y);
    }
    c.closePath();
  }

  function eyes(c, g, spec, st) {
    const { cx, cy, R, p } = g, e = spec.eyes;
    let open = Math.max(0.25, Math.min(1.15, 0.9 + 0.25 * st.A - 0.35 * st.I));
    if (st.calm) open = 0.7;
    if (st.mode === "listening") open = Math.min(1.2, open + 0.12);
    open *= (1 - (e.lid || 0)) * (1 - (st.blink || 0));
    const ew = R * e.size, ratio = e.shape === "narrow" ? 0.72 : e.shape === "almond" ? 0.9 : 1.12;
    const eh = ew * ratio * open;
    for (const s of [-1, 1]) {
      const ex = cx + s * R * e.spacing, ey = cy + R * e.y;
      const silver = e.special === "silverRight" && s === 1;
      const tilt = e.shape === "almond" ? s * -0.12 : 0;
      c.save(); c.translate(ex, ey); c.rotate(tilt);
      c.fillStyle = silver ? "hsla(210,15%,90%,0.98)" : hsla(p.h, 55, 94, 0.97);
      c.beginPath(); c.ellipse(0, 0, ew, Math.max(0.6, eh), 0, 0, TAU); c.fill();
      if (eh > 2) {
        c.save(); c.beginPath(); c.ellipse(0, 0, ew, eh, 0, 0, TAU); c.clip();
        const px = st.look.x * ew * 0.45, py = st.look.y * eh * 0.4, pr = ew * (0.55 + 0.15 * st.A - 0.1 * st.I);
        if (e.iris) { c.fillStyle = hsla(e.iris[0], e.iris[1], e.iris[2], 1); c.beginPath(); c.arc(px, py, ew * 0.82, 0, TAU); c.fill(); }
        c.fillStyle = silver ? "hsl(215,20%,40%)" : hsla(p.h, 45, 12, 1); c.beginPath();
        if (e.pupil === "slit") c.ellipse(px, py, pr * (0.3 + 0.6 * st.A), pr * 1.15, 0, 0, TAU); else c.arc(px, py, e.iris ? pr * 0.7 : pr, 0, TAU);
        c.fill();
        c.fillStyle = "rgba(255,255,255,0.9)"; c.beginPath(); c.arc(px - pr * 0.35, py - pr * 0.4, pr * 0.28, 0, TAU); c.fill();
        c.restore();
      }
      c.restore();
      const by = ey - ew * 1.35 - R * 0.05 * st.A, inner = by - R * 0.13 * st.I, outer = by + R * 0.04 * st.I;
      c.strokeStyle = hsla(p.h, p.s * 0.5, 92, 0.55); c.lineWidth = Math.max(1.5, R * 0.03); c.lineCap = "round";
      c.beginPath(); c.moveTo(ex - s * ew * 0.9, inner); c.quadraticCurveTo(ex, Math.min(inner, outer) - R * 0.03, ex + s * ew, outer); c.stroke();
    }
  }

  function glow(c, cx, cy, R, p, st, t) {
    const pulse = st.mode === "consulting" ? 0.25 * (1 + Math.sin(t * 6)) : 0;
    const gr = c.createRadialGradient(cx, cy, R * 0.3, cx, cy, R * (2.3 + 0.4 * st.A));
    gr.addColorStop(0, hsla(p.h, p.s, p.l + 10, (0.28 + 0.25 * st.A + pulse) * (st.calm ? 0.5 : 1)));
    gr.addColorStop(1, hsla(p.h, p.s, p.l, 0));
    c.fillStyle = gr; c.beginPath(); c.arc(cx, cy, R * 3, 0, TAU); c.fill();
  }

  function sigil(c, cx, cy, R, p, st, t, scale, dir, alpha) {
    const sg = st.sigil, rr = R * scale, rot = sg.rot + dir * t * (st.mode === "consulting" ? 1.6 : 0.08);
    c.save(); c.translate(cx, cy); c.rotate(rot); c.strokeStyle = hsla(p.h, p.s, 78, alpha); c.lineWidth = 1.2;
    c.beginPath(); c.arc(0, 0, rr, 0, TAU); c.stroke();
    c.globalAlpha = 0.6; c.beginPath(); c.arc(0, 0, rr * 0.93, 0, TAU); c.stroke();
    c.globalAlpha = 0.55; c.beginPath();
    for (let i = 0; i <= sg.n; i++) { const a = -Math.PI / 2 + (i * sg.k % sg.n) / sg.n * TAU; const x = Math.cos(a) * rr * 0.93, y = Math.sin(a) * rr * 0.93; i ? c.lineTo(x, y) : c.moveTo(x, y); }
    c.stroke(); c.globalAlpha = 1;
    for (const tk of sg.ticks) {
      const co = Math.cos(tk.a), si = Math.sin(tk.a), r0 = rr * 1.02, r1 = rr * (1.02 + tk.len);
      c.beginPath(); c.moveTo(co * r0, si * r0); c.lineTo(co * r1, si * r1);
      if (tk.kind === 1) { const mm = (r0 + r1) / 2; c.moveTo(co * mm - si * rr * 0.03, si * mm + co * rr * 0.03); c.lineTo(co * mm + si * rr * 0.03, si * mm - co * rr * 0.03); }
      if (tk.kind === 2) { c.moveTo(co * r1 + rr * 0.012, si * r1); c.arc(co * r1, si * r1, rr * 0.012, 0, TAU); }
      c.stroke();
    }
    c.restore();
  }

  function motes(c, cx, cy, R, p, st, dt) {
    if (st.calm || st.stage < 2 || st.reduce) { st.motes.length = 0; return; }
    const want = Math.round(10 + 40 * st.A + 8 * st.stage);
    while (st.motes.length < want) st.motes.push({ a: Math.random() * TAU, r: R * (1.1 + Math.random() * 1.2), y: 0, v: 0.2 + Math.random() * 0.6, life: Math.random(), size: 0.6 + Math.random() * 1.8 });
    if (st.motes.length > want) st.motes.length = want;
    c.save(); c.globalCompositeOperation = "lighter";
    for (const mo of st.motes) {
      if (st.mode === "consulting") { mo.r = Math.max(R * 0.2, mo.r - dt * R * 1.6); mo.a += dt * 2.5; if (mo.r <= R * 0.2) mo.r = R * 2.3; }
      else { mo.a += dt * 0.15 * mo.v; mo.y -= dt * R * 0.25 * mo.v * (0.3 + st.A - 0.6 * st.I); }
      mo.life += dt * 0.25; if (mo.life > 1 || Math.abs(mo.y) > R * 2) { mo.life = 0; mo.y = 0; mo.r = R * (1.1 + Math.random() * 1.2); }
      const a = Math.sin(mo.life * Math.PI) * (0.5 + 0.4 * st.A);
      c.fillStyle = hsla(p.h, p.s, 75, a); c.beginPath(); c.arc(cx + Math.cos(mo.a) * mo.r, cy + Math.sin(mo.a) * mo.r * 0.8 + mo.y, mo.size, 0, TAU); c.fill();
    }
    c.restore();
  }

  function extras(c, cx, cy, R, p, st, t) {
    if (st.stage >= 4 && !st.calm) for (let i = 0; i < 7; i++) { // crown of motes
      const a = t * 0.6 + i / 7 * TAU, front = Math.sin(a) > 0;
      c.fillStyle = hsla(p.h, p.s * 0.6, 88, front ? 0.95 : 0.4);
      c.beginPath(); c.arc(cx + Math.cos(a) * R * 0.75, cy - R * 1.45 + Math.sin(a) * R * 0.16, R * (front ? 0.045 : 0.03), 0, TAU); c.fill();
    }
    if (st.mode === "speaking" && !st.calm) for (let i = 0; i < 3; i++) {
      const ph = (t * 0.9 + i / 3) % 1;
      c.strokeStyle = hsla(p.h, p.s, 80, 0.35 * (1 - ph)); c.lineWidth = 1.5;
      c.beginPath(); c.ellipse(cx, cy, R * (1.1 + ph * 0.9), R * (1.15 + ph * 0.95), 0, 0, TAU); c.stroke();
    }
  }
  function halo(c, cx, cy, R, p, st, t) {
    if (st.stage < 5 || st.calm) return;
    c.save(); c.translate(cx, cy); c.rotate(t * 0.05);
    for (let i = 0; i < 24; i++) { c.rotate(TAU / 24); c.strokeStyle = hsla(p.h, p.s * 0.5, 85, 0.12 + 0.08 * Math.sin(t * 2 + i)); c.lineWidth = 1; c.beginPath(); c.moveTo(0, -R * 2.0); c.lineTo(0, -R * 2.35); c.stroke(); }
    c.restore();
  }

  // ---------- entry point ----------
  function render(c, spec, st, t, dt, box) {
    const p = palette(spec, st);
    const k = 0.55 + 0.45 * Math.min(1, (st.stage - 1) / 2);
    const R = box.size * 0.16 * (0.9 + 0.05 * st.stage) * (1 - 0.12 * st.I);
    const breath = 1 + (st.calm ? 0.015 : 0.03) * Math.sin(t * (st.calm ? 0.9 : 1.6));
    const bounce = st.calm ? 0 : Math.sin(t * 2.2) * R * 0.06 * st.A;
    const cx = box.cx, cy = box.cy + bounce + R * 0.12 * st.I;
    const m = { A: st.A, I: st.I, calm: st.calm, mode: st.mode, t: t * (spec.tempo || 1) };
    const g = { cx, cy, R: R * breath, k, m, p };

    glow(c, cx, cy, R, p, st, t);
    halo(c, cx, cy, R, p, st, t);
    sigil(c, cx, cy, R, p, st, t, 1.85, 1, Math.min(0.95, st.calm ? 0.12 : (0.18 + 0.12 * st.stage) * (st.mode === "consulting" ? 1.6 : 1)));
    if (st.stage >= 5 && !st.calm) sigil(c, cx, cy, R, p, st, t, 2.15, -1, 0.35);
    motes(c, cx, cy, R, p, st, dt);

    c.save();
    if (st.mode === "listening") { c.translate(cx, cy); c.rotate(0.05); c.translate(-cx, -cy); }
    const visible = part => st.stage >= (part.minStage || 2);
    for (const part of spec.parts) if (part.layer === "back" && visible(part) && P[part.type]) P[part.type](c, g, part);
    blobPath(c, cx, cy, g.R, m.t, spec, st.calm);
    const gr = c.createRadialGradient(cx - R * 0.15, cy - R * 0.35, R * 0.1, cx, cy, R * 1.1);
    const B = p.coat.base, alpha = spec.body && spec.body.translucent ? 0.55 : 1;
    gr.addColorStop(0, hsla(B[0], B[1] * 0.6, B[2] + (p.real ? 18 : 32), 0.97 * alpha)); gr.addColorStop(0.55, hsla(B[0], B[1], B[2], 0.94 * alpha)); gr.addColorStop(1, hsla(B[0], B[1], B[2] - (p.real ? 18 : 26), 0.9 * alpha));
    c.fillStyle = gr; c.fill();
    markings(c, g, spec);
    for (const part of spec.parts) if (part.layer === "front" && visible(part) && P[part.type]) P[part.type](c, g, part);
    eyes(c, g, spec, st);
    for (const part of spec.parts) if (part.layer === "top" && visible(part) && P[part.type]) P[part.type](c, g, part);
    c.restore();
    extras(c, cx, cy, R, p, st, t);
    return { R, p, cx, cy };
  }

  root.DaemonRender = { render, makeSigil, palette, hsla, primitives: Object.keys(P) };
})(typeof window !== "undefined" ? window : globalThis);
