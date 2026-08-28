// Fast preview of CarModelLibrary's assignment for all 317 catalog cars.
// A JS mirror of the C# rules, kept only so the tables can be iterated on
// without a Unity round trip. The authority is CarModelLibrary.cs - confirm
// with Tools > PSX Racing > Dump Car Model Mapping before believing this.
//
//   node tools/carmodels/preview_mapping.mjs [--full]
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const project = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const cars = JSON.parse(fs.readFileSync(path.join(project, 'Assets/PSXRacing/Resources/rg2_cars.json'), 'utf8')).cars;

const MODELS = [
  ['rx7_fd',       'Mazda RX-7 (FD)',           'Japan',   1992, 'Sports',   1280],
  ['supra_a80',    'Toyota Supra (A80)',        'Japan',   1993, 'GT',       1510],
  ['skyline_r32',  'Nissan Skyline GT-R (R32)', 'Japan',   1989, 'Sports',   1480],
  ['jdm_pickup',   'Compact pickup',            'Japan',   1983, 'Pickup',   1100],
  ['gto_66',       "Pontiac GTO '66",           'America', 1966, 'Muscle',   1650],
  ['mustang_67',   "Ford Mustang Fastback '67", 'America', 1967, 'Muscle',   1400],
  ['charger_69',   "Dodge Charger '69",         'America', 1969, 'Muscle',   1700],
  ['daytona_69',   "Charger Daytona '69",       'America', 1969, 'Muscle',   1750],
  ['bmw_e30',      'BMW 3-Series (E30)',        'Europe',  1985, 'Saloon',   1150],
  ['audi_saloon',  'Audi 80/100',               'Europe',  1986, 'Saloon',   1220],
  ['euro_hatch',   'European supermini',        'Europe',  1983, 'Hatch',     850],
  ['volvo_estate', 'Volvo 240 Estate',          'Europe',  1985, 'Estate',   1350],
  ['citroen_cx',   'Citroen CX',                'Europe',  1980, 'Saloon',   1320],
  ['mb_pagoda',    "Mercedes-Benz SL 'Pagoda'", 'Europe',  1965, 'Roadster', 1350],
  ['landrover',    'Land Rover pickup',         'Europe',  1985, 'Offroad',  1900],
  ['classic_van',  'Classic panel van',         'Europe',  1960, 'Van',      1400],
].map(([key, name, region, year, body, kg]) => ({ key, name, region, year, body, kg }));

const HAND = [
  ['Estate|Touring Wagon|Sport Wagon|STAGEA', 'volvo_estate'],
  ['SILEIGHTY|NISMO 270R', 'rx7_fd'],
  ['Spoon INTEGRA', 'euro_hatch'],
  ['Mazda RX-7|Mazda 110S', 'rx7_fd'],
  ['SKYLINE|CALSONIC|PENNZOIL|NISMO|Lexus (IS|GS)', 'skyline_r32'],
  ['Subaru IMPREZA|Lancer Evolution|Galant.*VR-4|LEGNUM|LEGACY B4', 'skyline_r32'],
  ['Toyota SUPRA|Toyota CELICA XX|3000GT|300ZX|Lexus SC', 'supra_a80'],
  ['Pontiac Tempest Le Mans GTO', 'gto_66'],
  ['Shelby Mustang|Mercury Cougar', 'mustang_67'],
  ['Plymouth Super Bird', 'daytona_69'],
  ['Dodge Charger|Plymouth Cuda|Chevrolet Chevelle', 'charger_69'],
  ['Chevrolet Corvette|Ford GT40|Dodge VIPER|Chaparral', 'mustang_67'],
  ['Chevrolet Camaro|BUICK', 'gto_66'],
  ['Volvo 240', 'volvo_estate'],
  ['Mercedes-Benz (300 SL|SL |SLK)', 'mb_pagoda'],
  ['BMW 2002|BMW M Coupe|Mercedes-Benz 190 E|Mercedes 190 E|RUF ', 'bmw_e30'],
  ['Audi quattro|Audi S4|Opel Calibra|Lotus Carlton', 'audi_saloon'],
  ['Volkswagen Golf|Peugeot 20[56]|Renault 5|Citroen Xsara|Opel Tigra|Mercedes-Benz A 160|Ford (Escort|FOCUS)', 'euro_hatch'],
  ['Peugeot 406|Alfa Romeo 1[556][56]', 'citroen_cx'],
  ['PAJERO|ESCUDO', 'landrover'],
].map(([p, k]) => [new RegExp(p, 'i'), k]);

const BODY_RULES = [
  ['Estate|Wagon|STAGEA', 'Estate'],
  ['PAJERO|ESCUDO|Rally Raid|Dirt Trial', 'Offroad'],
  ['Convertible|Spider|Spyder|Duetto|Miata|MX-5|Elise|Europa|Barchetta|S2000|Cobra|427 S/C|MGF|Fairlady 2000|Alpine A1|Roadster|Boxster', 'Roadster'],
  ['3door|CIVIC|STARLET|DEMIO|MIRAGE|CR-X|del Sol|CITY Turbo|BALLADE|Golf|Peugeot 20[56]|Renault 5|Xsara|Tigra|A 160|SERA|323F|DELTA|COROLLA Rally', 'Hatch'],
  ['Sedan|Saloon|Lancer 1600|Lancer EX|CARINA|BLUEBIRD|G20|GS300|IS200|Taurus|156|166|155|406', 'Saloon'],
  ['Esprit|XJ220|Diablo|Cizeta|NSX|Aston Martin|XKR|E-Type|Jensen|Interceptor|Griffith|Cerbera|V8S|Storm|Esperante|XJR-9|787B|R39[02]|R89C|R92CP|88C-V|GT-ONE|905|C 9|CLK-GTR|McLaren|LMR|DOME|Hommell|Panoz|Toyota 7|2000GT|110S', 'GT'],
].map(([p, b]) => [new RegExp(p, 'i'), b]);

function bodyOf(c) {
  for (const [re, b] of BODY_RULES) if (re.test(c.name)) return b;
  if (c.modelYear <= 1975 && c.dispCc >= 4000 && (c.eType || '').startsWith('V8')) return 'Muscle';
  if (c.origin === 'usa' && c.modelYear <= 1975) return 'Muscle';
  if (c.drv === 'FF' && c.kg <= 1150 && c.dispCc > 0 && c.dispCc <= 1800) return 'Hatch';
  return 'Coupe';
}
const regionOf = c => c.origin === 'jpn' ? 'Japan' : c.origin === 'usa' ? 'America' : 'Europe';

const AFF = {
  Sports:   { GT: .85, Coupe: .8, Muscle: .35, Saloon: .3 },
  GT:       { Sports: .85, Coupe: .7, Muscle: .4, Roadster: .3 },
  Coupe:    { Sports: .8, GT: .7, Roadster: .5, Saloon: .45, Muscle: .4, Hatch: .3 },
  Muscle:   { GT: .4, Sports: .3, Saloon: .3 },
  Saloon:   { Estate: .6, Hatch: .5, Coupe: .4, Sports: .35 },
  Estate:   { Saloon: .6, Van: .45, Hatch: .35 },
  Hatch:    { Saloon: .45, Coupe: .35, Estate: .3 },
  Roadster: { Sports: .6, Coupe: .5, GT: .45 },
  Offroad:  { Pickup: .75, Van: .5, Estate: .3 },
  Pickup:   { Offroad: .75, Van: .5 },
  Van:      { Pickup: .5, Estate: .45 },
};
const aff = (car, model) => car === model ? 1 : ((AFF[car] || {})[model] ?? 0.1);
const clamp01 = x => Math.max(0, Math.min(1, x));
const score = (c, m) => 60 * aff(bodyOf(c), m.body)
  + (regionOf(c) === m.region ? 34 : 0)
  + (22 - 30 * clamp01(Math.abs(c.modelYear - m.year) / 30))
  + 14 * clamp01(1 - Math.abs(c.kg - m.kg) / 750);

function keyFor(c) {
  for (const [re, k] of HAND) if (re.test(c.name)) return { key: k, hand: true };
  let best = null, bs = -1e9;
  for (const m of MODELS) {
    if (m.body === 'Van' || m.body === 'Pickup') continue;
    const s = score(c, m);
    if (s > bs) { bs = s; best = m; }
  }
  return { key: best.key, hand: false, score: bs };
}

const rows = cars.map(c => ({ c, ...keyFor(c) }));
const counts = {};
for (const r of rows) counts[r.key] = (counts[r.key] || 0) + 1;

if (process.argv.includes('--full')) {
  for (const r of rows.sort((a, b) => a.key.localeCompare(b.key) || a.c.modelYear - b.c.modelYear))
    console.log(`${r.key.padEnd(13)} ${r.hand ? '=' : '~'} ${bodyOf(r.c).padEnd(8)} ${r.c.origin} ${r.c.modelYear} ${String(r.c.kg).padStart(4)}kg  ${r.c.name}`);
  console.log('');
}
for (const m of MODELS) console.log(String(counts[m.key] || 0).padStart(4) + '  ' + m.key.padEnd(13) + m.name);
console.log(`${rows.filter(r => r.hand).length}/${rows.length} hand-mapped`);
