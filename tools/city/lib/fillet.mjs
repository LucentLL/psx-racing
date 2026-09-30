// fillet.mjs - WP-11 (plan amendment A4, "WP-11 (amended)"): EVERY 2-ARM
// NODE AND EVERY FREE VERTEX IS FILLETED. The owner, 2026-09-28: "Nor should
// any sharp angles of road or road lines."
//
// Runs inside lineClean (lib/lineclean.mjs) after PARA and before the tagged
// nodes are projected, on STRANDS: edges joined through every 2-arm node (a
// way boundary), so a corner AT a 2-arm node is rounded like any other and
// the node moves onto its arc - after this a 2-arm node is always a mitred
// continuation (the builder's bend fans go). Junction nodes (3+ arms) and
// dead ends are FIXED, and the segment touching a fixed node keeps its
// direction there (a fillet takes at most its far part, leaving
// max(1 m, a quarter) straight), so every arm's OutDir - ComputeTrims'
// through / branch / fan decisions - is unchanged.
//
// At each interior vertex turning D:
//   * the wanted radius is R = max(R_floor, R_E): R_E = Emax / (sec(D/2) - 1)
//     keeps the arc within the class Emax of the mapped corner, R_floor =
//     max(R_min(class), half width + 3 m) is the class minimum (the gate's B3
//     table) and keeps the inner edge from folding;
//   * each segment is shared between its two corners in proportion to the
//     tangent each wants (all of it at a dead straight neighbour), so on a
//     digitised curve every arc runs from segment middle to segment middle
//     and the line is one smooth arc spline;
//   * a corner whose share cannot reach R_floor is fitted TOGETHER with its
//     neighbour across the short segment (same hand): the two become one
//     corner where their outer tangents meet, and the shares are redone;
//     what still cannot reach R_floor (an S-bend on a short tangent, a
//     single-vertex hairpin) keeps the largest radius that fits and is
//     listed (stats.tight);
//   * a departure past the class Emax (R_floor wins at a sharp corner) is
//     listed (stats.overEmax), not hidden.
// Arcs are densified by SAGITTA, not by angle: chord <= sqrt(8 eps R^2 /
// (R + hw)) with eps = SmoothRules.DensifyEpsM (2 cm on the outer drawn
// edge); past ChordCapM (10 m) the gate's lone-vertex facet, min(c, 10 m) *
// turn / 8 <= eps, sets it (a gentle motorway arc keeps chords of R / 62).
const DEG = Math.PI / 180;
const dist = (p, q) => Math.hypot(p[0] - q[0], p[1] - q[1]);

/// Emax, the largest departure from a mapped corner (plan WP-11): street
/// 0.75 m, arterial 1.0, trunk 1.25, motorway 1.5, ramp 1.25.
export const emaxOf = w => w.link ? 1.25 : [0.75, 0.75, 1.0, 1.0, 1.25, 1.5][w.rank];

/// THE MITRED JUNCTIONS: nodes of 3+ arms the builder draws as a mitred
/// THROUGH pair with every other arm clipped beside it (a ramp's split or
/// merge, a fork) - CityMeshes.ComputeTrims' decision, replicated: the most
/// opposite pair (links only when no other pair exists) past ThroughCos, and
/// every other arm within BranchCos of an arm it clips against. The through
/// road runs on across such a node, so its corner there is filleted like a
/// 2-arm node's; the node moves onto the arc and the clipped arms follow it.
/// hwOf(e): the builder's half width (the profile's); returns Map node ->
/// [through edge, through edge].
export function mitredThrough(edges, nodes, hwOf) {
  const ThroughCos = -0.85, BranchCos = 0.5;
  const arms = Array.from({ length: nodes.length }, () => []);
  for (const e of edges) if (e.a !== e.b) { arms[e.a].push(e); arms[e.b].push(e); }
  const outDir = (e, n) => {
    const P = e.pts, [p, q] = e.a === n ? [P[0], P[1]] : [P[P.length - 1], P[P.length - 2]];
    const d = Math.hypot(q[0] - p[0], q[1] - p[1]) || 1;
    return [(q[0] - p[0]) / d, (q[1] - p[1]) / d];
  };
  const dot = (u, v) => u[0] * v[0] + u[1] * v[1];
  const out = new Map();
  for (let n = 0; n < nodes.length; n++) {
    if (arms[n].length < 3) continue;
    const A = arms[n].map(e => ({ e, dir: outDir(e, n), hw: hwOf(e) }));
    let tA = -1, tB = -1, best = 1;
    for (let pass = 0; pass < 2 && tA < 0; pass++)
      for (let i = 0; i < A.length; i++)
        for (let j = i + 1; j < A.length; j++) {
          if (pass === 0 && (A[i].e.way.link || A[j].e.way.link)) continue;
          const d = dot(A[i].dir, A[j].dir);
          if (d < best) { best = d; tA = i; tB = j; }
        }
    if (!(tA >= 0 && best < ThroughCos) || A[tA].e === A[tB].e) continue;
    const clipped = new Array(A.length).fill(-1);
    for (let i = 0; i < A.length; i++)
      for (let j = i + 1; j < A.length; j++) {
        if (dot(A[i].dir, A[j].dir) < BranchCos) continue;
        const ai = A[i], aj = A[j];
        let iClips;
        if (ai.e.way.link !== aj.e.way.link) iClips = ai.e.way.link;
        else if (ai.e.way.rank !== aj.e.way.rank) iClips = ai.e.way.rank < aj.e.way.rank;
        else if (Math.abs(ai.hw - aj.hw) > 0.05) iClips = ai.hw < aj.hw;
        else iClips = true;
        if ((i === tA || i === tB) && (j === tA || j === tB)) continue;
        if (j === tA || j === tB) iClips = true;
        if (i === tA || i === tB) iClips = false;
        const c = iClips ? i : j, h = iClips ? j : i;
        if (clipped[c] < 0) clipped[c] = h;
      }
    let allBranch = true;
    for (let i = 0; i < A.length && allBranch; i++) if (i !== tA && i !== tB && clipped[i] < 0) allBranch = false;
    if (allBranch) out.set(n, [A[tA].e, A[tB].e]);
  }
  return out;
}

/// opts: { rFloor(e), emax(e), hw(e), eps, chordCap, collinearDeg, through (mitredThrough's map) }
export function filletGraph(edges, nodes, opts) {
  const { rFloor, emax, hw, eps = 0.02, chordCap = 10, collinearDeg = 0.02, through = new Map() } = opts;
  const colTurn = collinearDeg * DEG;
  const arms = Array.from({ length: nodes.length }, () => []);
  for (const e of edges) if (e.a !== e.b) { arms[e.a].push(e); arms[e.b].push(e); }
  const two = arms.map(l => l.length === 2 && l[0] !== l[1]);
  const other = (e, n) => e.a === n ? e.b : e.a;
  /// the edge a strand runs on into across node n from e, or null (n ends it)
  const passNext = (n, e) => {
    if (two[n]) return arms[n][0] === e ? arms[n][1] : arms[n][0];
    const t = through.get(n);
    if (t && (t[0] === e || t[1] === e)) return t[0] === e ? t[1] : t[0];
    return null;
  };

  // ---- strands through 2-arm nodes and mitred junctions' through pairs
  const used = new Uint8Array(edges.length);
  const strands = [];
  for (const e0 of edges) {
    if (used[e0.id] || e0.a === e0.b) continue;
    let e = e0, n = e0.a, closed = false, guard = 0;
    for (let o; (o = passNext(n, e));) {
      if (o === e0) { closed = true; break; }
      e = o; n = other(o, n);
      if (++guard > edges.length) break;
    }
    if (closed) { e = e0; n = e0.a; }
    const seq = [];
    let cur = e, at = n, junctions = 0;
    for (;;) {
      if (used[cur.id]) break;
      used[cur.id] = 1; seq.push({ e: cur, at });
      const far = other(cur, at);
      const nx = passNext(far, cur);
      if (!nx || used[nx.id]) break;
      if (!two[far]) junctions++;
      cur = nx; at = far;
    }
    strands.push({ seq, closed: closed && seq.length > 1, junctions });
  }
  // the strands across mitred junctions first: they move those nodes, and every
  // other arm there then starts from where the node went
  strands.sort((p, q) => (q.junctions > 0) - (p.junctions > 0));
  const movedNodes = new Set();
  /// Put e's end at node n onto the node's (moved) position, the move easing
  /// out along the edge over max(10 m, 20 x the move) (never past half of it),
  /// so an arm that follows its node never kinks.
  const snapEnd = (e, n) => {
    const P = e.pts, atA = e.a === n, end = atA ? P[0] : P[P.length - 1];
    const dx = nodes[n][0] - end[0], dz = nodes[n][1] - end[1], m = Math.hypot(dx, dz);
    if (m < 1e-6) return;
    const Lf = Math.max(1e-3, Math.min(e.len * 0.5, Math.max(10, 20 * m)));
    const idx = atA ? P.map((_, k) => k) : P.map((_, k) => P.length - 1 - k);
    const orig = P.map(q => q.slice());
    let s = 0;
    for (let q = 0; q < idx.length; q++) {
      const k = idx[q];
      if (q) s += dist(orig[k], orig[idx[q - 1]]);
      if (s >= Lf) break;
      const t = s / Lf, w = 1 - t * t * (3 - 2 * t);
      P[k] = [orig[k][0] + dx * w, orig[k][1] + dz * w];
    }
    let L = 0; for (let q = 1; q < P.length; q++) L += dist(P[q], P[q - 1]);
    e.len = L;
  };

  const stats = { strands: strands.length, closed: 0, vertices: 0, filleted: 0, merged: 0, tight: 0, overEmax: 0,
                  nodesMoved: 0, nodeMoveMax: 0, pointsBefore: 0, pointsAfter: 0, armsFollowed: 0, junctionsFilleted: 0 };
  const review = [];
  for (const e of edges) stats.pointsBefore += e.pts.length;
  const moves = [];

  for (const st of strands) {
    const { seq } = st;
    // the ends are FIXED where their nodes stand now (a mitred junction may have moved)
    {
      const f = seq[0], l = seq[seq.length - 1], far = other(l.e, l.at);
      if (movedNodes.has(f.at)) snapEnd(f.e, f.at);
      if (movedNodes.has(far)) snapEnd(l.e, far);
    }
    // the strand as one polyline; V: { p, nodes: [node ids], e (edge for class) }
    const V = [];
    for (let k = 0; k < seq.length; k++) {
      const { e, at } = seq[k];
      const pts = e.a === at ? e.pts : [...e.pts].reverse();
      if (k === 0) V.push({ p: pts[0].slice(), nodes: [at], cls: [e] });
      for (let q = 1; q < pts.length; q++) V.push({ p: pts[q].slice(), nodes: [], cls: [e] });
      const far = other(e, at);
      V[V.length - 1].nodes.push(far);
      if (k + 1 < seq.length) V[V.length - 1].cls.push(seq[k + 1].e);
    }
    // the ends are fixed; a closed ring of 2-arm nodes (none in the 2026-09 data)
    // is held at its first node, counted in stats.closed
    if (st.closed) stats.closed++;
    // drop near-duplicate vertices (keep the one carrying nodes)
    const W = [V[0]];
    for (let i = 1; i < V.length; i++) {
      const last = W[W.length - 1];
      if (dist(V[i].p, last.p) < 0.01 && i < V.length - 1) {
        if (V[i].nodes.length) { last.nodes.push(...V[i].nodes); last.cls.push(...V[i].cls); }
        continue;
      }
      if (dist(V[i].p, last.p) < 0.01 && i === V.length - 1 && W.length > 1) {
        // the end is fixed: move the carried nodes onto it
        V[i].nodes.unshift(...last.nodes.filter(n => !V[i].nodes.includes(n)));
        W[W.length - 1] = V[i];
        continue;
      }
      W.push(V[i]);
    }
    stats.vertices += W.length;
    const fixedEnd = i => i === 0 || i === W.length - 1;
    const RF = v => Math.max(...v.cls.map(rFloor));   // the class floor (the stricter arm at a class change)
    const EM = v => Math.min(...v.cls.map(emax));
    const HW = v => Math.max(...v.cls.map(hw));
    for (const v of W) { v.rf = RF(v); v.em = EM(v); v.hw = HW(v); v.srcs = [v.p]; }

    // ---- shares, with merges of short same-hand pairs until nothing merges
    let T, D, U, Vv, R;
    const solve = () => {
      const m = W.length;
      D = new Float64Array(m); T = new Float64Array(m); R = new Float64Array(m);
      U = new Array(m); Vv = new Array(m);
      const L = new Float64Array(m);   // L[i]: segment i -> i+1
      for (let i = 0; i + 1 < m; i++) L[i] = dist(W[i].p, W[i + 1].p);
      const want = new Float64Array(m), wt = new Float64Array(m);
      for (let i = 1; i + 1 < m; i++) {
        const a = W[i - 1].p, b = W[i].p, c = W[i + 1].p;
        const ux = b[0] - a[0], uz = b[1] - a[1], vx = c[0] - b[0], vz = c[1] - b[1];
        const lu = Math.hypot(ux, uz), lv = Math.hypot(vx, vz);
        U[i] = [ux / lu, uz / lu]; Vv[i] = [vx / lv, vz / lv];
        D[i] = Math.atan2(ux * vz - uz * vx, ux * vx + uz * vz);
        const h = Math.abs(D[i]) / 2;
        if (Math.abs(D[i]) < colTurn) { want[i] = 0; continue; }
        const rE = W[i].em / (1 / Math.cos(h) - 1);
        // never further off the mapped corner than max(Emax, half the road width): a
        // near U-turn at one vertex keeps a tight turn (listed), not a cut-off loop
        const r = Math.min(Math.max(W[i].rf, rE), Math.max(W[i].em, W[i].hw) / (1 / Math.cos(h) - 1));
        want[i] = Math.min(1e6, r * Math.tan(h));
        wt[i] = Math.abs(D[i]);
      }
      // shares per segment in proportion to the TURN of the corner at each end:
      // the curvature is then as even as the mapped line allows (a nearly
      // straight vertex beside a real corner takes almost nothing). A corner
      // capped by its Emax / departure arc leaves the rest of the segment to
      // its neighbour (second pass).
      const room = new Float64Array(m);
      for (let i = 0; i + 1 < m; i++) {
        const l = L[i];
        // a fixed junction end keeps max(1 m, a quarter) of its leg straight
        room[i] = (fixedEnd(i) && W.length > 2) || fixedEnd(i + 1) ? Math.max(0, l - Math.max(1, 0.25 * l)) : l;
      }
      const shareL = new Float64Array(m), shareR = new Float64Array(m);   // for segment i: to vertex i (L), to vertex i+1 (R)
      for (let i = 0; i + 1 < m; i++) {
        const a = fixedEnd(i) ? 0 : wt[i], b = fixedEnd(i + 1) ? 0 : wt[i + 1];
        if (a + b > 0) { shareL[i] = room[i] * a / (a + b); shareR[i] = room[i] * b / (a + b); }
      }
      for (let i = 1; i + 1 < m; i++) if (want[i] > 0) T[i] = Math.min(want[i], shareR[i - 1], shareL[i]);
      // what an end CAPPED by its want (or a fixed / straight end) leaves unused,
      // the other end may take; two growing ends never share one leftover
      const T1 = Float64Array.from(T);
      const done = i => fixedEnd(i) || want[i] <= 0 || T1[i] >= want[i] * 0.999;
      for (let i = 1; i + 1 < m; i++) {
        if (want[i] <= 0 || done(i)) continue;
        const inL = done(i - 1) ? room[i - 1] - (fixedEnd(i - 1) ? 0 : T1[i - 1]) : shareR[i - 1];
        const inR = done(i + 1) ? room[i] - (fixedEnd(i + 1) ? 0 : T1[i + 1]) : shareL[i];
        T[i] = Math.max(T1[i], Math.min(want[i], inL, inR));
      }
      for (let i = 1; i + 1 < m; i++) if (want[i] > 0) R[i] = T[i] / Math.tan(Math.abs(D[i]) / 2);
      return L;
    };
    let L = solve();
    for (let pass = 0; pass < 64; pass++) {
      let did = false;
      for (let i = 1; i + 1 < W.length && !did; i++) {
        if (Math.abs(D[i]) < colTurn || R[i] >= W[i].rf * 0.999) continue;
        // the neighbour across the shorter side that limits it
        const cands = [];
        if (i - 1 >= 1) cands.push(i - 1);
        if (i + 1 <= W.length - 2) cands.push(i + 1);
        cands.sort((x, y) => L[Math.min(i, x)] - L[Math.min(i, y)]);
        for (const j of cands) {
          if (Math.abs(D[j]) >= colTurn && Math.sign(D[j]) !== Math.sign(D[i])) continue;
          const a = Math.min(i, j), b = Math.max(i, j);
          const Dt = D[a] + D[b];
          if (Math.abs(Dt) > 175 * DEG) continue;
          // the outer tangents: line (W[a-1] -> W[a]) and (W[b] -> W[b+1])
          const p = W[a - 1].p, r = U[a], q = W[b + 1].p, s = Vv[b];
          const den = r[0] * s[1] - r[1] * s[0];
          if (Math.abs(den) < 1e-9) continue;
          const wx = q[0] - p[0], wz = q[1] - p[1];
          const al = (wx * s[1] - wz * s[0]) / den, be = (wx * r[1] - wz * r[0]) / den;   // X = p + al r = q + be s
          if (!(al > 0.05 && be < -0.05)) continue;
          const X = [p[0] + r[0] * al, p[1] + r[1] * al];
          if (dist(X, W[a].p) > L[a] + 1 || dist(X, W[b].p) > L[a] + 1) continue;
          // the arc the merged corner would get must stay near what was mapped:
          // within Emax, or half the road width at a corner of 30 degrees or more
          {
            const h = Math.abs(Dt) / 2, em = Math.min(W[a].em, W[b].em), rf = Math.max(W[a].rf, W[b].rf);
            const rWant = Math.min(Math.max(rf, em / (1 / Math.cos(h) - 1)), Math.max(em, W[a].hw, W[b].hw) / (1 / Math.cos(h) - 1));
            const tFit = Math.min(rWant * Math.tan(h), al, -be);
            const rFit = tFit / Math.tan(h);
            const allow = em;
            const sg = Math.sign(Dt);
            const srcs = [...W[a].srcs, ...W[b].srcs];
            if (srcs.some(sv => arcDev(sv, X, r, s, tFit, rFit, sg) > allow)) continue;
          }
          const mv = { p: X, nodes: [...W[a].nodes, ...W[b].nodes], cls: [...W[a].cls, ...W[b].cls],
                       rf: Math.max(W[a].rf, W[b].rf), em: Math.min(W[a].em, W[b].em), hw: Math.max(W[a].hw, W[b].hw),
                       srcs: [...W[a].srcs, ...W[b].srcs], merged: true };
          W.splice(a, 2, mv);
          stats.merged++;
          did = true;
          break;
        }
      }
      if (!did) break;
      L = solve();
    }

    // ---- emit; every node at an arc-length position on the output
    const Q = [W[0].p.slice()];
    const nodeAt = [];                      // { node, sQ }
    const pending = [];                     // merged corners' nodes: { node, i }, projected once Q is whole
    const q0 = new Int32Array(W.length);    // Q's last index before vertex i was emitted
    let sQ = 0;
    const push = p => { const last = Q[Q.length - 1]; const d = dist(p, last); if (d < 1e-4) return; sQ += d; Q.push(p); };
    for (const n of W[0].nodes) nodeAt.push({ node: n, sQ: 0 });
    for (let i = 1; i + 1 < W.length; i++) {
      const v = W[i];
      q0[i] = Q.length - 1;
      if (!(T[i] > 1e-4)) {
        push(v.p.slice());
        for (const n of v.nodes) nodeAt.push({ node: n, sQ });
        continue;
      }
      stats.filleted++;
      const r = R[i], d = D[i], h = Math.abs(d) / 2, sg = Math.sign(d);
      if (r < v.rf * 0.999) { stats.tight++; review.push({ kind: 'tight', x: v.p[0], z: v.p[1], r, want: v.rf, turnDeg: Math.abs(d) / DEG, edge: v.cls[0].id }); }
      const depSrc = Math.max(...v.srcs.map(s => arcDev(s, v.p, U[i], Vv[i], T[i], r, sg)));
      if (depSrc > v.em + 1e-3) { stats.overEmax++; review.push({ kind: 'overEmax', x: v.p[0], z: v.p[1], dep: depSrc, emax: v.em, r, turnDeg: Math.abs(d) / DEG, edge: v.cls[0].id }); }
      const t0 = [v.p[0] - U[i][0] * T[i], v.p[1] - U[i][1] * T[i]];
      const nrm = [-U[i][1] * sg, U[i][0] * sg];          // toward the centre
      const cen = [t0[0] + nrm[0] * r, t0[1] + nrm[1] * r];
      // the sagitta chord; past the cap, the gate's lone-vertex facet min(c, cap) * turn / 8
      // (SmoothRules.ChordCapM) is what the eye sees, so a gentle arc may keep longer chords
      const cS = Math.sqrt(8 * eps * r * r / (r + v.hw));
      const chord = cS <= chordCap ? cS : Math.max(chordCap, 8 * eps * r / chordCap);
      let n = Math.max(1, Math.ceil(r * Math.abs(d) / chord));
      const single = !v.merged && v.nodes.length === 1;
      if (single && n % 2) n++;
      push(t0);
      const a0 = Math.atan2(t0[1] - cen[1], t0[0] - cen[0]);
      for (let k = 1; k <= n; k++) {
        const a = a0 + sg * Math.abs(d) * k / n;
        push([cen[0] + r * Math.cos(a), cen[1] + r * Math.sin(a)]);
        if (single && k === n / 2) nodeAt.push({ node: v.nodes[0], sQ });
      }
      if (!single) for (const nd of v.nodes) pending.push({ node: nd, i });
    }
    const lastV = W[W.length - 1];
    q0[W.length - 1] = Q.length - 1;
    push(lastV.p.slice());
    for (const n of lastV.nodes) nodeAt.push({ node: n, sQ });
    const cum = [0];
    for (let k = 1; k < Q.length; k++) cum.push(cum[k - 1] + dist(Q[k], Q[k - 1]));
    // a merged corner's nodes: where each old position projects onto the line
    // from the previous corner's end to the next corner's start
    for (const { node: nd, i } of pending) {
      const o = nodes[nd];
      const lo = q0[Math.max(1, i - 1)], hi = i + 2 < W.length ? Math.min(Q.length - 1, q0[i + 2] + 1) : Q.length - 1;
      let best = Infinity, bs = cum[lo];
      for (let k = lo + 1; k <= hi; k++) {
        const A = Q[k - 1], B = Q[k], dx = B[0] - A[0], dz = B[1] - A[1], L2 = dx * dx + dz * dz;
        let t = L2 > 0 ? ((o[0] - A[0]) * dx + (o[1] - A[1]) * dz) / L2 : 0; t = Math.max(0, Math.min(1, t));
        const dd = Math.hypot(o[0] - A[0] - dx * t, o[1] - A[1] - dz * t);
        if (dd < best) { best = dd; bs = cum[k - 1] + Math.sqrt(L2) * t; }
      }
      nodeAt.push({ node: nd, sQ: bs });
    }

    // the strand's node order is seq order: seq[0].at, then each edge's far node
    const order = [seq[0].at, ...seq.map(x => other(x.e, x.at))];
    // pair nodeAt entries with the order (a node id can appear twice only on a closed strand)
    const pos = [];
    const pool = nodeAt.slice();
    for (let k = 0; k < order.length; k++) {
      let idx = -1;
      for (let q = 0; q < pool.length; q++) if (pool[q] && pool[q].node === order[k]) { idx = q; break; }
      if (idx < 0) throw new Error(`fillet: node ${order[k]} lost on a strand`);
      pos.push(pool[idx].sQ); pool[idx] = null;
    }
    pos[0] = 0; pos[pos.length - 1] = sQ;
    // monotonic, each edge at least min(0.5 m, its old length) long
    for (let k = 1; k < pos.length - 1; k++) {
      const minGap = Math.min(0.5, seq[k - 1].e.len * 0.5);
      if (pos[k] < pos[k - 1] + minGap) pos[k] = pos[k - 1] + minGap;
    }
    for (let k = pos.length - 2; k >= 1; k--) {
      const minGap = Math.min(0.5, seq[k].e.len * 0.5);
      if (pos[k] > pos[k + 1] - minGap) pos[k] = pos[k + 1] - minGap;
    }
    // cut Q at the node positions
    const at = s => {
      let lo = 0, hi = Q.length - 1;
      while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (cum[mid] <= s) lo = mid; else hi = mid; }
      const L2 = cum[hi] - cum[lo], t = L2 > 0 ? (s - cum[lo]) / L2 : 0;
      return { i: lo, t, p: [Q[lo][0] + (Q[hi][0] - Q[lo][0]) * t, Q[lo][1] + (Q[hi][1] - Q[lo][1]) * t] };
    };
    const cuts = pos.map(s => at(s));
    for (let k = 0; k < seq.length; k++) {
      const c0 = cuts[k], c1 = cuts[k + 1];
      const piece = [c0.p];
      for (let q = c0.i + 1; q <= c1.i; q++) if (dist(Q[q], piece[piece.length - 1]) > 1e-3) piece.push(Q[q].slice());
      if (dist(c1.p, piece[piece.length - 1]) > 1e-3) piece.push(c1.p);
      else piece[piece.length - 1] = c1.p;
      if (piece.length < 2) piece.push(c1.p);
      const { e, at: from } = seq[k];
      e.pts = e.a === from ? piece : piece.reverse();
      let len = 0; for (let q = 1; q < e.pts.length; q++) len += dist(e.pts[q], e.pts[q - 1]);
      e.len = len;
    }
    for (let k = 1; k < order.length - 1; k++) {
      const nd = order[k], p = cuts[k].p;
      const mvd = dist(p, nodes[nd]);
      if (mvd > 1e-3) { stats.nodesMoved++; moves.push(mvd); }
      stats.nodeMoveMax = Math.max(stats.nodeMoveMax, mvd);
      nodes[nd] = p;
      if (mvd > 1e-6 && !two[nd]) movedNodes.add(nd);
    }
  }
  // arms already drawn when a later strand moved their junction follow it
  for (const n of movedNodes) for (const e of arms[n]) {
    const P = e.pts, end = e.a === n ? P[0] : P[P.length - 1];
    if (dist(end, nodes[n]) > 1e-6) { snapEnd(e, n); stats.armsFollowed++; }
  }
  stats.junctionsFilleted = movedNodes.size;
  for (const e of edges) stats.pointsAfter += e.pts.length;
  moves.sort((a, b) => a - b);
  stats.nodeMoveMedian = moves.length ? +moves[moves.length >> 1].toFixed(3) : 0;
  stats.nodeMoveMax = +stats.nodeMoveMax.toFixed(2);
  return { stats, review };
}

/// How far the arc (tangent points at +-T about the corner c, radius r)
/// passes from a source vertex s: the distance from s to the arc's circle
/// where s lies inside the corner's wedge, else to the nearer tangent line.
function arcDev(s, c, u, v, T, r, sg) {
  const t0 = [c[0] - u[0] * T, c[1] - u[1] * T];
  const nrm = [-u[1] * sg, u[0] * sg];
  const cen = [t0[0] + nrm[0] * r, t0[1] + nrm[1] * r];
  return Math.abs(Math.hypot(s[0] - cen[0], s[1] - cen[1]) - r);
}
