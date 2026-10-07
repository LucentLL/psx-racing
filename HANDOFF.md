# HANDOFF - branch tyre-load (from main 56f32403), 2026-10-07

Stopped at the context budget. NOT merged, NOT pushed.

## Commits
- 325ecdb9 Factory parts are not tuneable; race/rally springs from a tarmac ride frequency
- (next) Tyre load sensitivity + anti-roll bars feed the tyre loads; AI grip reads the static split

## Tyre load sensitivity (CarController)
- Each grounded wheel's load is read against the MEAN of the grounded wheels (downforce, dips and crests
  cancel); the tyre uses Fz x LoadMuFactor(Fz/mean) for its friction circle AND cornering stiffness.
  LoadMuFactor(r) = clamp(1 - k (r - 1), 1 - 2k, 1 + k), k = TyreLoadSensitivity = 0.08 (8% per doubling).
- TyreLoadGain = 1.05 on the circle only (not the stiffness): the FD's skidpad g is unchanged.
- The anti-roll bar's reaction now goes through the tyres (wheelLoad / wheelContacts.load), so the roll split
  moves grip. Confirmed first: bars pushed the body only, never the tyre load.
- AIGrip.NominalMu takes StaticAxleMuFactor per axle (1.0 on a 50:50 car, so the FD calibration holds).
- Calibration levers in HandlingPlayCheck P: PSX_P_EXP tokens "ls=0.10;lg=1.06;lgc" (lgc = gain on stiffness too).
- k=0.10 put the Stratos at 15.3 deg lane-change slip (spin by P's criterion), 0.15 the A310/Elise too; a 50%
  front roll-share floor helped only ~0.5 deg. k=0.08 keeps all 22 under 15.

## Results (PSXCity sandbox)
- HandlingPlayCheck A-H + S: all ok. P: 22/22 never roll over; lane-change body slip max 14.6 (Stratos).
- FD: 1.33 g -> 1.33 g, balance +1.8 -> +2.0, lane change 7.1/11.3 -> 7.1/10.4, 100-0 brake 100.4 -> 97.8 m.
- Front-heavy: Taurus +1.8 -> +2.4 (1.17 -> 1.10 g), 406 +1.7 -> +2.3, Volvo 240 +2.2 -> +2.9, EG/AE86 lower g.
- Rear-heavy: Elise +2.0 -> +1.8 (1.47 -> 1.52 g), A310 +1.6 -> +1.6, RUF +1.9 -> +1.8, Stratos +1.8 -> +1.7.
  The slip-difference metric moves little: the front saturates first on every car (rear mu stagger 1.05).
- P lane-change "off line" FAILs: 9 (main 8); RUF is new (14.1 -> 22.1 deg); body slip up 1.5-2.5 deg on
  rear-heavy cars (Elise 10.5 -> 12.9, A310 11.9 -> 14.1, Stratos 12.5 -> 14.6).

## NOT RUN (budget stop) - do these next
1. tools\sandbox-selftest.ps1 (PSXCity already holds this branch): factory-parts pins + car/tuning checks.
2. tools\aigrip-play-check.ps1 -Cars "RX-7 Type RS;CIVIC SiR-II (EG);VIPER GTS;SKYLINE GT-R (R32);Acura NSX;
   LEVIN GT-APEX (AE86);Taurus SHO;Elise Sport 190" - measured/predicted should stay ~1.00-1.03.
3. race-play-check -Venues "GillespieGap,ChimneyRockRev" -Seconds 600 -Finish -Traffic NONE, seeds 0,1.
   race-play-check does NOT copy rg2_cars.json - the sandbox has it from the handling run.

## Factory parts (owner rule) - see commit 325ecdb9
- Road car: only BOUGHT parts open rows; factory LSD works at sheet figures, row reads NEEDS ADJUSTABLE LSD,
  shop sells the adjustable LSD over it. IsBuiltToTune = race | rally | raceSusp (touring, JGTC, 155 TI,
  Escudo): all rows open, slider-only parts refused (ALREADY ADJUSTABLE).
- Owner question: rally/touring cars can still buy STAGES, the weld and the blower (only IsRaceCar is
  "already built"); the owner said rally cars "can't receive different parts".
- Spring sliders span 0.70x..1.45x of the default: the top passes the sheet's 20 kgf/mm on Lister Storm
  (F 19.2, R 15.7), Calsonic GT-R F 16.8, JGTC GT-R F 16.0, Panoz F 15.3, Corvette C2 R 14.7.
