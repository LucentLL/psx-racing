// sources.mjs - the Credits table of tools/city/SOURCES.md, read once for
// everything that prints a credit: the attribution export_osm.mjs writes into
// the data, the in-game CREDITS pages (Resources/psx_credits*.txt), the
// one-line credit a front page prints (Resources/psx_credits_line.txt) and the
// LICENSES.txt published beside the game (tools/city/credits.mjs writes them).
// SOURCES.md is the one place a credit line is written down.
//
// PER EDITION (see Assets/PSXRacing/Scripts/Edition.cs): MAIN ships the
// stages and none of Charlotte's data, CITY ships Charlotte and none of the
// stages, ALL ships everything. A row's "shipped in" says which: one
// "EDITION: what" group per edition that carries the row's data, separated by
// ";". A row credits exactly the editions it names; ALL credits every row.

import { readFileSync, existsSync } from 'node:fs';

/// The editions a row may name. ALL is not one of them: it is every row.
export const EDITIONS = ['MAIN', 'CITY'];

/// "CITY: a, b; MAIN: c" -> [['CITY', 'a, b'], ['MAIN', 'c']]. Throws on a
/// malformed group, an unknown or repeated edition, or no group at all.
function parseShippedIn(id, s) {
  const groups = [];
  for (const part of (s || '').split(';').map(p => p.trim()).filter(Boolean)) {
    const m = /^([A-Z]+):\s*(\S.*)$/.exec(part);
    if (!m) throw new Error(`SOURCES.md Credits: "${id}" shipped in: "${part}" is not "EDITION: what" (EDITION one of ${EDITIONS.join(', ')})`);
    if (!EDITIONS.includes(m[1])) throw new Error(`SOURCES.md Credits: "${id}" shipped in: "${m[1]}" is not an edition (${EDITIONS.join(', ')}; ALL is every row)`);
    if (groups.some(g => g[0] === m[1])) throw new Error(`SOURCES.md Credits: "${id}" shipped in names ${m[1]} twice`);
    groups.push([m[1], m[2].trim()]);
  }
  if (!groups.length) throw new Error(`SOURCES.md Credits: "${id}" ships in no edition (shipped in: "EDITION: what; ...")`);
  return groups;
}

/// The rows of SOURCES.md's "## Credits" table, in order: { id, credit,
/// short, licence, 'licence link', 'shipped in', shipped: [[EDITION, what]],
/// editions: [EDITION] }. Throws on a missing section or table, a missing
/// column, a malformed row, a text column that is not plain one-line ASCII
/// (the game's font and the no-clip rule), a "shipped in" that names no
/// edition, an edition no row credits, or no OpenStreetMap row (ODbL).
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
  for (const col of ['id', 'credit', 'short', 'shipped in'])
    if (!header || !header.includes(col)) throw new Error(`SOURCES.md Credits: no table with a "${col}" column`);
  if (!rows.length) throw new Error('SOURCES.md Credits: the table is empty');
  for (const r of rows) {
    if (!r.id || !r.credit || !r.short) throw new Error(`SOURCES.md Credits: a row with no id, credit or short (${JSON.stringify(r)})`);
    for (const k of ['credit', 'short', 'licence', 'licence link', 'shipped in'])
      if (r[k] && !/^[\x20-\x7e]+$/.test(r[k])) throw new Error(`SOURCES.md Credits: "${r.id}" ${k} is not plain one-line ASCII`);
    r.shipped = parseShippedIn(r.id, r['shipped in']);
    r.editions = r.shipped.map(g => g[0]);
  }
  if (!rows.some(r => r.id === 'osm')) throw new Error('SOURCES.md Credits: the OpenStreetMap row (id osm) is required by ODbL');
  for (const ed of EDITIONS)
    if (!rows.some(r => r.editions.includes(ed))) throw new Error(`SOURCES.md Credits: no row ships in ${ed} - every edition carries data someone is owed a credit for`);
  return rows;
}

function checkEdition(ed) {
  if (ed !== 'ALL' && !EDITIONS.includes(ed)) throw new Error(`edition "${ed}" is not ALL, ${EDITIONS.join(' or ')}`);
}

/// The rows <ed>'s build credits: every row for ALL, else the rows whose
/// "shipped in" names <ed>.
export function rowsFor(rows, ed) {
  checkEdition(ed);
  return ed === 'ALL' ? rows : rows.filter(r => r.editions.includes(ed));
}

const bareLink = u => (u || '').replace(/^https?:\/\//, '').replace(/\/$/, '');

/// An in-game CREDITS page (plain text, wrapped by the page itself): each
/// credit line of <ed>'s rows, then its licence and link. ALL's is the one
/// Resources/psx_credits.txt has always held.
export function creditsPage(rows, ed = 'ALL') {
  const out = ['MAP AND TERRAIN DATA', ''];
  for (const r of rowsFor(rows, ed)) {
    out.push(r.credit);
    const lic = [r.licence, bareLink(r['licence link'])].filter(Boolean).join(' - ');
    if (lic) out.push('Licence: ' + lic);
    out.push('');
  }
  out.push('The data was adapted for the game: simplified, re-projected and baked into its own formats. ' +
           'Every source and its licence is listed in LICENSES.txt beside the game.');
  return out.join('\n') + '\n';
}

/// The one line a front page prints for <ed>: its rows' short credits.
export function frontLine(rows, ed) {
  return rowsFor(rows, ed).map(r => r.short.replace(/[.\s]+$/, '')).join('. ') + '.';
}

/// Resources/psx_credits_line.txt: one "EDITION: line" row for ALL and each
/// edition (CreditsPanel.Line reads the one it is asked for).
export function linesFile(rows) {
  const out = [
    '# The one-line data credit each edition\'s front page prints (CreditsPanel.Line;',
    '# the CITY front page, CityFrontEnd, prints CITY\'s). One "EDITION: line" row each.',
    '# Generated from tools/city/SOURCES.md by tools/city/credits.mjs - do not edit.',
  ];
  for (const ed of ['ALL', ...EDITIONS]) out.push(`${ed}: ${frontLine(rows, ed)}`);
  return out.join('\n') + '\n';
}

/// LICENSES.txt, published next to index.html: <ed>'s rows, each with its
/// licence, its link and what of <ed> it is in (every edition's, for ALL).
export function licensesFile(rows, ed = 'ALL') {
  const list = rowsFor(rows, ed);
  const title = ed === 'ALL'
    ? 'PSX Racing - map and terrain data: credits and licences'
    : `PSX Racing, ${ed} edition - map and terrain data: credits and licences`;
  const out = [
    title,
    '='.repeat(title.length),
    '',
    ed === 'ALL'
      ? 'The game\'s map and terrain data are adapted from the sources below:'
      : `The ${ed} edition's map and terrain data are adapted from the sources below:`,
    'simplified, re-projected and baked into the game\'s own formats.',
    '',
  ];
  for (const r of list) {
    out.push(r.credit);
    if (r.licence) out.push('  Licence: ' + r.licence);
    if (r['licence link']) out.push('  ' + r['licence link']);
    for (const [e, what] of r.shipped) {
      if (ed === 'ALL') out.push(`  In (${e} edition): ${what}`);
      else if (e === ed) out.push('  In: ' + what);
    }
    out.push('');
  }
  if (list.some(r => r.id === 'osm'))
    out.push('Contains information from OpenStreetMap, which is made available here',
             'under the Open Database License (ODbL): https://www.openstreetmap.org/copyright',
             '');
  out.push('The full registry of sources (including those used only to check the',
           'data, never shipped) is tools/city/SOURCES.md in the game\'s source',
           'repository, https://github.com/LucentLL/psx-racing',
           '',
           'Generated from tools/city/SOURCES.md by tools/city/credits.mjs.');
  return out.join('\n') + '\n';
}
