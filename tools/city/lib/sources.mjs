// sources.mjs - the Credits table of tools/city/SOURCES.md, read once for
// everything that prints a credit: the attribution export_osm.mjs writes into
// the data, the in-game CREDITS page (Resources/psx_credits.txt) and the
// LICENSES.txt published beside the game (tools/city/credits.mjs writes both).
// SOURCES.md is the one place a credit line is written down.

import { readFileSync, existsSync } from 'node:fs';

/// The rows of SOURCES.md's "## Credits" table, in order: { id, credit,
/// licence, 'licence link', 'shipped in' }. Throws on a missing section or
/// table, a malformed row, a credit that is not plain one-line ASCII (the
/// game's font and the no-clip rule), or no OpenStreetMap row (ODbL).
export function readCredits(path) {
  if (!existsSync(path)) throw new Error('tools/city/SOURCES.md is missing: it holds the credits');
  const md = readFileSync(path, 'utf8').replace(/\r\n/g, '\n');
  const at = md.search(/^## Credits\s*$/m);
  if (at < 0) throw new Error('tools/city/SOURCES.md has no "## Credits" section');
  const lines = md.slice(at).split('\n').slice(1);
  const rows = [];
  let header = null;
  for (const l of lines) {
    if (/^## /.test(l)) break;
    if (!l.startsWith('|')) { if (header) break; continue; }
    const cells = l.split('|').slice(1, -1).map(c => c.trim());
    if (!header) { header = cells; continue; }
    if (cells.every(c => /^-+$/.test(c))) continue;
    if (cells.length !== header.length) throw new Error(`SOURCES.md Credits: row "${l}" has ${cells.length} cells, the header ${header.length}`);
    rows.push(Object.fromEntries(header.map((h, i) => [h, cells[i]])));
  }
  if (!header || !header.includes('id') || !header.includes('credit')) throw new Error('SOURCES.md Credits: no table with "id" and "credit" columns');
  if (!rows.length) throw new Error('SOURCES.md Credits: the table is empty');
  for (const r of rows) {
    if (!r.id || !r.credit) throw new Error(`SOURCES.md Credits: a row with no id or credit (${JSON.stringify(r)})`);
    for (const k of ['credit', 'licence', 'licence link'])
      if (r[k] && !/^[\x20-\x7e]+$/.test(r[k])) throw new Error(`SOURCES.md Credits: "${r.id}" ${k} is not plain one-line ASCII`);
  }
  if (!rows.some(r => r.id === 'osm')) throw new Error('SOURCES.md Credits: the OpenStreetMap row (id osm) is required by ODbL');
  return rows;
}

const bareLink = u => (u || '').replace(/^https?:\/\//, '').replace(/\/$/, '');

/// The in-game CREDITS page (plain text, wrapped by the page itself): each
/// credit line, then its licence and link.
export function creditsPage(rows) {
  const out = ['MAP AND TERRAIN DATA', ''];
  for (const r of rows) {
    out.push(r.credit);
    const lic = [r.licence, bareLink(r['licence link'])].filter(Boolean).join(' - ');
    if (lic) out.push('Licence: ' + lic);
    out.push('');
  }
  out.push('The data was adapted for the game: simplified, re-projected and baked into its own formats. ' +
           'Every source and its licence is listed in LICENSES.txt beside the game.');
  return out.join('\n') + '\n';
}

/// LICENSES.txt, published next to index.html.
export function licensesFile(rows) {
  const out = [
    'PSX Racing - map and terrain data: credits and licences',
    '========================================================',
    '',
    'The game\'s map and terrain data are adapted from the sources below:',
    'simplified, re-projected and baked into the game\'s own formats.',
    '',
  ];
  for (const r of rows) {
    out.push(r.credit);
    if (r.licence) out.push('  Licence: ' + r.licence);
    if (r['licence link']) out.push('  ' + r['licence link']);
    if (r['shipped in']) out.push('  In: ' + r['shipped in']);
    out.push('');
  }
  out.push('Contains information from OpenStreetMap, which is made available here',
           'under the Open Database License (ODbL): https://www.openstreetmap.org/copyright',
           '',
           'The full registry of sources (including those used only to check the',
           'data, never shipped) is tools/city/SOURCES.md in the game\'s source',
           'repository, https://github.com/LucentLL/psx-racing',
           '',
           'Generated from tools/city/SOURCES.md by tools/city/credits.mjs.');
  return out.join('\n') + '\n';
}
