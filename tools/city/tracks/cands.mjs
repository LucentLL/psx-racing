// cands.mjs - the candidate courses: waypoints on the shipped graph
//   J(a, b, lat, lon)        the junction of two named streets nearest the hint
//   P(lat, lon, name)        the nearest node of a named street
//   E(map, x, y, name, hdg)  the edge of a named street nearest a pixel of a district map, driven heading hdg (compass)
import { junction, nodeNear, edgeNear } from './lib.mjs';
const J = (a, b, lat, lon, maxD = 1500) => () => junction(a, b, lat, lon, maxD);
const P = (lat, lon, name, maxD = 800) => () => nodeNear(lat, lon, name, maxD);
const MAPS = {   // the district maps render.py drew (1400 px square): centre lat, lon, span m
  mp: [35.1960, -80.8330, 3200], bal: [35.0560, -80.8480, 3500], se: [35.2110, -80.8580, 3000],
  noda: [35.2420, -80.8070, 3200], pm: [35.2200, -80.8170, 3000], un: [35.3000, -80.7400, 6000],
  ap: [35.2200, -80.9050, 6000], sp: [35.1520, -80.8300, 3000], up: [35.2265, -80.8430, 2400],
  el: [35.2090, -80.8200, 3000],
};
export function pxLL(map, x, y) {
  const [lat0, lon0, span] = MAPS[map];
  const m = span / 1400;
  const dx = (x - 700) * m, dz = (700 - y) * m;
  return [lat0 + dz / 111132, lon0 + dx / (111320 * Math.cos(35.18456 * Math.PI / 180))];
}
const E = (map, x, y, name, hdg, maxD = 600) => () => { const [lat, lon] = pxLL(map, x, y); return edgeNear(lat, lon, name, hdg, maxD); };
const EL = (lat, lon, name, hdg, maxD = 600) => () => edgeNear(lat, lon, name, hdg, maxD);
function ahead(lat, lon, hdg, m) { const r = hdg * Math.PI / 180; return [lat + m * Math.cos(r) / 111132, lon + m * Math.sin(r) / (111320 * Math.cos(35.18456 * Math.PI / 180))]; }

// A FREEWAY DUMBBELL: a lap of one freeway between interchanges A and B, turning round on each
// interchange's own ramps (off, across the road it meets, back on the other way).
function DB(name, A, B, f0 = 0.22, f1 = 0.78, maxD = 400) {
  const at = t => [A[0] + (B[0] - A[0]) * t, A[1] + (B[1] - A[1]) * t];
  const hdg = (Math.atan2((B[1] - A[1]) * 111320 * Math.cos(35.18456 * Math.PI / 180), (B[0] - A[0]) * 111132) * 180 / Math.PI + 360) % 360;
  const back = (hdg + 180) % 360;
  return [EL(...at(0.5), name, hdg, maxD), EL(...at(0.5), name, back, maxD)];
}

const W = (ei, dir = 1) => () => ({ ei, dir });
export const CANDS = [
  // ------------------------------------------------------------ circuits (closed loops, raced over laps)
  { id: 'C1', kind: 'circuit', name: 'UPTOWN STREET CIRCUIT', district: 'Uptown',
    via: [E('up', 600, 800, 'South Tryon Street', 45), E('up', 960, 450, 'North Tryon Street', 45), E('up', 1150, 545, 'East 9th Street', 135),
          E('up', 1145, 632, 'North Brevard Street', 225), E('up', 795, 930, 'South Brevard Street', 225)],
    fun: 'A tower canyon: Tryon Street flat out between the skyscrapers, then square corners onto the one-way grid.' },
  { id: 'C2', kind: 'circuit', name: 'SOUTH END CIRCUIT', district: 'South End',
    via: [E('se', 820, 445, 'South Tryon Street', 225), E('se', 330, 800, 'South Tryon Street', 200), E('se', 330, 1070, 'Remount Road', 120),
          E('se', 600, 930, 'South Boulevard', 30), E('se', 935, 480, 'South Boulevard', 40), E('se', 1000, 290, 'Carson Boulevard', 315)],
    fun: 'Two long parallel blasts (South Tryon out, South Boulevard back) joined by Remount and Carson, across the light-rail line.' },
  { id: 'C3', kind: 'circuit', name: 'NODA CIRCUIT', district: 'NoDa',
    via: [E('noda', 470, 310, 'North Tryon Street', 45), E('noda', 760, 330, 'East 36th Street', 150), E('noda', 1000, 640, 'East 36th Street', 135),
          E('noda', 1060, 805, 'The Plaza', 220), E('noda', 800, 750, 'Matheson Avenue', 290), E('noda', 470, 500, 'Matheson Avenue', 330)],
    fun: 'The arts district: down 36th Street through the NoDa crossing, The Plaza, then the Matheson bridge over the tracks.' },
  { id: 'C4', kind: 'circuit', name: 'PLAZA MIDWOOD CIRCUIT', district: 'Plaza Midwood',
    via: [E('pm', 800, 670, 'Central Avenue', 95), E('pm', 1020, 450, 'The Plaza', 8), E('pm', 1010, 60, 'Parkwood Avenue', 260),
          E('pm', 900, 270, 'Hawthorne Lane', 200), E('pm', 660, 560, 'Hawthorne Lane', 210)],
    fun: 'Central Avenue strip, the long run up The Plaza under the trees, back down Hawthorne.' },
  { id: 'C5', kind: 'circuit', name: 'MYERS PARK - QUEENS ROAD', district: 'Dilworth / Myers Park',
    via: [E('mp', 690, 680, 'Queens Road West', 230), E('mp', 545, 830, 'Queens Road West', 180), EL(35.1854, -80.8402, 'Queens Road West', 180),
          EL(35.1815, -80.8380, 'Queens Road West', 110), EL(35.1831, -80.8324, 'Queens Road East', 45), E('mp', 900, 880, 'Queens Road', 20),
          E('mp', 905, 640, 'Queens Road', 320)],
    fun: 'The Queens Road loop under the willow-oak canopy: one flowing bend after another, no straight longer than a block.' },
  { id: 'C6', kind: 'circuit', name: 'ELIZABETH CIRCUIT', district: 'Elizabeth',
    via: [E('el', 600, 325, 'East 7th Street', 135), E('el', 930, 630, 'East 7th Street', 135), E('el', 700, 800, 'Randolph Road', 315), E('el', 390, 360, 'Charlottetowne Avenue', 35)],
    fun: '7th Street and Randolph Road between the hospital and the old houses.' },
  { id: 'C7', kind: 'circuit', name: 'UNIVERSITY CIRCUIT', district: 'University City',
    via: [E('un', 450, 740, 'North Tryon Street', 15), E('un', 530, 570, 'East W T Harris Boulevard', 150),
          E('un', 520, 880, 'University City Boulevard', 240)],
    fun: 'Wide divided boulevards: three fast straights and big sweepers round the campus.' },
  { id: 'C8', kind: 'circuit', name: 'AIRPORT INDUSTRIAL CIRCUIT', district: 'Airport / West Blvd',
    via: [E('ap', 305, 620, 'Billy Graham Parkway', 0), E('ap', 450, 445, 'Wilkinson Boulevard', 115), E('ap', 520, 690, 'Morris Field Drive', 225)],
    fun: 'Billy Graham Parkway expressway, warehouses and the runway approach lights.' },
  { id: 'C9', kind: 'circuit', name: 'BALLANTYNE CIRCUIT', district: 'Ballantyne',
    via: [E('bal', 720, 735, 'Ballantyne Commons Parkway', 85), E('bal', 1000, 760, 'Ballantyne Commons Parkway', 110), E('bal', 1110, 600, 'North Community House Road', 350),
          E('bal', 900, 480, 'Upper Avenue', 240), E('bal', 480, 470, 'Ballantyne Corporate Place', 260), E('bal', 358, 630, 'Ballantyne Corporate Place', 180),
          E('bal', 450, 720, 'Ballantyne Commons Parkway', 100)],
    fun: 'Office-park parkway curves past the lakes, a figure through both halves of Ballantyne Corporate Place.' },
  { id: 'C10', kind: 'circuit', name: 'SOUTHPARK CIRCUIT', district: 'SouthPark',
    via: [J('Sharon Road', 'Fairview Road', 35.1500, -80.8330), J('Fairview Road', 'Barclay Downs Drive', 35.1480, -80.8380),
          P(35.1560, -80.8360, 'Barclay Downs Drive'), J('Sharon Road', 'Carnegie Boulevard', 35.1560, -80.8300)],
    fun: 'Round the mall: Sharon, Fairview, Barclay Downs and Carnegie.' },
  // ------------------------------------------------------------ sprints (point to point, 2-5 km)
  { id: 'S1', kind: 'sprint', name: 'PROVIDENCE ROAD SPRINT', district: 'Elizabeth > Myers Park', leadM: 60, shutM: 250,
    via: [E('el', 520, 800, 'Providence Road', 180), P(35.1650, -80.8000, 'Providence Road', 2500)], totalM: 60 + 4500 + 250,
    fun: 'Out of Elizabeth past the Myers Park mansions: four lanes that bend and roll all the way down to the creek.' },
  { id: 'S2', kind: 'sprint', name: 'THE PLAZA SPRINT', district: 'Plaza Midwood > NoDa', leadM: 60, shutM: 250,
    via: [E('pm', 1040, 300, 'The Plaza', 8), J('The Plaza', 'East Sugar Creek Road', 35.2520, -80.7750, 3000)], cut: { from: 820 },
    fun: 'Up The Plaza from Parkwood: bungalows and big oaks, a dip and a climb over the crest at 36th, flat out to Sugar Creek.' },
  { id: 'S3', kind: 'sprint', name: 'BILLY GRAHAM PARKWAY SPRINT', district: 'Airport / West Blvd', leadM: 60, shutM: 300,
    via: [E('ap', 430, 1330, 'Billy Graham Parkway', 330), E('ap', 300, 450, 'Billy Graham Parkway', 0)],
    fun: 'The expressway past the airport fence: long fast sweepers on a divided four-lane, top gear most of the way.' },
  { id: 'S4', kind: 'sprint', name: 'W.T. HARRIS SPRINT', district: 'University City', leadM: 60, shutM: 300,
    via: [E('un', 520, 580, 'East W T Harris Boulevard', 150), E('un', 1035, 1200, 'East W T Harris Boulevard', 175)],
    fun: 'University boulevard: a wide divided road in long sweeping bends through the woods.' },
  { id: 'S5', kind: 'sprint', name: 'PARK ROAD SPRINT', district: 'Dilworth > Park Road', leadM: 60, shutM: 250,
    via: [P(35.2000, -80.8530, 'Park Road', 1500), J('Park Road', 'East Woodlawn Road', 35.1700, -80.8520, 2500)],
    fun: 'Dilworth to the Park Road shops: a two-lane through Freedom Park country, up and down the creek valleys.' },
  { id: 'S6', kind: 'sprint', name: 'I-77 SOUTH SPRINT', district: 'Uptown > South', leadM: 60, shutM: 400,
    via: [EL(35.2300, -80.8650, 'I-77', 200, 900), EL(35.1990, -80.8815, 'I-77', 210, 900)],
    fun: 'A freeway blast out of uptown: from the I-277 split south on four lanes, the skyline in the mirrors.' },
  // ------------------------------------------------------------ shorter cuts of today's sprints
  { id: 'X1', kind: 'sprint', name: 'TRYON SPRINT: SOUTH END TO UPTOWN', district: 'South End > Uptown', leadM: 60, shutM: 250, cutOf: 'TryonSprint',
    route: 'tryon', cut: { from: 0, to: { lat: 35.2338, lon: -80.8352, d: 250 } },
    fun: "The first half of today's Tryon Sprint: Camden Road, through the Square between the towers, finish under I-277." },
  { id: 'X2', kind: 'sprint', name: 'TRYON SPRINT: UPTOWN TO NODA', district: 'Uptown > NoDa', leadM: 60, shutM: 250, cutOf: 'TryonSprint',
    route: 'tryon', cut: { from: { lat: 35.2338, lon: -80.8352, d: -60 }, to: null },
    fun: 'The second half: from under I-277 north past the mills and the rail yards to 36th Street.' },
  { id: 'X3', kind: 'sprint', name: 'INDEPENDENCE: THE BELK TO BRIAR CREEK', district: 'Uptown > East', leadM: 60, shutM: 300, cutOf: 'IndependenceSprint',
    route: 'independence', cut: { from: 0, to: { lat: 35.2084, lon: -80.7959, d: 300 } },
    fun: 'The first half of US 74: off the Belk and down the expressway between the walls to Briar Creek.' },
  { id: 'X4', kind: 'sprint', name: 'INDEPENDENCE: BRIAR CREEK TO SHARON AMITY', district: 'East', leadM: 60, shutM: 300, cutOf: 'IndependenceSprint',
    route: 'independence', cut: { from: { lat: 35.2084, lon: -80.7959, d: -60 }, to: null },
    fun: 'The second half: the open expressway east, under Eastway and Wendover, to Sharon Amity.' },
  // ------------------------------------------------------------ drags (lead 60 m + the run + shutdown)
  { id: 'D1', kind: 'drag', name: 'UPTOWN 1/4 MILE: 4TH STREET', district: 'Uptown', leadM: 60, runM: 402, shutM: 250, totalM: 60 + 402 + 250,
    cut: { from: { lat: 35.22071, lon: -80.83757, d: 0 } },
    via: [EL(35.22071, -80.83757, 'East 4th Street', 316), () => edgeNear(...ahead(35.22071, -80.83757, 316, 760), 'East 4th Street', 316, 400)],
    fun: 'Four lanes one way, aimed at the towers: the quarter mile ends two blocks short of Tryon, the shutdown runs into the canyon.' },
  { id: 'D2', kind: 'drag', name: 'HARRIS 1/4 MILE', district: 'East (Hickory Grove)', leadM: 60, runM: 402, shutM: 250, totalM: 60 + 402 + 250,
    cut: { from: { lat: 35.24750, lon: -80.73743, d: 0 } },
    via: [EL(35.24750, -80.73743, 'East W T Harris Boulevard', 184), () => edgeNear(...ahead(35.24750, -80.73743, 184, 760), 'East W T Harris Boulevard', 184, 400)],
    fun: 'Three lanes a side on W.T. Harris, dead straight and dead flat.' },
  { id: 'D3', kind: 'drag', name: 'US 74 1/2 MILE: INDEPENDENCE', district: 'East', leadM: 60, runM: 804, shutM: 300, totalM: 60 + 804 + 300,
    cut: { from: { lat: 35.18469, lon: -80.75716, d: 0 } },
    via: [EL(35.18469, -80.75716, 'East Independence Expressway', 324), () => edgeNear(...ahead(35.18469, -80.75716, 324, 1200), 'East Independence Expressway', 324, 500)],
    fun: 'Four lanes of expressway inbound toward the skyline, a half mile with nothing in the way.' },
  { id: 'D4', kind: 'drag', name: 'WILKINSON 1/2 MILE', district: 'West (Wilkinson Blvd)', leadM: 60, runM: 804, shutM: 300, totalM: 60 + 804 + 300,
    cut: { from: { lat: 35.22432, lon: -80.89386, d: 0 } },
    via: [EL(35.22432, -80.89386, 'Wilkinson Boulevard', 92), () => edgeNear(...ahead(35.22432, -80.89386, 92, 1200), 'Wilkinson Boulevard', 92, 500)],
    fun: 'The old airport road: three lanes east toward uptown past the warehouses and motels.' },
  // ------------------------------------------------------------ freeway-and-ramp circuits (owner, 14:00)
  // One waypoint per carriageway arm; the router (freeways and ramps preferred) finds each turnaround on the
  // interchange's own ramps. The arm waypoints are the edges tools armsearch.mjs found give the shortest legal lap
  // (graph b2aa3453).
  { id: 'H1', kind: 'circuit', name: 'I-485 / I-85 RAMPS (YOUR LOOP)', district: 'West (I-485 x I-85)', fwy: true,
    via: [W(2514), W(2535), W(9949)],
    fun: 'Your loop: up I-485, round at Moores Chapel Road, back down and through the cloverleaf onto I-85, east to Little Rock Road, round and back.' },
  { id: 'H2', kind: 'circuit', name: 'I-485 WEST RAMPS: WILKINSON TO MOORES CHAPEL', district: 'West (I-485)', fwy: true,
    via: [W(2509), W(2526)],
    fun: 'The same I-485 north arm, run on through the I-85 cloverleaf to Wilkinson Boulevard: round on the ramps at both ends, clear of the airport ramps.' },
  { id: 'H3', kind: 'circuit', name: 'I-77 SOUTH RAMPS: REMOUNT TO CLANTON', district: 'South (I-77)', fwy: true,
    via: [W(6686), W(890)],
    fun: 'I-77 south out of the city: round at Clanton Road, back north and round at Remount Road. Short and fast.' },
  { id: 'H4', kind: 'circuit', name: 'I-277 BELK FREEWAY RAMPS', district: 'Uptown (I-277)', fwy: true,
    via: [W(1831), W(2420)],
    fun: 'The Belk Freeway under the skyline between the South Boulevard ramps and the I-77 junction, turning round on the ramps at both ends.' },
  { id: 'H5', kind: 'circuit', name: 'I-485 BALLANTYNE RAMPS: JOHNSTON TO REA', district: 'Ballantyne (I-485)', fwy: true,
    via: [W(10713), W(13317)],
    fun: 'The outer belt between Johnston Road and Rea Road: wide and fast, round on the ramps at each end.' },
  { id: 'H6', kind: 'circuit', name: 'US 74 RAMPS: WENDOVER TO SHARON AMITY', district: 'East (US 74)', fwy: true,
    via: [W(13595), W(2298)],
    fun: 'The Independence Expressway between Wendover and Sharon Amity, round on the interchange ramps and frontage streets.' },
  { id: 'H7', kind: 'circuit', name: 'I-85 UNIVERSITY RAMPS', district: 'University (I-85)', fwy: true,
    via: [W(3102), W(3093)],
    fun: 'I-85 between University City Boulevard and W.T. Harris: six lanes and a cloverleaf at the top.' },
];
