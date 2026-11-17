// trackfiles.mjs - the Charlotte tracks, ONE FILE EACH (merge-safe: a lane that adds a track adds files,
// it edits nothing shared):
//   Assets/PSXRacing/Resources/CityTracks/<route id>.json   { version, graph, attribution, candidate, route, venue }
//   Assets/PSXRacing/Resources/CityTracks/_order.txt        the venues' order in TrackCatalog.All (the
//                                                           integrator's: tools/city/tracks/order.mjs)
// Scripts/CityTracks.cs reads the same files at load (Resources.LoadAll): no index to edit, no .cs to edit.
import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

export const TRACKS_REL = 'Assets/PSXRacing/Resources/CityTracks';
export const ORDER_FILE = '_order.txt';

export function tracksDir(wt) { return join(wt, TRACKS_REL); }

/// every track file, sorted by file name (the order the game appends tracks _order.txt does not list yet)
export function readTracks(wt) {
  const dir = tracksDir(wt);
  if (!existsSync(dir)) return [];
  return readdirSync(dir).filter(n => n.endsWith('.json')).sort()
    .map(n => ({ file: n, ...JSON.parse(readFileSync(join(dir, n), 'utf8')) }));
}

/// the venue ids _order.txt lists (blank lines and # comments skipped)
export function readOrder(wt) {
  const p = join(tracksDir(wt), ORDER_FILE);
  if (!existsSync(p)) return [];
  return readFileSync(p, 'utf8').split(/\r?\n/).map(l => l.replace(/#.*$/, '').trim()).filter(Boolean);
}

/// the venue ids a track file makes, in the order the game appends them: the forward venue, then its
/// reverse twin when the file asks for one (venue.reverse = 1)
export function venueIdsOf(t) {
  const v = t.venue || {};
  const ids = [v.id];
  if (v.reverse) ids.push(v.id + 'Rev');
  return ids;
}
