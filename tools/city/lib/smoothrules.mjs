// smoothrules.mjs - the smoothness gate's numbers for the offline tools, read
// from THE one place they live: Assets/PSXRacing/Editor/SmoothRules.cs. Both
// implementations of the gate (CitySmooth.cs and linecheck.mjs) therefore run
// on the same V, the same R_min table and the same check states; a constant
// this parser cannot find is an error, never a silent default.
//
// A `const float` is read as C# holds it: the nearest float (Math.fround), not
// the decimal the source spells. 0.025f is 0.02500000037 in CitySmooth, and a
// double 0.025 here moved scores by ~1e-8 - harmless until the arc frame's
// discrete tests (|k| <= tolK at exactly R 2000, the curvature-step test, the
// V/4 fit, the V/50 chord points, ceil(L / sqrt(4V/k))) flipped on it and the
// two gates disagreed (review 7). A `const double` stays a double, and so do
// the RClass tables' values once rounded to float (RClass.r is a float).
import { readFileSync } from 'node:fs';

// A literal only: an expression (`V * 2f`) would parse as its first number.
function csNumber(v, name) {
  if (!/^[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?$/.test(v)) throw new Error(`SmoothRules.cs: ${name} = ${v} is not a numeric literal`);
  return parseFloat(v);
}

export function readSmoothRules(csPath) {
  const src = readFileSync(csPath, 'utf8');
  const R = { source: csPath };
  for (const m of src.matchAll(/public const (float|double|int|bool) (\w+) = ([^;]+);/g)) {
    const [, type, name, raw] = m;
    const v = raw.trim();
    if (type === 'bool') R[name] = v === 'true';
    else if (type === 'int') R[name] = parseInt(v, 10);
    else if (type === 'double') R[name] = csNumber(v.replace(/[dD]$/, ''), name);
    else R[name] = Math.fround(csNumber(v.replace(/[fF]$/, ''), name));
  }
  const table = name => {
    const m = src.match(new RegExp(`${name} =\\s*\\{([\\s\\S]*?)\\};`));
    if (!m) throw new Error(`SmoothRules.cs: no ${name} table`);
    return [...m[1].matchAll(/new RClass\("([a-z_]+)", ([0-9.]+)f\)/g)].map(x => ({ cls: x[1], r: Math.fround(parseFloat(x[2])) }));
  };
  R.RMin = Object.fromEntries(table('RMin').map(x => [x.cls, x.r]));
  R.CurbReturn = Object.fromEntries(table('CurbReturn').map(x => [x.cls, x.r]));
  R.ClassWeight = Object.fromEntries(table('ClassWeight').map(x => [x.cls, x.r]));
  R.TaperFloor = Object.fromEntries(table('TaperFloor').map(x => [x.cls, x.r]));
  const cm = src.match(/Checks =\s*\{([\s\S]*?)\};/);
  if (!cm) throw new Error('SmoothRules.cs: no Checks table');
  R.Checks = [...cm[1].matchAll(/new CheckDef\("(\w+)", "(\w+)", State\.(\w+), "([^"]*)", "([^"]*)"\)/g)]
    .map(x => ({ id: x[1], name: x[2], state: x[3].toUpperCase(), what: x[4], zeroFrom: x[5] }));
  const pw = src.match(/PinnedWays = \{([^}]*)\}/);
  if (!pw) throw new Error('SmoothRules.cs: no PinnedWays');
  R.PinnedWays = [...pw[1].matchAll(/(\d+)u/g)].map(x => parseInt(x[1], 10));
  const need = ['V', 'SampleStepM', 'CollinearDeg', 'ChordCapM', 'JitterStepM', 'JitterHalfM', 'CurveHalfM', 'InnerEdgeMinRM',
    'LineWidthTol', 'StrayM', 'StrayRunM', 'ExistInsetM', 'EdgeLineInsetM', 'PlanTaperShape', 'JoinM', 'MatchM', 'SeamM', 'GapM',
    'FanMouthM', 'DashM', 'DashGapM', 'DashTol', 'StubM', 'CrossM', 'CrossDyM', 'GoreNoseM', 'DensifyEpsM', 'KeyStepM', 'WorstN',
    'DedupM', 'RefSpotReachM', 'RankRatioCap', 'ExposureRoute', 'ExposureRefSpot', 'BandCount', 'ReportOnly', 'PinActive', 'CurbReturnShare',
    'KinkNoiseShare', 'RunBreakM', 'RatioQuantum', 'TexelPadM', 'FastInAudit', 'MergeMarginM', 'LengthQuantumM', 'KinkViewM', 'PixelAtM', 'SimplifyEpsM'];
  for (const k of need) if (R[k] === undefined || Number.isNaN(R[k])) throw new Error(`SmoothRules.cs: constant ${k} not found`);
  if (R.Checks.length < 17) throw new Error(`SmoothRules.cs: ${R.Checks.length} checks parsed`);
  R.rMinFor = cls => R.RMin[cls] ?? 7.5;
  R.taperFloorFor = cls => R.TaperFloor[cls] ?? 15;
  R.weightFor = cls => cls.endsWith('_link') ? 1 : (R.ClassWeight[cls] ?? 1);
  R.check = id => R.Checks.find(c => c.id === id);
  return R;
}
