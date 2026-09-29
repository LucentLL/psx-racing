// smoothrules.mjs - the smoothness gate's numbers for the offline tools, read
// from THE one place they live: Assets/PSXRacing/Editor/SmoothRules.cs. Both
// implementations of the gate (CitySmooth.cs and linecheck.mjs) therefore run
// on the same V, the same R_min table and the same check states; a constant
// this parser cannot find is an error, never a silent default.
import { readFileSync } from 'node:fs';

export function readSmoothRules(csPath) {
  const src = readFileSync(csPath, 'utf8');
  const R = { source: csPath };
  for (const m of src.matchAll(/public const (float|int|bool) (\w+) = ([^;]+);/g)) {
    const [, type, name, raw] = m;
    const v = raw.trim();
    if (type === 'bool') R[name] = v === 'true';
    else if (type === 'int') R[name] = parseInt(v, 10);
    else R[name] = parseFloat(v.replace(/f$/, ''));
  }
  const table = name => {
    const m = src.match(new RegExp(`${name} =\\s*\\{([\\s\\S]*?)\\};`));
    if (!m) throw new Error(`SmoothRules.cs: no ${name} table`);
    return [...m[1].matchAll(/new RClass\("([a-z_]+)", ([0-9.]+)f\)/g)].map(x => ({ cls: x[1], r: parseFloat(x[2]) }));
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
