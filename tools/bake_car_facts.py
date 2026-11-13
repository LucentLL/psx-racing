"""
Bake Resources/car_facts.json: the catalog FACTS of every car (round D of the
fault-system design, art workspace Fault_System_Design_2026-11-11.md, section 2
"Catalog facts", 3.5 and the D row of section 10). Not saved data: the game
reads it through Scripts/CarFacts.cs. Nothing consumes it yet except the
self-test validator and the career market's race/rally filter (owner's answer
9, 2026-11-12: race and rally cars leave the newspaper).

SOURCES. The catalog (Resources/rg2_cars.json: id, name, origin, model year,
engine type, aspiration, displacement, race/rally/raceSusp rows) plus the
tables below, authored from general automotive knowledge (no web lookups).
Every value we are not sure of is listed in the row's `lowConf` field; the
owner spot-checks 30 rows (Car_Facts_SpotCheck_2026-11-12.md).

RULES (derived, not authored per row):
  family       jpn / usa / eur: the MAKER's home (catalog origin "eur" is RG2's
               catch-all for small makers; the six Japanese and American ones
               are corrected and say so in `note`)
  builtToTune  catalog rule (CarSpec.IsBuiltToTune): "Race Car" or "Rally" in
               the name, or raceSusp > 0
  market       authored: US, JDM, EUR, UK; RACE for every built-to-tune row
  roadLegal    false on every built-to-tune row; true elsewhere unless authored
  serviceUnit  "hours" when not road legal (race hours), else "miles"
  obd          2 (OBD-II) ONLY for a US-market, road-legal, 1996+ EFI row - and
               every such row has it (federal rule for model year 1996);
               1 = the maker's own code system (OBD-I / JDM / European);
               0 = no codes (carburettor, mechanical injection, early analog
               EFI, race ECU)
  eSys         CARB, MECH, EFI-OBD1, EFI-OBD2, EFI-JDM, EFI-RACE
  bumperEra    chrome (a metal bar) before 1973; US market 1973-82 "5mph";
               US 1983+ "2.5mph"; others plastic from 1981 (chrome 1973-80),
               with authored exceptions
  ac           none / R-12 / R-134a by market and year (switch-over years
               marked low confidence)
  partsWait    by parts source: us_local 1, us_dealer 1-3, us_classic 2-7,
               jdm_shared 3-7, jdm_only 7-21, eur_dealer 2-5, eur_special
               5-21, classic 7-21, race 14-28 (days)
  priceClass   economy 1.0, sport 1.15, exotic 2.5, race 3.0 (not road legal)

    py tools/bake_car_facts.py --check   # validate, write nothing
    py tools/bake_car_facts.py           # write Resources/car_facts.json

Idempotent; the output depends only on the catalog and these tables.
"""
import io
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CATALOG = os.path.join(ROOT, "Assets", "PSXRacing", "Resources", "rg2_cars.json")
OUT = os.path.join(ROOT, "Assets", "PSXRacing", "Resources", "car_facts.json")
FORMAT = 1

# --------------------------------------------------------------------------
# ENGINES: code -> (camDrive, beltMi, interference, ignition, fuel,
#                   stockInternals, stockBoostBar, knownIssues, lowConf)
#   camDrive      belt / chain / gear / none (rotary: no valves)
#   interference  yes / no / unknown / n/a
#   ignition      points / dist (electronic distributor) / dis (no distributor)
#   fuel          carb / mech (mechanical injection: K-Jetronic, Kugelfischer,
#                 Bosch direct, Hilborn) / efi
#   stockInternals  highest power stage (0-3; 4 = BUILT ENGINE) the stock
#                 bottom end tolerates on the car's own path
#   stockBoostBar boost the stock bottom end tolerates, bar
# --------------------------------------------------------------------------
E = {}


def eng(code, cam, belt, interf, ign, fuel, si, boost, issues="", lo=""):
    E[code] = dict(cam=cam, belt=belt, interf=interf, ign=ign, fuel=fuel,
                   si=si, boost=boost, issues=issues, lo=lo)


# Honda
eng("EM CVCC 1.5", "belt", 60000, "no", "points", "carb", 1, 0.3, "", "engineCode,interference,ignition")
eng("EW 1.5 PGM-FI", "belt", 60000, "no", "dist", "efi", 1, 0.4, "", "interference")
eng("ER 1.2 turbo", "belt", 60000, "no", "dist", "efi", 1, 0.85, "", "interference")
eng("H1300 air-cooled", "chain", 0, "unknown", "points", "carb", 1, 0.3, "", "camDrive")
eng("A20A3", "belt", 60000, "unknown", "dist", "efi", 1, 0.4, "", "interference")
eng("B16A", "belt", 60000, "yes", "dist", "efi", 2, 0.5, "VTEC oil-pressure switch / solenoid gasket leak")
eng("B16B", "belt", 60000, "yes", "dist", "efi", 2, 0.4)
eng("B18C", "belt", 60000, "yes", "dist", "efi", 2, 0.4)
eng("H22A", "belt", 60000, "yes", "dist", "efi", 2, 0.5, "oil consumption (FRM sleeves)", "stockBoostBar")
eng("F20C", "chain", 0, "yes", "dis", "efi", 2, 0.4)
eng("C30A", "belt", 90000, "yes", "dist", "efi", 2, 0.5)
eng("C32B", "belt", 90000, "yes", "dist", "efi", 2, 0.5)
eng("J32A2", "belt", 105000, "yes", "dis", "efi", 2, 0.5, "automatic gearbox failures (5-speed auto)", "engineCode")
eng("ZC Mugen race", "belt", 0, "yes", "dist", "efi", 3, 0.0)
eng("B16B race", "belt", 0, "yes", "dist", "efi", 3, 0.0)
eng("AS800E RSC", "chain", 0, "yes", "points", "carb", 3, 0.0, "", "camDrive")
# Toyota / Lexus
eng("2T-G", "chain", 0, "unknown", "points", "carb", 1, 0.4)
eng("3M", "chain", 0, "unknown", "points", "carb", 1, 0.3)
eng("4A-GE 16v", "belt", 60000, "no", "dist", "efi", 2, 0.5, "", "interference")
eng("4A-GZE", "belt", 60000, "no", "dist", "efi", 2, 0.8, "", "interference")
eng("4A-GE 20v", "belt", 60000, "yes", "dist", "efi", 2, 0.4)
eng("4E-FTE", "belt", 60000, "no", "dist", "efi", 1, 0.8, "", "interference")
eng("5E-FHE", "belt", 60000, "no", "dist", "efi", 1, 0.4, "", "interference")
eng("3S-FE", "belt", 60000, "no", "dist", "efi", 2, 0.5)
eng("3S-GE gen1", "belt", 60000, "no", "dist", "efi", 2, 0.5, "", "interference")
eng("3S-GE gen2", "belt", 60000, "unknown", "dist", "efi", 2, 0.5)
eng("3S-GE gen3", "belt", 60000, "yes", "dist", "efi", 2, 0.5, "", "interference")
eng("3S-GE BEAMS", "belt", 60000, "yes", "dis", "efi", 2, 0.5)
eng("3S-GTE gen1", "belt", 60000, "no", "dist", "efi", 2, 0.9, "", "interference")
eng("3S-GTE gen2", "belt", 60000, "unknown", "dist", "efi", 2, 1.0, "intercooler/turbo coolant lines")
eng("3S-GTE gen3", "belt", 60000, "yes", "dist", "efi", 2, 1.0, "", "interference,ignition")
eng("2ZZ-GE", "chain", 0, "yes", "dis", "efi", 2, 0.4, "lift-bolt failure on early engines, oil consumption")
eng("1ZZ-FE", "chain", 0, "unknown", "dis", "efi", 1, 0.4, "oil consumption (piston rings)")
eng("5M-GEU", "belt", 60000, "no", "dist", "efi", 2, 0.5, "", "interference")
eng("7M-GTEU", "belt", 60000, "no", "dis", "efi", 2, 1.0, "head gasket (head-bolt stretch)")
eng("1JZ-GTE", "belt", 60000, "no", "dis", "efi", 3, 1.2, "ceramic turbine wheels shed at high boost")
eng("1JZ-GTE VVT-i", "belt", 60000, "unknown", "dis", "efi", 3, 1.2)
eng("2JZ-GTE", "belt", 60000, "no", "dis", "efi", 3, 1.5, "sequential-turbo vacuum lines")
eng("2JZ-GTE VVT-i", "belt", 60000, "no", "dis", "efi", 3, 1.5, "sequential-turbo vacuum lines", "interference")
eng("2JZ-GE VVT-i", "belt", 60000, "unknown", "dis", "efi", 3, 0.7)
eng("1G-FE BEAMS", "belt", 60000, "unknown", "dis", "efi", 2, 0.4)
eng("R36V race", "gear", 0, "yes", "dis", "efi", 3, 0.0, "", "camDrive")
eng("R32V race", "gear", 0, "yes", "dis", "efi", 3, 0.0, "", "camDrive")
eng("Toyota 7 5.0 TT race", "gear", 0, "yes", "points", "mech", 3, 0.0, "", "camDrive,fuel,engineCode")
eng("3S-GTE rally", "belt", 0, "yes", "dis", "efi", 3, 0.0)
# Nissan / NISMO / Prince
eng("G7", "chain", 0, "unknown", "points", "carb", 1, 0.3)
eng("U20", "chain", 0, "unknown", "points", "carb", 1, 0.3)
eng("S20", "chain", 0, "yes", "points", "carb", 1, 0.3, "", "interference")
eng("L16", "chain", 0, "no", "points", "carb", 2, 0.4)
eng("L24", "chain", 0, "no", "points", "carb", 2, 0.4)
eng("L28", "chain", 0, "no", "dist", "carb", 2, 0.5, "", "fuel")
eng("L28E", "chain", 0, "no", "dist", "efi", 2, 0.5)
eng("Z18", "chain", 0, "no", "dist", "carb", 1, 0.4, "", "fuel")
eng("FJ20ET", "chain", 0, "yes", "dist", "efi", 2, 0.8, "", "interference")
eng("FJ24", "chain", 0, "yes", "dist", "carb", 2, 0.4, "", "interference,ignition")
eng("CA16DE", "belt", 60000, "yes", "dist", "efi", 1, 0.4, "", "interference")
eng("CA18DE", "belt", 60000, "yes", "dist", "efi", 2, 0.5, "", "interference")
eng("CA18DET", "belt", 60000, "yes", "dist", "efi", 2, 0.8, "", "interference")
eng("SR20DE", "chain", 0, "yes", "dist", "efi", 2, 0.5, "", "ignition")
eng("SR20DET", "chain", 0, "yes", "dis", "efi", 2, 1.0, "valve-lifter tick (hydraulic lash adjusters)")
eng("RB20DET", "belt", 60000, "unknown", "dis", "efi", 2, 0.8)
eng("RB25DE", "belt", 60000, "yes", "dis", "efi", 2, 0.6)
eng("RB25DET", "belt", 60000, "yes", "dis", "efi", 2, 1.0)
eng("RB26DETT", "belt", 60000, "yes", "dis", "efi", 3, 1.0, "oil-pump drive collar at high rpm, ceramic turbine wheels")
eng("RB-X GT2 2.8", "belt", 60000, "yes", "dis", "efi", 3, 1.2, "", "engineCode")
eng("RB26 race", "belt", 0, "yes", "dis", "efi", 3, 0.0)
eng("VG30ET", "belt", 60000, "no", "dist", "efi", 2, 0.7)
eng("VG30DETT", "belt", 60000, "yes", "dis", "efi", 2, 1.0, "plastic intake plenum / vacuum lines in a crowded bay")
eng("VRH35L", "chain", 0, "yes", "dis", "efi", 3, 1.2, "", "camDrive")
eng("VRH35 race", "gear", 0, "yes", "dis", "efi", 3, 0.0, "", "camDrive")
# Mazda
eng("10A rotary", "none", 0, "n/a", "points", "carb", 1, 0.4, "apex seals")
eng("13B-T", "none", 0, "n/a", "dist", "efi", 1, 0.6, "apex seals, coolant seals")
eng("13B-REW", "none", 0, "n/a", "dis", "efi", 1, 0.8, "apex seals, sequential-turbo vacuum lines, heat")
eng("R26B race", "none", 0, "n/a", "dis", "efi", 3, 0.0)
eng("B6-ZE", "belt", 60000, "no", "dis", "efi", 2, 0.5, "short-nose crankshaft (early 1.6)")
eng("BP-ZE", "belt", 60000, "no", "dis", "efi", 2, 0.5)
eng("B5-ME", "belt", 60000, "unknown", "dist", "efi", 1, 0.4, "", "engineCode")
eng("KF-ZE", "belt", 60000, "unknown", "dist", "efi", 1, 0.4)
# Mitsubishi
eng("4G32 SOHC", "chain", 0, "unknown", "points", "carb", 1, 0.3, "", "camDrive")
eng("4G32 DOHC", "chain", 0, "unknown", "points", "carb", 1, 0.3, "", "camDrive")
eng("G62B turbo", "belt", 60000, "no", "dist", "efi", 1, 0.6, "", "interference")
eng("4G63T", "belt", 60000, "yes", "dist", "efi", 2, 1.2, "balance-shaft belt, crank walk (later engines)")
eng("4G63T coil", "belt", 60000, "yes", "dis", "efi", 2, 1.2, "balance-shaft belt")
eng("4G63T rally", "belt", 0, "yes", "dis", "efi", 3, 0.0)
eng("4G92 MIVEC", "belt", 60000, "yes", "dist", "efi", 1, 0.4, "", "interference,ignition")
eng("6A12", "belt", 60000, "unknown", "dist", "efi", 1, 0.5, "", "ignition")
eng("6A12 MIVEC", "belt", 60000, "unknown", "dist", "efi", 1, 0.4, "", "ignition")
eng("6A13TT", "belt", 60000, "unknown", "dis", "efi", 2, 1.0, "", "ignition")
eng("6G72 DOHC", "belt", 60000, "yes", "dis", "efi", 2, 0.5, "lifter tick")
eng("6G72TT", "belt", 60000, "yes", "dis", "efi", 2, 1.0, "lifter tick, rear turbo oil lines")
eng("4G54 turbo rally", "chain", 0, "unknown", "dist", "efi", 3, 0.0, "", "fuel,engineCode")
eng("4G63T Starion rally", "belt", 0, "yes", "dist", "efi", 3, 0.0, "", "engineCode")
eng("FTO JTCC race", "belt", 0, "yes", "dis", "efi", 3, 0.0, "", "engineCode")
# Subaru
eng("EJ20 turbo", "belt", 60000, "yes", "dis", "efi", 1, 1.1, "ringland failure under detonation, head gaskets", "interference")
eng("EJ20 twin seq", "belt", 60000, "yes", "dis", "efi", 1, 0.9, "sequential-turbo control solenoids", "interference")
eng("EJ22 22B", "belt", 60000, "yes", "dis", "efi", 2, 1.1, "", "interference")
eng("EJ20 WRC", "belt", 0, "yes", "dis", "efi", 3, 0.0)
# Isuzu / Suzuki
eng("G161W", "chain", 0, "unknown", "points", "carb", 1, 0.3, "", "camDrive")
eng("G200WE", "belt", 60000, "unknown", "dist", "efi", 1, 0.4, "", "camDrive,engineCode")
eng("H25A TT Pikes", "chain", 0, "yes", "dis", "efi", 3, 0.0, "", "engineCode")
# GM / Ford / Chrysler (US)
eng("Chevy 235 Blue Flame I6", "gear", 0, "no", "points", "carb", 1, 0.3, "fibre cam gear")
eng("Buick 215 V8", "chain", 0, "no", "points", "carb", 1, 0.4)
eng("Pontiac 389", "chain", 0, "no", "points", "carb", 2, 0.5)
eng("SBC 327", "chain", 0, "no", "points", "carb", 2, 0.5)
eng("SBC 327 FI", "chain", 0, "no", "points", "mech", 2, 0.5)
eng("SBC 302 DZ", "chain", 0, "no", "points", "carb", 2, 0.5)
eng("SBC 350 L46", "chain", 0, "no", "points", "carb", 2, 0.5)
eng("SBC 350 L48", "chain", 0, "no", "points", "carb", 2, 0.5)
eng("SBC 350 L98 TPI", "chain", 0, "no", "dist", "efi", 2, 0.5)
eng("LT1", "chain", 0, "no", "dist", "efi", 2, 0.5, "Opti-Spark distributor gets wet (water-pump leak)")
eng("LT4", "chain", 0, "no", "dist", "efi", 2, 0.5, "Opti-Spark distributor gets wet (water-pump leak)")
eng("LT5", "chain", 0, "unknown", "dis", "efi", 2, 0.5)
eng("LS6", "chain", 0, "unknown", "dis", "efi", 3, 0.6, "valve springs / oil consumption (LS6)")
eng("BBC 454 LS6", "chain", 0, "no", "points", "carb", 2, 0.5)
eng("Buick 3.8 SFI turbo", "chain", 0, "no", "dis", "efi", 2, 1.1, "turbo oil lines, head gaskets at high boost")
eng("Chrysler 440", "chain", 0, "no", "points", "carb", 2, 0.5)
eng("Chrysler 440 Six Pack", "chain", 0, "no", "points", "carb", 2, 0.5)
eng("Chrysler 426 Hemi", "chain", 0, "no", "points", "carb", 2, 0.5)
eng("Chrysler 420A", "belt", 60000, "yes", "dis", "efi", 1, 0.4, "", "interference")
eng("Viper V10 8.0", "chain", 0, "no", "dis", "efi", 2, 0.5)
eng("Ford 289 HiPo", "chain", 0, "no", "points", "carb", 2, 0.5)
eng("Ford 302 GT40", "chain", 0, "no", "points", "carb", 3, 0.0)
eng("Ford 390 FE", "chain", 0, "no", "points", "carb", 2, 0.5)
eng("Ford 427 FE", "chain", 0, "no", "points", "carb", 2, 0.5)
eng("Ford 3.4 SHO V8", "chain", 0, "yes", "dis", "efi", 1, 0.4, "cam sprocket slips on its shaft (valves hit pistons)")
eng("Cosworth BDT 1.8T", "belt", 36000, "yes", "dist", "efi", 2, 1.2, "", "fuel,ignition,beltMi")
eng("Cosworth BDT-E 2.1T", "belt", 0, "yes", "dist", "efi", 3, 0.0)
eng("Ford YB Cosworth WRC", "belt", 0, "yes", "dis", "efi", 3, 0.0)
eng("Ford Zetec WRC", "belt", 0, "yes", "dis", "efi", 3, 0.0)
eng("Chevy 327 alu Hilborn", "chain", 0, "yes", "points", "mech", 3, 0.0, "", "fuel")
eng("Chevy 465 alu Hilborn", "chain", 0, "yes", "points", "mech", 3, 0.0, "", "fuel,engineCode")
eng("Ford 6.0 V8 Panoz", "chain", 0, "yes", "dis", "efi", 3, 0.0, "", "engineCode,camDrive")
# Europe
eng("Renault 807 1.6", "chain", 0, "no", "points", "carb", 1, 0.4, "", "engineCode")
eng("Renault 807 1.6 inj", "chain", 0, "no", "points", "efi", 1, 0.4, "", "engineCode,fuel")
eng("Renault Cleon 1.4 turbo", "chain", 0, "no", "dist", "mech", 1, 0.9, "", "ignition")
eng("Renault Cleon 1.5 turbo rally", "chain", 0, "yes", "dist", "mech", 3, 0.0)
eng("VW EG 1.6", "belt", 60000, "no", "points", "mech", 1, 0.4, "", "ignition")
eng("BMW M10 turbo", "chain", 0, "unknown", "points", "mech", 1, 0.6, "turbo lag / heat soak (first European turbo road car)")
eng("BMW S50B32", "chain", 0, "yes", "dis", "efi", 2, 0.4, "VANOS seals, rod bearings at high mileage")
eng("BMW S70/2 V12", "chain", 0, "yes", "dis", "efi", 3, 0.0)
eng("Audi WR 2.1 turbo", "belt", 60000, "no", "dist", "mech", 2, 1.0, "", "interference")
eng("Audi AGB 2.7T", "belt", 75000, "yes", "dis", "efi", 2, 1.2, "turbo oil-feed coking, coil packs")
eng("PSA XU8T", "belt", 36000, "yes", "dist", "mech", 2, 1.0, "", "interference,beltMi")
eng("PSA XU8T rally", "belt", 0, "yes", "dist", "efi", 3, 0.0)
eng("PSA XU10J4RS", "belt", 72000, "yes", "dis", "efi", 1, 0.4)
eng("PSA XU10J4 kit car", "belt", 0, "yes", "dis", "efi", 3, 0.0)
eng("PSA XU9J4 WRC", "belt", 0, "yes", "dis", "efi", 3, 0.0, "", "engineCode")
eng("PSA ES9J4 3.0 V6", "belt", 75000, "yes", "dis", "efi", 1, 0.4)
eng("Peugeot SA35 V10", "gear", 0, "yes", "dis", "efi", 3, 0.0)
eng("Opel X16XE", "belt", 40000, "yes", "dis", "efi", 1, 0.4, "timing belt snaps early", "beltMi")
eng("Opel KF V6 ITC", "chain", 0, "yes", "dis", "efi", 3, 0.0, "", "camDrive")
eng("GM C36GET", "chain", 0, "unknown", "dis", "efi", 2, 1.0, "", "ignition")
eng("Mercedes M198", "chain", 0, "unknown", "points", "mech", 1, 0.3, "oil dilution from the direct injection")
eng("Mercedes M102 2.5-16", "chain", 0, "yes", "dist", "efi", 2, 0.4, "", "interference")
eng("Mercedes M102 DTM", "chain", 0, "yes", "dist", "efi", 3, 0.0)
eng("Mercedes M166 1.6", "chain", 0, "no", "dis", "efi", 1, 0.4, "", "interference")
eng("Mercedes M111 Kompressor", "chain", 0, "yes", "dis", "efi", 2, 0.7, "", "interference")
eng("Mercedes M113 5.0", "chain", 0, "yes", "dis", "efi", 2, 0.5, "", "interference")
eng("Mercedes M120 6.0 V12", "chain", 0, "yes", "dis", "efi", 2, 0.4, "wiring-harness insulation breaks down", "interference")
eng("Mercedes M119 race", "chain", 0, "yes", "dis", "efi", 3, 0.0)
eng("Alfa twin cam 1.6", "chain", 0, "yes", "points", "carb", 1, 0.3, "", "interference")
eng("Alfa twin cam 1.6 GTA", "chain", 0, "yes", "points", "carb", 1, 0.3, "", "interference")
eng("Alfa Busso 2.5 V6 24v", "belt", 45000, "yes", "dis", "efi", 1, 0.4, "timing-belt tensioner", "beltMi,ignition")
eng("Alfa 2.5 V6 DTM", "chain", 0, "yes", "dis", "efi", 3, 0.0, "", "engineCode,camDrive")
eng("Lancia 2.0 16v turbo", "belt", 36000, "yes", "dist", "efi", 2, 1.0, "", "beltMi,interference")
eng("Lancia 2.0 16v turbo rally", "belt", 0, "yes", "dist", "efi", 3, 0.0)
eng("Lancia S4 twincharged", "belt", 0, "yes", "dist", "efi", 3, 0.0)
eng("Ferrari Dino V6 2.4", "chain", 0, "yes", "points", "carb", 1, 0.3, "", "interference")
eng("Ferrari Dino V6 2.4 rally", "chain", 0, "yes", "points", "carb", 3, 0.0, "", "fuel")
eng("Cizeta V16", "gear", 0, "yes", "dis", "efi", 1, 0.3, "", "camDrive,ignition")
eng("Rover K 1.8 VVC", "belt", 60000, "unknown", "dis", "efi", 1, 0.4, "head gasket")
eng("Rover K 1.8 VHPD", "belt", 60000, "unknown", "dis", "efi", 1, 0.4, "head gasket")
eng("Rover V8 3.9 TVR", "chain", 0, "no", "dist", "efi", 2, 0.5)
eng("Rover V8 5.0 TVR", "chain", 0, "no", "dist", "efi", 2, 0.5)
eng("TVR AJP6 Speed Six", "chain", 0, "yes", "dis", "efi", 1, 0.4, "finger-follower / valve-train wear", "camDrive,ignition")
eng("Lotus-Ford Twin Cam", "chain", 0, "unknown", "points", "carb", 1, 0.3)
eng("Lotus 910 2.2 turbo", "belt", 36000, "yes", "dist", "carb", 2, 1.0, "", "beltMi,interference")
eng("Lotus 918 V8 TT", "belt", 36000, "yes", "dis", "efi", 2, 1.0, "", "beltMi,ignition")
eng("Jaguar XK 3.8", "chain", 0, "yes", "points", "carb", 1, 0.4, "", "interference")
eng("Jaguar AJ-V8 SC", "chain", 0, "yes", "dis", "efi", 2, 0.8, "plastic timing-chain tensioners crack, Nikasil bores")
eng("Jaguar JRV-6 TT", "belt", 30000, "yes", "dis", "efi", 2, 1.0, "", "camDrive,beltMi")
eng("Jaguar V12 7.0 race", "chain", 0, "yes", "dis", "efi", 3, 0.0)
eng("Jaguar V12 Lister", "chain", 0, "yes", "dis", "efi", 3, 0.0)
eng("Aston V8 5.3 twin SC", "chain", 0, "yes", "dist", "efi", 2, 0.7, "", "interference,ignition")
eng("Volvo B230F", "belt", 50000, "no", "dist", "efi", 2, 0.7, "", "interference")
eng("Porsche 930 3.4 turbo RUF", "chain", 0, "yes", "dist", "mech", 2, 1.0, "timing-chain tensioners (early type)")
eng("RUF CTR 3.4 TT", "chain", 0, "yes", "dist", "efi", 2, 1.2, "timing-chain tensioners", "fuel,ignition")
eng("RUF CTR2 3.6 TT", "chain", 0, "yes", "dis", "efi", 2, 1.2, "", "ignition")
eng("Dome L28", "chain", 0, "no", "dist", "carb", 2, 0.5, "", "fuel")

# --------------------------------------------------------------------------
# CARS: one row per catalog id.
#   m   market: US, JDM, EUR, UK (RACE is forced on built-to-tune rows)
#   e   engine code (a key of E)
#   ab  airbags: none / driver / dual / dual+side
#   rb  rear brakes: disc / drum
# Optional: fuel, ign, obdr (obd1Read), bump, ac, turbo, pc (price class
# name), parts (parts source), legal (road legal), lo (low-confidence fields,
# comma list), note.
# --------------------------------------------------------------------------
C = {}


def R(cid, m, e, ab, rb, **kw):
    if cid in C:
        raise SystemExit("duplicate row " + cid)
    if e not in E:
        raise SystemExit("unknown engine " + e + " on " + cid)
    d = dict(m=m, e=e, ab=ab, rb=rb)
    d.update(kw)
    C[cid] = d


NOTE_JDM_US_NAME = ("GT4's North-American name for a JDM car; the catalog's figures are "
                    "the JDM spec, so the market is JDM")

R("honda_civic_1500_3door_cx__79", "JDM", "EM CVCC 1.5", "none", "drum", lo="bumperEra")
R("honda_civic_1500_3door_25i__83", "JDM", "EW 1.5 PGM-FI", "none", "drum", lo="obd1Read")
R("alpine_a110_1600s__73", "EUR", "Renault 807 1.6", "none", "disc", ac="none")
R("alpine_a310_1600ve__73", "EUR", "Renault 807 1.6 inj", "none", "disc", ac="none", bump="plastic",
  lo="bumperEra", note="early analog injection, no self-diagnosis")
R("dome_zero__78", "JDM", "Dome L28", "none", "disc", bump="plastic", ac="none", pc="exotic", parts="classic",
  lo="market,roadLegal,rearBrakes",
  note="a prototype road car that never received Japanese type approval; road legal here as the catalog "
       "treats it (owner's call). Catalog origin 'eur' is RG2's catch-all: the maker (Dome) is Japanese")
R("honda_1300_coupe_9_s__70", "JDM", "H1300 air-cooled", "none", "drum", ac="none")
R("mitsubishi_galant_gto_mr__70", "JDM", "4G32 DOHC", "none", "drum", ac="none")
R("mitsubishi_lancer_1600_gsr__73", "JDM", "4G32 SOHC", "none", "drum", ac="none")
R("nissan_bluebird_hardtop_1800sss__910___79", "JDM", "Z18", "none", "drum", lo="rearBrakes,bumperEra,fuel")
R("nissan_fairlady_z_280z_l_2seater__s130___78", "JDM", "L28E", "none", "drum", lo="rearBrakes,bumperEra",
  note="L-Jetronic-type analog injection, no self-diagnosis")
R("toyota_celica_1600gt__ta22___70", "JDM", "2T-G", "none", "drum", ac="none")
R("volkswagen_golf_i_gti__76", "EUR", "VW EG 1.6", "none", "drum", ac="none", lo="bumperEra",
  note="K-Jetronic mechanical injection")
R("toyota_corolla_levin_gt_apex__ae86___83", "JDM", "4A-GE 16v", "none", "disc")
R("toyota_sprinter_trueno_gt_apex__ae86___83", "JDM", "4A-GE 16v", "none", "disc")
R("honda_ballade_sports_cr_x_1_5i__83", "JDM", "EW 1.5 PGM-FI", "none", "drum", lo="obd1Read")
R("mazda_110s__l10a___67", "JDM", "10A rotary", "none", "drum", ac="none")
R("honda_city_turbo_ii__83", "JDM", "ER 1.2 turbo", "none", "drum", lo="obd1Read")
R("nissan_silvia_q_s__s13___88", "JDM", "CA18DE", "none", "disc")
R("mazda_demio__j___99", "JDM", "B5-ME", "dual", "drum")
R("nissan_silvia_q_s__s13___91", "JDM", "SR20DE", "none", "disc", lo="airbags",
  note="driver SRS was an option on the S13")
R("mazda_mazda_323f__93", "EUR", "KF-ZE", "none", "disc", lo="market,airbags",
  note="the catalog's 170 PS is the JDM Lantis 2.0 V6 figure; the name is the European 323F")
R("isuzu_piazza_xe__81", "JDM", "G200WE", "none", "drum", lo="rearBrakes",
  note="early analog injection, no self-diagnosis")
R("honda_accord_coupe__88", "US", "A20A3", "none", "drum", lo="rearBrakes",
  note="built in Marysville, Ohio, and sold in Japan as an import: a US-market car")
R("nissan_silvia_k_s__s13___88", "JDM", "CA18DET", "none", "disc")
R("nissan_exa_canopy_l_a_version_type_s__n13___88", "JDM", "CA16DE", "none", "drum", lo="rearBrakes")
R("toyota_sera__92", "JDM", "5E-FHE", "none", "drum", lo="airbags")
R("toyota_carina_ed_2_0_x_4ws__89", "JDM", "3S-FE", "none", "disc", lo="rearBrakes")
R("mazda_rx_7_gt_limited__fc__j___85", "JDM", "13B-T", "none", "disc")
R("toyota_mr2_1600_g__86", "JDM", "4A-GE 16v", "none", "disc")
R("toyota_starlet_glanza_v__97", "JDM", "4E-FTE", "dual", "drum", lo="airbags,rearBrakes")
R("buick_special__62", "US", "Buick 215 V8", "none", "drum", ac="none", lo="ac",
  note="the catalog's 7456 cc DOHC / 564 PS figures do not match the real '62 Special (215 cu in "
       "aluminium OHV V8); the facts describe the real car")
R("nissan_silvia_k_s__s13___91", "JDM", "SR20DET", "none", "disc", lo="airbags,ignition")
R("nissan_silvia_q_s_aero__s14___93", "JDM", "SR20DE", "driver", "disc", lo="airbags,ac")
R("honda_cr_x_sir__90", "JDM", "B16A", "none", "disc")
R("lotus_europa_special__71", "UK", "Lotus-Ford Twin Cam", "none", "drum", ac="none")
R("mitsubishi_lancer_ex_1800gsr_ic_turbo__83", "JDM", "G62B turbo", "none", "drum", lo="obd1Read,rearBrakes")
R("mazda_mx_5_miata__na___89", "US", "B6-ZE", "driver", "disc",
  note="the US Miata was sold as a 1990 model with a driver airbag; the catalog dates it '89")
R("nissan_silvia_q_s_aero__s14___96", "JDM", "SR20DE", "driver", "disc", lo="airbags,ignition")
R("renault_5_turbo__80", "EUR", "Renault Cleon 1.4 turbo", "none", "disc", bump="plastic", ac="none",
  pc="exotic", note="K-Jetronic mechanical injection; a homologation special")
R("toyota_mr2_1600_g_limited_super_charger__86", "JDM", "4A-GZE", "none", "disc")
R("mazda_mx_5_miata_j_limited__na__j___91", "JDM", "B6-ZE", "none", "disc", lo="airbags")
R("honda_civic_sir_ii__eg___91", "JDM", "B16A", "none", "disc", lo="airbags",
  note="driver SRS was an option")
R("toyota_celica_xx_2800gt__81", "JDM", "5M-GEU", "none", "disc", lo="obd1Read,bumperEra",
  note="1981 EFI without self-diagnosis")
R("audi_quattro__82", "EUR", "Audi WR 2.1 turbo", "none", "disc", lo="bumperEra",
  note="K-Jetronic mechanical injection")
R("honda_civic_sir_ii__eg___92", "JDM", "B16A", "none", "disc", lo="airbags")
R("mazda_mx_5_miata_j_limited_ii__na__j___93", "JDM", "BP-ZE", "driver", "disc", lo="airbags,ac")
R("toyota_celica_2000gt_r__st162___86", "JDM", "3S-GE gen1", "none", "disc")
R("honda_civic_sir_ii__eg___93", "JDM", "B16A", "driver", "disc", lo="airbags,ac")
R("honda_cr_x_del_sol_sir__92", "JDM", "B16A", "none", "disc", lo="airbags")
R("mazda_mx_5_miata_s_special_type_i__na__j___95", "JDM", "BP-ZE", "driver", "disc", lo="airbags")
R("mazda_mx_5_miata_v_special_type_ii__na__j___93", "JDM", "BP-ZE", "driver", "disc", lo="airbags,ac")
R("mitsubishi_fto_gr__94", "JDM", "6A12", "driver", "disc", lo="airbags,rearBrakes")
R("honda_civic_sir_ii__eg___95", "JDM", "B16A", "driver", "disc", lo="airbags")
R("mazda_mx_5_miata_vr_limited__na__j___95", "JDM", "BP-ZE", "driver", "disc", lo="airbags")
R("mazda_rx_7_gt_x__fc__j___90", "JDM", "13B-T", "none", "disc")
R("mitsubishi_mirage_cyborg_zr__97", "JDM", "4G92 MIVEC", "dual", "disc", lo="airbags,rearBrakes")
R("nissan_240sx__96", "US", "SR20DET", "dual", "disc", parts="jdm_shared", lo="market,engineCode",
  note="GT4's US name for the JDM 180SX Type X (SR20DET, 205 PS). No US 240SX had the SR20DET or a 1996 "
       "fastback; treated as a US-market car (design section 2: 'The 96 240SX has it [OBD-II]'). Engine "
       "parts come from Japan. OWNER: confirm US market")
R("nissan_skyline_hard_top_2000_turbo_rs__r30___83", "JDM", "FJ20ET", "none", "disc", lo="obd1Read,bumperEra")
R("mazda_mx_5_miata_sr_limited__na__j___97", "JDM", "BP-ZE", "dual", "disc", lo="airbags")
R("mitsubishi_fto_gr__97", "JDM", "6A12", "dual", "disc", lo="airbags,rearBrakes")
R("nissan_silvia_k_s_aero__s14___93", "JDM", "SR20DET", "driver", "disc", lo="airbags,ac")
R("nissan_silvia_spec_s_aero__s15___99", "JDM", "SR20DE", "dual", "disc", ign="dis", lo="ignition,obd1Read")
R("opel_tigra_1_6i__99", "EUR", "Opel X16XE", "dual", "drum", lo="airbags")
R("toyota_celica_gt_r__st183__4ws___91", "JDM", "3S-GE gen2", "none", "disc", lo="airbags")
R("lotus_elan_s1__62", "UK", "Lotus-Ford Twin Cam", "none", "disc", ac="none")
R("mitsubishi_lancer_1600_gsr_rally_car__74", "JDM", "4G32 SOHC", "none", "drum", bump="chrome",
  lo="rearBrakes")
R("nissan_240zg__hs30___71", "JDM", "L24", "none", "drum", ac="none")
R("nissan_skyline_2000gt_r__kpgc110___73", "JDM", "S20", "none", "disc", ac="none")
R("nissan_skyline_hard_top_2000gt_r__kpgc10___70", "JDM", "S20", "none", "drum", ac="none", lo="rearBrakes")
R("peugeot_206_s16__99", "EUR", "PSA XU10J4RS", "dual", "disc", lo="airbags")
R("sileighty__98", "JDM", "SR20DET", "none", "disc", lo="airbags,ac",
  note="a tuner build (180SX body, S13 Silvia nose) on S13 parts. Catalog origin 'eur' is RG2's "
       "catch-all: the maker is Japanese")
R("toyota_corolla_levin_bz_r__98", "JDM", "4A-GE 20v", "dual", "disc", lo="ignition")
R("toyota_sprinter_trueno_bz_r__98", "JDM", "4A-GE 20v", "dual", "disc", lo="ignition")
R("bmw_2002_turbo__73", "EUR", "BMW M10 turbo", "none", "drum", ac="none",
  note="Kugelfischer mechanical injection")
R("chevrolet_camaro_iroc_z_concept__88", "US", "SBC 350 L98 TPI", "none", "disc", lo="rearBrakes,roadLegal",
  note="a concept car; road legal here as the catalog treats it (owner's call)")
R("mazda_rx_7_infini_iii__fc__j___90", "JDM", "13B-T", "none", "disc")
R("nissan_240sx__s14___96", "US", "SR20DET", "dual", "disc", parts="jdm_shared", lo="market,engineCode",
  note="GT4's US name for the JDM Silvia K's (S14, SR20DET 220 PS); the US 240SX had the KA24DE. "
       "Treated as a US-market car like the other 240SX row. OWNER: confirm US market")
R("nissan_skyline_hard_top_2000_rs_x_turbo_c__r30___84", "JDM", "FJ20ET", "none", "disc", lo="obd1Read")
R("eagle_talon_esi__97", "US", "Chrysler 420A", "dual", "drum", lo="rearBrakes")
R("honda_civic_type_r__ek___97", "JDM", "B16B", "dual", "disc", lo="airbags",
  note="JDM: no OBD-II (design section 2)")
R("mitsubishi_fto_gpx__94", "JDM", "6A12 MIVEC", "driver", "disc", lo="airbags")
R("subaru_impreza_sedan_wrx_sti__gc___94", "JDM", "EJ20 turbo", "none", "disc", lo="airbags")
R("volvo_240_glt_estate__88", "EUR", "Volvo B230F", "none", "disc", bump="5mph", lo="market,obd1Read",
  note="the 240's 5-mph absorber bumpers were fitted in every market; LH-Jetronic 2.2 has no fault codes",
  obdr="none")
R("honda_civic_type_r__ek___98", "JDM", "B16B", "dual", "disc", lo="airbags")
R("infiniti_g20__90", "US", "SR20DE", "driver", "disc", lo="airbags",
  note="the catalog's 150 PS is the JDM Primera figure; Infiniti was a US-only brand in 1990")
R("mazda_mx_5_miata_1_8_rs__nb__j___98", "JDM", "BP-ZE", "dual", "disc")
R("peugeot_205_turbo_16__85", "EUR", "PSA XU8T", "none", "disc", bump="plastic", ac="none", pc="exotic",
  note="Group B homologation road car (200 built); K-Jetronic mechanical injection")
R("mitsubishi_fto_gpx__97", "JDM", "6A12 MIVEC", "dual", "disc", lo="airbags")
R("mitsubishi_galant_2_0_dohc_turbo_vr_4__89", "JDM", "4G63T", "none", "disc")
R("nissan_fairlady_z_300zx__z31___83", "JDM", "VG30ET", "none", "disc", lo="obd1Read")
R("honda_prelude_si_vtec__91", "JDM", "H22A", "none", "disc", lo="airbags,ac")
R("mitsubishi_eclipse_gt__95", "US", "4G63T", "dual", "disc", lo="market",
  note="the 2G Eclipse was built in Illinois for every market; OBD-II arrived with the 1996 model")
R("nissan_skyline_gts_r__r31___87", "JDM", "RB20DET", "none", "disc", ign="dist", lo="ignition")
R("toyota_celica_ss_ii__st202___97", "JDM", "3S-GE gen3", "dual", "disc")
R("toyota_celica_ss_ii__zzt231___99", "JDM", "2ZZ-GE", "dual", "disc")
R("chevrolet_camaro_z28_coupe__97", "US", "LT1", "dual", "disc")
R("isuzu_117_coupe__68", "JDM", "G161W", "none", "drum", ac="none")
R("isuzu_bellett_1600_gt_r__69", "JDM", "G161W", "none", "drum", ac="none", lo="rearBrakes")
R("mazda_110s__l10b___68", "JDM", "10A rotary", "none", "drum", ac="none")
R("mercedes_benz_a_160_avantgarde__98", "EUR", "Mercedes M166 1.6", "dual+side", "drum")
R("mitsubishi_fto_gpx__99", "JDM", "6A12 MIVEC", "dual", "disc")
R("nissan_bluebird_rally_car__510___69", "JDM", "L16", "none", "drum", lo="rearBrakes")
R("nissan_fairlady_2000__sr311___68", "JDM", "U20", "none", "drum", ac="none")
R("nissan_silvia_spec_r_aero__s15___99", "JDM", "SR20DET", "dual", "disc", lo="obd1Read")
R("nissan_skyline_2000gt_b__s54a___67", "JDM", "G7", "none", "drum", ac="none")
R("nissan_skyline_gts25_type_s__r32___91", "JDM", "RB25DE", "none", "disc", lo="airbags")
R("subaru_legacy_touring_wagon_gt_b__96", "JDM", "EJ20 twin seq", "dual", "disc", lo="airbags",
  turbo="sequential")
R("toyota_celica_2000gt_four__st165___86", "JDM", "3S-GTE gen1", "none", "disc")
R("nissan_skyline_gts_t_type_m__r32___89", "JDM", "RB20DET", "none", "disc")
R("mitsubishi_fto_gp_version_r__97", "JDM", "6A12 MIVEC", "dual", "disc", lo="airbags")
R("nissan_silvia_240rs__s100___83", "JDM", "FJ24", "none", "disc", ac="none", pc="exotic",
  note="Group B homologation road car, twin side-draft carburettors")
R("subaru_impreza_sedan_wrx_sti_version_ii__gc___95", "JDM", "EJ20 turbo", "driver", "disc", lo="airbags")
R("ford_taurus_sho__98", "US", "Ford 3.4 SHO V8", "dual", "disc")
R("mitsubishi_fto_gp_version_r__99", "JDM", "6A12 MIVEC", "dual", "disc")
R("mitsubishi_pajero_rally_raid_car__85", "JDM", "4G54 turbo rally", "none", "disc", lo="rearBrakes")
R("honda_integra_type_r__dc2___95", "JDM", "B18C", "driver", "disc", lo="airbags")
R("honda_prelude_sir__96", "JDM", "H22A", "dual", "disc", lo="airbags")
R("mitsubishi_lancer_evolution_gsr__92", "JDM", "4G63T", "none", "disc", lo="airbags,ac")
R("subaru_impreza_sedan_wrx_sti_version_iii__gc___96", "JDM", "EJ20 turbo", "dual", "disc", lo="airbags")
R("toyota_mr2_spyder__99", "US", "1ZZ-FE", "dual", "disc",
  note="the US name (JDM: MR-S); the real car is a 2000 model")
R("honda_prelude_type_s__96", "JDM", "H22A", "dual", "disc", lo="airbags")
R("nissan_skyline_gts_t_type_m__r32___91", "JDM", "RB20DET", "none", "disc", lo="airbags")
R("toyota_mr2_g_limited__97", "JDM", "3S-GE gen3", "dual", "disc")
R("nissan_240rs_rally_car__85", "JDM", "FJ24", "none", "disc")
R("subaru_legacy_b4_rsk__98", "JDM", "EJ20 twin seq", "dual", "disc", turbo="sequential")
R("honda_integra_type_r__dc2___98", "JDM", "B18C", "dual", "disc")
R("honda_prelude_sir_s_spec__98", "JDM", "H22A", "dual", "disc")
R("chevrolet_corvette_stingray_l46_350__c3___69", "US", "SBC 350 L46", "none", "disc", ac="none", lo="ac")
R("honda_integra_type_r__dc2___99", "JDM", "B18C", "dual", "disc")
R("mgf__97", "UK", "Rover K 1.8 VVC", "driver", "disc", lo="airbags,interference")
R("pontiac_tempest_le_mans_gto__64", "US", "Pontiac 389", "none", "drum", ac="none", lo="ac")
R("toyota_supra_3_0gt_turbo_a__88", "JDM", "7M-GTEU", "none", "disc", lo="ignition")
R("honda_prelude_type_s__98", "JDM", "H22A", "dual", "disc")
R("mitsubishi_lancer_evolution_ii_gsr__94", "JDM", "4G63T", "driver", "disc", lo="airbags")
R("subaru_impreza_sedan_wrx_sti_version_iv__gc___97", "JDM", "EJ20 turbo", "dual", "disc")
R("toyota_celica_gt_four_rc__st185___91", "JDM", "3S-GTE gen2", "none", "disc", lo="airbags",
  note="Group A homologation run ('RC'); water-to-air intercooler")
R("subaru_impreza_sedan_wrx_sti_version_v__gc___98", "JDM", "EJ20 turbo", "dual", "disc")
R("toyota_mr2_gt_s__97", "JDM", "3S-GTE gen3", "dual", "disc")
R("buick_gnx__87", "US", "Buick 3.8 SFI turbo", "none", "drum")
R("chevrolet_camaro_z28_302__69", "US", "SBC 302 DZ", "none", "drum", ac="none", lo="rearBrakes")
R("dodge_charger_440_r_t__70", "US", "Chrysler 440", "none", "drum", ac="none", lo="ac")
R("alfa_romeo_156_2_5_v6_24v__98", "EUR", "Alfa Busso 2.5 V6 24v", "dual", "disc", lo="airbags",
  note="Alfa Romeo left the US market in 1995")
R("mitsubishi_lancer_evolution_iii_gsr__95", "JDM", "4G63T", "driver", "disc", lo="airbags")
R("subaru_impreza_sedan_wrx_sti_version_vi__gc___99", "JDM", "EJ20 turbo", "dual", "disc")
R("subaru_impreza_sport_wagon_wrx_sti_version_vi__gf___99", "JDM", "EJ20 turbo", "dual", "disc")
R("toyota_supra_2_5gt_twin_turbo_r__90", "JDM", "1JZ-GTE", "none", "disc")
R("chevrolet_camaro_ss__69", "US", "SBC 350 L48", "none", "drum", ac="none", lo="ac")
R("lexus_is200__j___98", "JDM", "3S-GE BEAMS", "dual", "disc", lo="airbags",
  note=NOTE_JDM_US_NAME + " (Toyota Altezza RS200, 3S-GE)")
R("lexus_is200__98", "EUR", "1G-FE BEAMS", "dual+side", "disc", lo="market,airbags",
  note="the European Lexus IS200 (the JDM twin is the Altezza AS200)")
R("mitsubishi_3000gt_sl__j___95", "JDM", "6G72 DOHC", "driver", "disc", lo="airbags",
  note=NOTE_JDM_US_NAME + " (Mitsubishi GTO)")
R("mitsubishi_lancer_evolution_iv_gsr__96", "JDM", "4G63T coil", "dual", "disc", lo="airbags")
R("nissan_300zx_2seater__z32___89", "JDM", "VG30DETT", "none", "disc", lo="airbags",
  note=NOTE_JDM_US_NAME + " (Fairlady Z twin turbo)")
R("subaru_impreza_coupe_wrxtyper_sti_version_vi__gc___99", "JDM", "EJ20 turbo", "dual", "disc")
R("mitsubishi_3000gt_sl__j___96", "JDM", "6G72 DOHC", "dual", "disc", lo="airbags")
R("mitsubishi_lancer_evolution_vi_rs__99", "JDM", "4G63T coil", "none", "disc", ac="none", lo="airbags,ac",
  note="the RS is the stripped competition base")
R("toyota_celica_gt_four__st205___98", "JDM", "3S-GTE gen3", "dual", "disc")
R("honda_s2000__99", "JDM", "F20C", "dual", "disc", lo="obd1Read",
  note="250 PS is the JDM rating; the US car came for 2000")
R("mazda_rx_7_type_r__fd__j___91", "JDM", "13B-REW", "none", "disc", turbo="sequential", lo="airbags")
R("mitsubishi_lancer_evolution_v_gsr__98", "JDM", "4G63T coil", "dual", "disc")
R("nismo_270r__94", "JDM", "SR20DET", "driver", "disc", lo="airbags,ac",
  note="NISMO's S14 Silvia road car. Catalog origin 'eur' is RG2's catch-all: NISMO is Japanese")
R("nissan_skyline_gt_r__r32___89", "JDM", "RB26DETT", "none", "disc", lo="airbags")
R("nissan_stagea_25t_rs_four_s__98", "JDM", "RB25DET", "dual", "disc")
R("toyota_supra_sz_r__97", "JDM", "2JZ-GE VVT-i", "dual", "disc")
R("tvr_v8s__91", "UK", "Rover V8 3.9 TVR", "none", "disc", bump="plastic", ac="none", lo="ac")
R("lancia_stratos__73", "EUR", "Ferrari Dino V6 2.4", "none", "disc", bump="plastic", ac="none", pc="exotic")
R("alfa_romeo_166_2_5_v6_24v_sportronic__98", "EUR", "Alfa Busso 2.5 V6 24v", "dual+side", "disc",
  lo="airbags")
R("mitsubishi_3000gt_sl__j___98", "JDM", "6G72 DOHC", "dual", "disc")
R("mazda_rx_7_type_r__fd__j___93", "JDM", "13B-REW", "driver", "disc", turbo="sequential", lo="airbags,ac")
R("mitsubishi_lancer_evolution_vi_gsr__99", "JDM", "4G63T coil", "dual", "disc")
R("mitsubishi_legnum_vr_4_type_v__98", "JDM", "6A13TT", "dual", "disc", turbo="twin")
R("nissan_skyline_gt_r__r32___91", "JDM", "RB26DETT", "none", "disc", lo="airbags")
R("alfa_romeo_spider_1600_duetto__66", "EUR", "Alfa twin cam 1.6", "none", "disc", ac="none")
R("dodge_charger_super_bee_426_hemi__71", "US", "Chrysler 426 Hemi", "none", "drum", ac="none")
R("ford_rs200__84", "UK", "Cosworth BDT 1.8T", "none", "disc", bump="plastic", ac="none", pc="exotic",
  lo="obd1Read",
  note="Group B homologation road car built in Britain by Ford of Europe (200 built)")
R("mazda_rx_7_type_r_s__fd__j___95", "JDM", "13B-REW", "driver", "disc", turbo="sequential", lo="airbags")
R("peugeot_406_3_0_v6_coupe__98", "EUR", "PSA ES9J4 3.0 V6", "dual+side", "disc", lo="airbags")
R("mazda_rx_7_type_rs__fd__j___96", "JDM", "13B-REW", "dual", "disc", turbo="sequential", lo="airbags")
R("nissan_300zx_2seater__z32___98", "JDM", "VG30DETT", "dual", "disc", lo="airbags")
R("lotus_esprit_turbo_hc__87", "UK", "Lotus 910 2.2 turbo", "none", "disc", ac="R-12", lo="ac",
  note="blow-through Dellorto carburettors; the US cars got injection from 1989")
R("mitsubishi_lancer_evolution_iv_rally_car__97", "JDM", "4G63T rally", "none", "disc")
R("mazda_rx_7_type_rz__fd__j___92", "JDM", "13B-REW", "none", "disc", turbo="sequential", lo="airbags")
R("chevrolet_chevelle_ss_454__70", "US", "BBC 454 LS6", "none", "drum", ac="none", lo="ac")
R("chevrolet_corvette_zr_1__c4___90", "US", "LT5", "driver", "disc", pc="exotic")
R("lexus_gs300__91", "JDM", "2JZ-GTE", "driver", "disc", lo="airbags,ac",
  note=NOTE_JDM_US_NAME + " (Toyota Aristo 3.0V, 2JZ-GTE)")
R("lotus_elise_sport_190__98", "UK", "Rover K 1.8 VHPD", "none", "disc", bump="plastic", ac="none")
R("mazda_rx_7_type_rs_r__fd___97", "JDM", "13B-REW", "dual", "disc", turbo="sequential", lo="airbags",
  note="a JDM limited edition")
R("mazda_rx_7_type_rz__fd__j___93", "JDM", "13B-REW", "driver", "disc", turbo="sequential", lo="airbags,ac")
R("nissan_300zx_2by2__z32___98", "JDM", "VG30DETT", "dual", "disc", lo="airbags")
R("nissan_skyline_gt_r_vspec__r32___93", "JDM", "RB26DETT", "driver", "disc", lo="airbags,ac")
R("plymouth_cuda_440_six_pack__71", "US", "Chrysler 440 Six Pack", "none", "drum", ac="none")
R("spoon_integra_type_r__dc2___99", "JDM", "B18C", "dual", "disc",
  note="Spoon Sports' tuned DC2 Type R. Catalog origin 'eur' is RG2's catch-all: the maker is Japanese")
R("audi_s4__98", "EUR", "Audi AGB 2.7T", "dual+side", "disc", turbo="twin",
  note="the B5 S4 reached the US for 2000")
R("mazda_rx_7_type_rs__fd___98", "JDM", "13B-REW", "dual", "disc", turbo="sequential",
  note="the US FD ended in 1995; a 1998 car is JDM")
R("mazda_rx_7_type_rz__fd__j___95", "JDM", "13B-REW", "driver", "disc", turbo="sequential", lo="airbags")
R("mazda_rx_7_type_rz__fd__j___96", "JDM", "13B-REW", "dual", "disc", turbo="sequential", lo="airbags")
R("mercedes_benz_slk_230_kompressor__98", "US", "Mercedes M111 Kompressor", "dual+side", "disc",
  lo="market", note="sold in the US from 1997; US market assumed (the catalog's figures fit both)")
R("mitsubishi_3000gt_mr__j___95", "JDM", "6G72TT", "driver", "disc", turbo="twin", lo="airbags",
  note=NOTE_JDM_US_NAME + " (Mitsubishi GTO MR)")
R("nissan_skyline_gt_r_vspec_ii__r32___94", "JDM", "RB26DETT", "driver", "disc", lo="airbags,ac")
R("toyota_celica_gt_four_rally_car__st185___95", "JDM", "3S-GTE rally", "none", "disc")
R("toyota_celica_gt_four_rally_car__st205___95", "JDM", "3S-GTE rally", "none", "disc")
R("mitsubishi_lancer_evolution_vi_rally_car__99", "JDM", "4G63T rally", "none", "disc")
R("toyota_corolla_rally_car__98", "JDM", "3S-GTE rally", "none", "disc")
R("peugeot_206_rally_car__99", "EUR", "PSA XU9J4 WRC", "none", "disc")
R("subaru_impreza_rally_car__99", "JDM", "EJ20 WRC", "none", "disc")
R("mitsubishi_starion_4wd_rally_car__84", "JDM", "4G63T Starion rally", "none", "disc")
R("citroen_xsara_rally_car__99", "EUR", "PSA XU10J4 kit car", "none", "disc")
R("bmw_m_coupe__98", "EUR", "BMW S50B32", "dual+side", "disc", lo="airbags",
  note="321 PS is the European S50B32; the US M Coupe had the 240 hp S52")
R("chevrolet_corvette_coupe__c2___63", "US", "SBC 327", "none", "drum", ac="none")
R("hommell_berlinette_r_s_coupe__99", "EUR", "PSA XU10J4RS", "none", "disc", bump="plastic", ac="none",
  lo="airbags,ac")
R("lancia_delta_hf_integrale_evoluzione__91", "EUR", "Lancia 2.0 16v turbo", "none", "disc")
R("mitsubishi_3000gt_mr__j___98", "JDM", "6G72TT", "dual", "disc", turbo="twin")
R("nissan_skyline_gt_r__r33___95", "JDM", "RB26DETT", "driver", "disc", lo="airbags")
R("nissan_skyline_gt_r_n1__r32___91", "JDM", "RB26DETT", "none", "disc", ac="none", lo="ac",
  note="N1: the homologation base, A/C and audio deleted")
R("subaru_impreza_premium_sport_coupe_22b_sti_version__gc___98", "JDM", "EJ22 22B", "dual", "disc",
  pc="exotic", note="400 built")
R("toyota_supra_rz__97", "JDM", "2JZ-GTE VVT-i", "dual", "disc", turbo="sequential")
R("mitsubishi_3000gt_vr_4_turbo__j___95", "JDM", "6G72TT", "driver", "disc", turbo="twin", lo="airbags")
R("nissan_skyline_gt_r__r33___96", "JDM", "RB26DETT", "dual", "disc", lo="airbags")
R("lexus_sc300__97", "JDM", "1JZ-GTE VVT-i", "dual", "disc", turbo="single", lo="engineCode",
  note=NOTE_JDM_US_NAME + " (Toyota Soarer 2.5GT-T)")
R("mitsubishi_3000gt_vr_4_turbo__j___96", "JDM", "6G72TT", "dual", "disc", turbo="twin", lo="airbags")
R("nissan_skyline_gt_r__r33___97", "JDM", "RB26DETT", "dual", "disc", lo="airbags")
R("chevrolet_corvette_grand_sport__c4___96", "US", "LT4", "dual", "disc")
R("mitsubishi_3000gt_vr_4__j___98", "JDM", "6G72TT", "dual", "disc", turbo="twin")
R("nissan_skyline_gt_r_vspec__r33___95", "JDM", "RB26DETT", "driver", "disc", lo="airbags")
R("nissan_skyline_gt_r_vspec_n1__r32___93", "JDM", "RB26DETT", "none", "disc", ac="none", lo="ac")
R("nissan_stagea_260rs_autechversion__98", "JDM", "RB26DETT", "dual", "disc")
R("tvr_griffith_500__94", "UK", "Rover V8 5.0 TVR", "none", "disc", bump="plastic", ac="none", pc="exotic",
  lo="ac,priceClass")
R("mitsubishi_hsr_ii_concept__89", "JDM", "6G72TT", "none", "disc", turbo="twin", ac="none", pc="exotic",
  parts="race", lo="roadLegal",
  note="a concept car; road legal here as the catalog treats it (owner's call). One-off parts")
R("nissan_skyline_gt_r_vspec__r33___96", "JDM", "RB26DETT", "dual", "disc", lo="airbags")
R("nissan_skyline_gt_r_vspec__r33___97", "JDM", "RB26DETT", "dual", "disc", lo="airbags")
R("peugeot_205_turbo_16_rally_car__85", "EUR", "PSA XU8T rally", "none", "disc")
R("renault_5_maxi_turbo_rally_car__85", "EUR", "Renault Cleon 1.5 turbo rally", "none", "disc")
R("lotus_carlton__90", "UK", "GM C36GET", "none", "disc", turbo="twin", pc="exotic",
  note="the UK-market Lotus Omega/Carlton")
R("lotus_motor_sport_elise__99", "UK", "Rover K 1.8 VHPD", "none", "disc", legal=False, ac="none",
  lo="roadLegal",
  note="the one-make race-series Elise: NOT road legal (authored), although the catalog does not mark "
       "it built-to-tune. OWNER: confirm")
R("nissan_skyline_gt_r_n1__r33___95", "JDM", "RB26DETT", "driver", "disc", ac="none", lo="airbags,ac")
R("nissan_skyline_gt_r_vspec_lm_limited__r33___96", "JDM", "RB26DETT", "dual", "disc", lo="airbags")
R("alfa_romeo_155_2_5_v6_ti__93", "EUR", "Alfa 2.5 V6 DTM", "none", "disc")
R("chevrolet_corvette_convertible__c1___54", "US", "Chevy 235 Blue Flame I6", "none", "drum", ac="none",
  parts="classic", note="triple side-draft carburettors, Powerglide automatic only")
R("mercedes_benz_190_e_2_5___16_evolution_ii__91", "EUR", "Mercedes M102 2.5-16", "none", "disc",
  pc="exotic", lo="airbags",
  note="502 built, never sold in the US; KE-Jetronic (electronically trimmed continuous injection)")
R("nissan_skyline_gt_r__r34___99", "JDM", "RB26DETT", "dual", "disc", lo="obd1Read")
R("tvr_cerbera_speed_six__97", "UK", "TVR AJP6 Speed Six", "none", "disc", bump="plastic", pc="exotic",
  lo="ac")
R("nissan_skyline_gt_r_v_spec__r34___99", "JDM", "RB26DETT", "dual", "disc", lo="obd1Read")
R("ford_rs200_rally_car__85", "UK", "Cosworth BDT-E 2.1T", "none", "disc")
R("acura_nsx__91", "US", "C30A", "driver", "disc")
R("honda_nsx__90", "JDM", "C30A", "driver", "disc", lo="airbags")
R("nissan_skyline_gt_r_special_color_midnight_purple_ii__r34___99", "JDM", "RB26DETT", "dual", "disc",
  lo="obd1Read")
R("nissan_skyline_gt_r_v_spec_n1__r34___99", "JDM", "RB26DETT", "dual", "disc", ac="none",
  lo="obd1Read,ac,airbags")
R("peugeot_205_turbo_16_evolution_2_rally_car__86", "EUR", "PSA XU8T rally", "none", "disc")
R("lancia_stratos_rally_car__77", "EUR", "Ferrari Dino V6 2.4 rally", "none", "disc", bump="plastic")
R("lancia_delta_hf_integrale_rally_car__92", "EUR", "Lancia 2.0 16v turbo rally", "none", "disc")
R("jensen_interceptor_mkiii__74", "UK", "Chrysler 440", "none", "disc", ign="dist", ac="R-12", pc="exotic",
  lo="ignition,priceClass", note="Chrysler 440 and TorqueFlite; chrome bumpers on the home-market car")
R("honda_nsx__93", "JDM", "C30A", "driver", "disc", lo="airbags")
R("dodge_viper_gts__99", "US", "Viper V10 8.0", "dual", "disc")
R("alfa_romeo_giulia_sprint_speciale__63", "EUR", "Alfa twin cam 1.6", "none", "drum", ac="none",
  pc="exotic", lo="rearBrakes,priceClass")
R("ford_escort_rally_car__98", "UK", "Ford YB Cosworth WRC", "none", "disc")
R("honda_nsx__95", "JDM", "C30A", "dual", "disc", lo="airbags")
R("jaguar_xkr_coupe__99", "UK", "Jaguar AJ-V8 SC", "dual+side", "disc", pc="exotic", lo="market,priceClass",
  note="the XKR reached the US for 2000")
R("mercedes_benz_sl_500__r129___98", "US", "Mercedes M113 5.0", "dual+side", "disc", lo="market")
R("acura_nsx_coupe__97", "US", "C32B", "dual", "disc")
R("honda_nsx__97", "JDM", "C32B", "dual", "disc")
R("alfa_romeo_giulia_sprint_gta_1600__65", "EUR", "Alfa twin cam 1.6 GTA", "none", "disc", ac="none",
  pc="exotic", note="twin-plug homologation special")
R("ford_focus_rally_car__99", "UK", "Ford Zetec WRC", "none", "disc")
R("lotus_esprit_v8_gt__98", "UK", "Lotus 918 V8 TT", "driver", "disc", turbo="twin", pc="exotic",
  lo="market,airbags")
R("nismo_skyline_gt_r_r_tune__r34___99", "JDM", "RB26DETT", "dual", "disc", lo="obd1Read",
  note="NISMO's tuned R34. Catalog origin 'eur' is RG2's catch-all: NISMO is Japanese")
R("plymouth_super_bird__70", "US", "Chrysler 426 Hemi", "none", "drum", ac="none", parts="us_classic",
  note="the catalog's 6981 cc is the 426 Hemi")
R("jaguar_e_type_coupe__61", "UK", "Jaguar XK 3.8", "none", "disc", ac="none", pc="exotic")
R("honda_nsx__99", "JDM", "C32B", "dual", "disc")
R("lotus_esprit_v8_se__98", "UK", "Lotus 918 V8 TT", "driver", "disc", turbo="twin", pc="exotic",
  lo="market,airbags")
R("ruf_btr__86", "EUR", "Porsche 930 3.4 turbo RUF", "none", "disc", bump="5mph", pc="exotic",
  lo="bumperEra",
  note="911 G-body impact bumpers (absorbers, 5-mph type) in every market; K-Jetronic mechanical injection")
R("honda_nsx_type_s__97", "JDM", "C32B", "dual", "disc")
R("honda_nsx_type_r__92", "JDM", "C30A", "none", "disc", ac="none", lo="airbags,ac",
  note="the NSX-R deleted weight: no audio, A/C optional")
R("honda_nsx_type_s__99", "JDM", "C32B", "dual", "disc")
R("mercury_cougar_xr_7__67", "US", "Ford 390 FE", "none", "drum", ac="none", lo="ac")
R("honda_nsx_type_s_zero__97", "JDM", "C32B", "dual", "disc", ac="none", lo="airbags,ac")
R("opel_calibra_touring_car__94", "EUR", "Opel KF V6 ITC", "none", "disc")
R("honda_nsx_type_s_zero__99", "JDM", "C32B", "dual", "disc", ac="none", lo="airbags,ac")
R("honda_gathers_drider_civic_race_car__98", "JDM", "B16B race", "none", "disc")
R("honda_s800_rsc_race_car__68", "JDM", "AS800E RSC", "none", "drum", lo="rearBrakes")
R("mercedes_benz_sl_600__r129___98", "US", "Mercedes M120 6.0 V12", "dual+side", "disc", pc="exotic",
  lo="market")
R("mitsubishi_fto_super_touring_car__97", "JDM", "FTO JTCC race", "none", "disc")
R("ruf_ctr__yellow_bird___87", "EUR", "RUF CTR 3.4 TT", "none", "disc", bump="5mph", turbo="twin",
  pc="exotic", lo="bumperEra",
  note="911 G-body impact bumpers (absorbers, 5-mph type), reshaped by RUF")
R("nismo_400r__96", "JDM", "RB-X GT2 2.8", "dual", "disc", pc="exotic", lo="airbags",
  note="NISMO's R33 road car (about 44 built). Catalog origin 'eur' is RG2's catch-all: NISMO is Japanese")
R("lancia_delta_s4_rally_car__85", "EUR", "Lancia S4 twincharged", "none", "disc", turbo="twincharged")
R("mercedes_benz_300_sl_coupe__54", "US", "Mercedes M198", "none", "drum", ac="none", pc="exotic",
  parts="classic", lo="market",
  note="most Gullwings were sold in the US; Bosch mechanical direct injection")
R("honda_mugen_motul_civic_si_race_car__87", "JDM", "ZC Mugen race", "none", "disc")
R("shelby_mustang_g_t__350r__65", "US", "Ford 289 HiPo", "none", "drum", legal=False, ac="none",
  lo="roadLegal",
  note="the R was Shelby's competition model (36 built, no street equipment): NOT road legal (authored), "
       "although the catalog does not mark it built-to-tune. OWNER: confirm")
R("suzuki_escudo_dirt_trial_car__98", "JDM", "H25A TT Pikes", "none", "disc", turbo="twin")
R("amg_mercedes_190_e_2_5___16_evolution_ii_touring_car__92", "EUR", "Mercedes M102 DTM", "none", "disc")
R("ac_cars_427_s_c__66", "US", "Ford 427 FE", "none", "disc", ac="none", pc="exotic", parts="classic",
  lo="market",
  note="built by AC in Britain, sold by Shelby American in the US; the S/C were unsold competition "
       "cars converted for the road")
R("shelby_cobra_427__67", "US", "Ford 427 FE", "none", "disc", ac="none", pc="exotic", parts="classic")
R("ruf_ctr2__96", "EUR", "RUF CTR2 3.6 TT", "dual", "disc", turbo="twin", pc="exotic")
R("nissan_pennzoil_nismo_gt_r__jgtc___99", "JDM", "RB26 race", "none", "disc", turbo="single")
R("toyota_2000gt__67", "JDM", "3M", "none", "disc", ac="none", pc="exotic")
R("aston_martin_v8_vantage__99", "UK", "Aston V8 5.3 twin SC", "dual", "disc", pc="exotic", lo="airbags",
  turbo="none", note="Ford EEC-IV engine management")
R("cizeta_v16t__94", "EUR", "Cizeta V16", "none", "disc", bump="plastic", ac="R-134a", pc="exotic",
  parts="race", lo="airbags,ac")
R("chaparral_2d_race_car__67", "US", "Chevy 327 alu Hilborn", "none", "disc", bump="plastic",
  note="Catalog origin 'eur' is RG2's catch-all: Chaparral is American")
R("ford_gt40_race_car__69", "US", "Ford 302 GT40", "none", "disc", bump="plastic")
R("jaguar_xj220__92", "UK", "Jaguar JRV-6 TT", "none", "disc", turbo="twin", pc="exotic", parts="race",
  ac="R-12")
R("peugeot_905_race_car__92", "EUR", "Peugeot SA35 V10", "none", "disc")
R("nissan_calsonic_skyline_gt_r_race_car__93", "JDM", "RB26 race", "none", "disc", turbo="twin")
R("mazda_787b_race_car__91", "JDM", "R26B race", "none", "disc")
R("nismo_gt_r_lm_road_going_version__95", "JDM", "RB26DETT", "driver", "disc", pc="exotic", parts="race",
  lo="market,airbags",
  note="the Le Mans homologation road car (rear drive); the one built was registered in Britain. "
       "Catalog origin 'eur' is RG2's catch-all: NISMO is Japanese")
R("chevrolet_corvette_z06__c2__race_car__63", "US", "SBC 327 FI", "none", "drum",
  note="Rochester mechanical fuel injection")
R("toyota_gt_one_race_car__ts020___99", "JDM", "R36V race", "none", "disc", turbo="twin")
R("nissan_r390_gt1_race_car__98", "JDM", "VRH35 race", "none", "disc", turbo="twin")
R("bmw_v12_lmr_race_car__99", "EUR", "BMW S70/2 V12", "none", "disc")
R("bmw_mclaren_f1_gtr_race_car__97", "EUR", "BMW S70/2 V12", "none", "disc")
R("panoz_esperante_gtr_1_race_car__98", "US", "Ford 6.0 V8 Panoz", "none", "disc",
  note="Catalog origin 'eur' is RG2's catch-all: Panoz is American")
R("lister_storm_v12_race_car__99", "UK", "Jaguar V12 Lister", "none", "disc")
R("amg_mercedes_clk_gtr_race_car__98", "EUR", "Mercedes M119 race", "none", "disc")
R("chaparral_2j_race_car__70", "US", "Chevy 465 alu Hilborn", "none", "disc", bump="plastic",
  note="Catalog origin 'eur' is RG2's catch-all: Chaparral is American")
R("sauber_mercedes_c_9_race_car__89", "EUR", "Mercedes M119 race", "none", "disc", turbo="twin")
R("minolta_toyota_88c_v_race_car__89", "JDM", "R32V race", "none", "disc", turbo="twin")
R("nissan_r89c_race_car__89", "JDM", "VRH35 race", "none", "disc", turbo="twin")
R("nissan_r92cp_race_car__92", "JDM", "VRH35 race", "none", "disc", turbo="twin")
R("toyota_7_race_car__70", "JDM", "Toyota 7 5.0 TT race", "none", "disc", bump="plastic", turbo="twin")
R("nissan_r390_gt1_road_car__98", "JDM", "VRH35L", "none", "disc", turbo="twin", pc="exotic", parts="race",
  lo="airbags,obd1Read", note="the GT1 homologation road car")
R("jaguar_xjr_9_race_car__88", "UK", "Jaguar V12 7.0 race", "none", "disc")
R("honda_accord_coupe_type_s__03", "US", "J32A2", "dual+side", "disc", lo="engineCode,airbags",
  note="the id says 03 and the name says 99; the catalog's 3.2 V6 figures match the 2001-03 Acura CL "
       "Type-S (J32A2). A US-market car either way")
R("chevrolet_corvette_z06__c5___99", "US", "LS6", "dual", "disc",
  note="the real Z06 (LS6) is a 2001 model; the catalog dates it '99")

# --------------------------------------------------------------------------
# Class lists
# --------------------------------------------------------------------------
ECONOMY = {
    "honda_civic_1500_3door_cx__79", "honda_civic_1500_3door_25i__83", "honda_1300_coupe_9_s__70",
    "nissan_bluebird_hardtop_1800sss__910___79", "volkswagen_golf_i_gti__76",
    "toyota_corolla_levin_gt_apex__ae86___83", "toyota_sprinter_trueno_gt_apex__ae86___83",
    "honda_ballade_sports_cr_x_1_5i__83", "honda_city_turbo_ii__83", "mazda_demio__j___99",
    "mazda_mazda_323f__93", "isuzu_piazza_xe__81", "honda_accord_coupe__88",
    "nissan_exa_canopy_l_a_version_type_s__n13___88", "toyota_sera__92", "toyota_carina_ed_2_0_x_4ws__89",
    "toyota_starlet_glanza_v__97", "honda_cr_x_sir__90", "honda_civic_sir_ii__eg___91",
    "honda_civic_sir_ii__eg___92", "honda_civic_sir_ii__eg___93", "honda_civic_sir_ii__eg___95",
    "honda_cr_x_del_sol_sir__92", "mitsubishi_mirage_cyborg_zr__97", "opel_tigra_1_6i__99",
    "peugeot_206_s16__99", "toyota_corolla_levin_bz_r__98", "toyota_sprinter_trueno_bz_r__98",
    "eagle_talon_esi__97", "honda_civic_type_r__ek___97", "honda_civic_type_r__ek___98",
    "volvo_240_glt_estate__88", "infiniti_g20__90", "mercedes_benz_a_160_avantgarde__98",
    "toyota_mr2_1600_g__86",
}
EXOTIC_PREFIX = ("honda_nsx", "acura_nsx", "ruf_", "lotus_esprit_v8")

# JDM rows whose model had a US twin sharing most parts (chassis, brakes, trim)
JDM_SHARED_PREFIX = (
    "honda_civic", "honda_cr_x", "honda_integra", "honda_prelude", "honda_nsx", "honda_s2000",
    "honda_ballade", "mazda_mx_5", "mazda_rx_7", "nissan_300zx", "nissan_fairlady_z_300zx",
    "nissan_fairlady_z_280z", "nissan_240zg", "nissan_silvia_q_s__s13", "nissan_silvia_k_s__s13",
    "nissan_silvia_q_s_aero__s14", "nissan_silvia_k_s_aero__s14", "toyota_supra", "toyota_mr2",
    "toyota_celica_2000gt_four__st165", "toyota_celica_gt_four_rc__st185", "toyota_celica_gt_r__st183",
    "toyota_celica_2000gt_r__st162", "toyota_celica_ss_ii", "toyota_celica_xx", "toyota_corolla_levin_gt_apex",
    "toyota_sprinter_trueno_gt_apex", "mitsubishi_3000gt", "mitsubishi_galant_2_0", "isuzu_piazza",
    "nissan_exa", "lexus_gs300", "lexus_sc300", "sileighty", "spoon_integra", "toyota_carina",
)


def family_of(cid, origin):
    if cid in ("dome_zero__78", "sileighty__98", "spoon_integra_type_r__dc2___99") or cid.startswith("nismo_"):
        return "jpn"
    if cid.startswith("chaparral_") or cid.startswith("panoz_"):
        return "usa"
    if origin in ("ger", "gbr", "fra", "ita", "eur"):
        return "eur"
    return origin


def built_to_tune(c):
    n = c["name"]
    return ("Race Car" in n) or ("Rally" in n) or c.get("raceSusp", 0) > 0


def obd1_method(cid, market):
    if market in ("EUR", "UK"):
        return "dealer"
    for p, m in (("honda_", "ecu_led"), ("acura_", "ecu_led"), ("spoon_", "ecu_led"),
                 ("nissan_", "ecu_led"), ("infiniti_", "ecu_led"), ("nismo_", "ecu_led"),
                 ("sileighty", "ecu_led"), ("toyota_", "toyota_te1_e1"), ("lexus_", "toyota_te1_e1"),
                 ("mitsubishi_", "analog_meter"), ("eagle_", "analog_meter"), ("mazda_", "jumper_lamp"),
                 ("subaru_", "jumper_lamp"), ("isuzu_", "jumper_lamp"), ("chevrolet_", "gm_jumper"),
                 ("buick_", "gm_jumper"), ("pontiac_", "gm_jumper"), ("ford_", "analog_meter"),
                 ("mercedes_", "dealer"), ("jaguar_", "dealer")):
        if cid.startswith(p):
            return m
    return "dealer"


def ac_rule(market, year):
    """(ac, lowConf) for a road car with A/C."""
    if year < 1970:
        return "none", False
    if market == "US":
        return ("R-12", year == 1993) if year <= 1993 else ("R-134a", False)
    if market == "JDM":
        if year <= 1991:
            return "R-12", False
        if year <= 1993:
            return ("R-12" if year == 1992 else "R-134a"), True
        return "R-134a", False
    if year <= 1992:
        return "R-12", False
    if year <= 1994:
        return ("R-12" if year == 1993 else "R-134a"), True
    return "R-134a", False


PARTS = {"us_local": (1, 1), "us_dealer": (1, 3), "us_classic": (2, 7), "jdm_shared": (3, 7),
         "jdm_only": (7, 21), "eur_dealer": (2, 5), "eur_special": (5, 21), "classic": (7, 21),
         "race": (14, 28)}
EUR_DEALER_PREFIX = ("mercedes_", "bmw_", "audi_", "volkswagen_", "volvo_", "jaguar_xkr", "lexus_",
                     "lotus_esprit")
PRICE = {"economy": 1.0, "sport": 1.15, "exotic": 2.5, "race": 3.0}


def bake(cars):
    rows, lowcount = [], 0
    for c in cars:
        cid = c["id"]
        if cid not in C:
            raise SystemExit("no facts row for catalog id " + cid)
        a = C[cid]
        en = E[a["e"]]
        year = int(c["modelYear"])
        btt = built_to_tune(c)
        lo = [x for x in (a.get("lo", "") + "," + en["lo"]).split(",") if x]
        market = "RACE" if btt else a["m"]
        legal = a.get("legal", not btt)
        fuel = a.get("fuel", en["fuel"])
        # codes
        if fuel != "efi" or market == "RACE":
            obd, obdr = 0, "none"
        elif market == "US" and year >= 1996 and legal:
            obd, obdr = 2, "none"
        else:
            obdr = a.get("obdr", obd1_method(cid, market))
            if year <= 1982:
                obdr = "none"
            obd = 0 if obdr == "none" else 1
        if fuel == "carb":
            esys = "CARB"
        elif fuel == "mech":
            esys = "MECH"
        elif market == "RACE":
            esys = "EFI-RACE"
        elif obd == 2:
            esys = "EFI-OBD2"
        elif market == "JDM":
            esys = "EFI-JDM"
        else:
            esys = "EFI-OBD1"
        # bumper
        bump = a.get("bump")
        if bump is None:
            if year < 1973:
                bump = "chrome"
            elif market == "US":
                bump = "5mph" if year <= 1982 else "2.5mph"
            elif market == "RACE":
                bump = "plastic"
            else:
                bump = "chrome" if year <= 1980 else "plastic"
        # A/C
        ac = a.get("ac")
        if ac is None:
            if not legal:
                ac = "none"
            else:
                ac, aclo = ac_rule(market, year)
                if aclo:
                    lo.append("ac")
        if not legal:
            ac = "none"
        # induction
        turbo = a.get("turbo")
        if turbo is None:
            if c["asp"] == "TURBO":
                turbo = "single"
                if a["e"] in ("RB26DETT", "VG30DETT", "6G72TT", "1JZ-GTE", "RB26 race",
                              "Audi AGB 2.7T", "Lotus 918 V8 TT", "GM C36GET"):
                    turbo = "twin"
                if a["e"] in ("2JZ-GTE", "2JZ-GTE VVT-i", "13B-REW", "EJ20 twin seq"):
                    turbo = "sequential"
            else:
                turbo = "none"
        # classes
        if not legal:
            pcn = "race"
        else:
            pcn = a.get("pc") or ("exotic" if cid.startswith(EXOTIC_PREFIX) else
                                  "economy" if cid in ECONOMY else "sport")
        parts = a.get("parts")
        if parts is None:
            if not legal:
                parts = "race"
            elif market == "US":
                if year < 1975:
                    parts = "us_classic"
                elif cid.startswith(("mercedes_", "bmw_", "jaguar_")):
                    parts = "eur_dealer"
                elif cid.startswith("acura_nsx"):
                    parts = "us_dealer"
                else:
                    parts = "us_local"
            elif market == "JDM":
                if year < 1975:
                    parts = "classic"
                else:
                    parts = "jdm_shared" if cid.startswith(JDM_SHARED_PREFIX) else "jdm_only"
            else:
                if year < 1975:
                    parts = "classic"
                else:
                    parts = "eur_dealer" if cid.startswith(EUR_DEALER_PREFIX) else "eur_special"
        if not legal:
            parts = "race"
        pmin, pmax = PARTS[parts]
        ign = a.get("ign", en["ign"])
        if not legal:
            ab = "none"
        else:
            ab = a["ab"]
        row = dict(
            id=cid, name=c["name"], origin=c["origin"], family=family_of(cid, c["origin"]),
            market=market, year=year, builtToTune=btt, roadLegal=legal,
            serviceUnit="miles" if legal else "hours",
            priceClass=PRICE[pcn], priceClassName=pcn,
            stockInternals=3 if btt else en["si"], stockBoostBar=0.0 if btt else en["boost"],
            engineCode=a["e"], fuel=fuel, eSys=esys, obd=obd, obd1Read=obdr,
            camDrive=en["cam"], beltMi=en["belt"] if en["cam"] == "belt" and legal else 0,
            interference=en["interf"], ignition=ign, turbo=turbo,
            airbags=ab, rearBrakes=a["rb"], bumperEra=bump, ac=ac,
            partsSource=parts, partsWaitMin=pmin, partsWaitMax=pmax,
            knownIssues=en["issues"], lowConf=",".join(sorted(set(lo))), note=a.get("note", ""))
        if row["lowConf"]:
            lowcount += 1
        rows.append(row)
    extra = set(C) - set(c["id"] for c in cars)
    if extra:
        raise SystemExit("facts rows not in the catalog: " + ", ".join(sorted(extra)))
    return rows, lowcount


def validate(rows, cars):
    """The same rules the self-test enforces (LifeSimSelfTest.CarFacts.cs)."""
    bad = []
    ids = [r["id"] for r in rows]
    if len(ids) != len(set(ids)):
        bad.append("duplicate ids")
    if sorted(ids) != sorted(c["id"] for c in cars):
        bad.append("rows != catalog ids")
    for r in rows:
        i, y, m = r["id"], r["year"], r["market"]
        if r["obd"] == 2 and not (m == "US" and y >= 1996 and r["fuel"] == "efi"):
            bad.append(i + ": OBD-II off a US-market 1996+ EFI row")
        if m == "US" and y >= 1996 and r["fuel"] == "efi" and r["roadLegal"] and r["obd"] != 2:
            bad.append(i + ": US-market 1996+ EFI road car without OBD-II")
        if r["fuel"] == "carb" and (r["obd"] != 0 or r["obd1Read"] != "none" or r["eSys"] != "CARB"):
            bad.append(i + ": codes on a carburettor row")
        if r["fuel"] == "mech" and (r["obd"] != 0 or r["eSys"] != "MECH"):
            bad.append(i + ": codes on a mechanical-injection row")
        ab = r["airbags"]
        if not r["roadLegal"] and ab != "none":
            bad.append(i + ": airbags on a race car")
        if m in ("US", "JDM") and y < 1987 and ab != "none":
            bad.append(i + ": airbags before 1987 (" + m + ")")
        if m in ("EUR", "UK") and y < 1981 and ab != "none":
            bad.append(i + ": airbags before 1981 (" + m + ")")
        if m == "US" and y >= 1998 and r["roadLegal"] and ab not in ("dual", "dual+side"):
            bad.append(i + ": US-market 1998+ without dual airbags")
        if r["builtToTune"] and r["roadLegal"]:
            bad.append(i + ": built-to-tune row marked road legal")
        if r["serviceUnit"] != ("miles" if r["roadLegal"] else "hours"):
            bad.append(i + ": service unit")
        if not r["roadLegal"] and r["priceClass"] != 3.0:
            bad.append(i + ": race price class")
        b = r["bumperEra"]
        if m == "US" and 1973 <= y <= 1982 and r["roadLegal"] and b != "5mph":
            bad.append(i + ": US 1973-82 without 5-mph bumpers")
        if b == "2.5mph" and not (m == "US" and y >= 1983):
            bad.append(i + ": 2.5-mph bumper off a US 1983+ row")
        if b == "5mph" and not (m == "US" and 1973 <= y <= 1982) and "5-mph" not in r["note"]:
            bad.append(i + ": 5-mph bumper outside US 1973-82 without an authored note")
        if y < 1973 and b in ("5mph", "2.5mph"):
            bad.append(i + ": federal bumper before 1973")
        if r["partsWaitMin"] > r["partsWaitMax"] or r["partsWaitMin"] < 1:
            bad.append(i + ": parts wait")
        if r["camDrive"] == "belt" and r["roadLegal"] and r["beltMi"] <= 0:
            bad.append(i + ": belt engine without an interval")
        if r["camDrive"] != "belt" and r["beltMi"] != 0:
            bad.append(i + ": belt interval on a " + r["camDrive"] + " engine")
    return bad


def main():
    cars = json.load(io.open(CATALOG, encoding="utf-8"))["cars"]
    rows, lowcount = bake(cars)
    bad = validate(rows, cars)
    nl = sum(1 for r in rows if not r["roadLegal"])
    print("rows %d, not road legal %d (built-to-tune %d), OBD-II %d, carb %d, mech %d, low-confidence rows %d"
          % (len(rows), nl, sum(1 for r in rows if r["builtToTune"]), sum(1 for r in rows if r["obd"] == 2),
             sum(1 for r in rows if r["fuel"] == "carb"), sum(1 for r in rows if r["fuel"] == "mech"), lowcount))
    for b in bad:
        print("BAD " + b)
    if bad:
        sys.exit(1)
    if "--check" in sys.argv:
        return
    out = io.StringIO()
    out.write('{"format":%d,"source":"tools/bake_car_facts.py","rows":[\n' % FORMAT)
    for k, r in enumerate(rows):
        out.write(json.dumps(r, ensure_ascii=True, separators=(",", ":")))
        out.write(",\n" if k < len(rows) - 1 else "\n")
    out.write("]}\n")
    with io.open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(out.getvalue())
    print("wrote " + OUT)


if __name__ == "__main__":
    main()
