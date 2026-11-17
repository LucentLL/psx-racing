// netexport.mjs - the road network and water as compact JSON for the Python renderer
import { writeFileSync, mkdirSync, readFileSync } from 'node:fs';
import { city, WT, toLat, toLon } from './lib.mjs';
import { pathToFileURL } from 'node:url';
const C = city();
mkdirSync(new URL('./out/', import.meta.url), { recursive: true });
const r1 = v => Math.round(v * 10) / 10;
const edges = C.edges.map(e => [e.rank, (e.link ? 1 : 0) | (e.oneway ? 2 : 0) | (e.bridge ? 4 : 0) | (e.tunnel ? 8 : 0), +e.profile.width.toFixed(1), e.pts.flat().map(r1), e.name || '']);
const waters = C.waters.map(w => [w.kind, +w.width.toFixed(1), w.pts.flat().map(r1), w.name || '']);
writeFileSync(new URL('./out/net.json', import.meta.url), JSON.stringify({ edges, waters }));
console.log('edges', edges.length, 'waters', waters.length);
// buildings (footprints) for the built-up shading
const cd = await import(pathToFileURL(WT + '/tools/city/lib/citydata.mjs').href);
const B = cd.parseBld(readFileSync(WT + '/Assets/PSXRacing/Resources/charlotte_bld.bytes'));
const blds = B.footprints.filter(f => !f.hidden).map(f => [Math.round(f.h), f.pts.flat().map(r1)]);
writeFileSync(new URL('./out/bld.json', import.meta.url), JSON.stringify(blds));
console.log('buildings', blds.length, 'bbox', B.bbox.map(v => v.toFixed(0)).join(','));
