# WHICH SIDE EACH CATALOG CAR IS DRIVEN FROM (owner, 2026-10-16):
#   "RHD when real car is RHD. Same for LHD. Some cars can be either, like EG
#    Civic or NSX. Also, aren't British cars RHD?"
#
#   py tools/cockpit/drive_side.py          rules -> Docs/drive_side.csv + Resources copy
#   py tools/cockpit/drive_side.py --sync   Docs/drive_side.csv (owner-edited) -> Resources
#
# One row per catalog row (Resources/rg2_cars.json), decided for THAT row's
# market variant: a Japanese-market name or trim is RHD, a US-market name is
# LHD, a British make is RHD, continental Europe and America are LHD. A car
# the row's name does not pin to one market is flagged ambiguous=yes with the
# reason, so the owner can review those first. The game reads the Resources
# copy (DriveSide.cs); Docs/drive_side.csv is the one to edit.
import csv, io, json, os, re, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CARS = os.path.join(ROOT, "Assets", "PSXRacing", "Resources", "rg2_cars.json")
DOC = os.path.join(ROOT, "Docs", "drive_side.csv")
RES = os.path.join(ROOT, "Assets", "PSXRacing", "Resources", "DriveSide.csv")

# (regex on the name, side, reason, ambiguous) - FIRST match wins.
RULES = [
    # --- explicit market tags and US-market names of Japanese cars ---------
    (r"\(J\)|, J\)", "RHD", "(J) = the Japanese-market version", False),
    (r"^Acura ", "LHD", "Acura = Honda's US brand", False),
    (r"^Infiniti ", "LHD", "Infiniti = Nissan's US brand", False),
    (r"^Lexus SC300|^Lexus GS300", "LHD", "Lexus name = US/export car (Japan: Soarer / Aristo)", False),
    (r"^Lexus IS200 '", "LHD", "Lexus IS200 = export (Japan: Altezza); Europe LHD, but the UK got RHD", True),
    (r"240SX", "LHD", "240SX = the US name of the Silvia / 180SX", False),
    (r"Nissan 300ZX", "LHD", "300ZX = the US name (Japan: Fairlady Z), though JDM cars wore 300ZX badges too", True),
    (r"MX-5 Miata \(NA\)", "LHD", "MX-5 Miata with no (J) = US car (Japan: Eunos Roadster)", False),
    (r"MR2 Spyder", "LHD", "MR2 Spyder = the US name (Japan: MR-S)", False),
    (r"Mitsubishi ECLIPSE", "LHD", "Eclipse = US-built for the US market", False),
    (r"Mazda Mazda 323F", "LHD", "323F = export name (Japan: Lantis); Europe LHD, the UK RHD", True),
    (r"Honda ACCORD Coupe '88", "LHD", "US-built Accord Coupe; Japan took it as an LHD import", False),
    (r"Honda ACCORD Coupe Type S", "LHD", "US-built Accord Coupe (Acura CL Type-S power); JDM Euro-R body was a sedan", True),
    # --- the owner's own examples: cars sold both ways ----------------------
    (r"Honda NSX Type", "RHD", "NSX Type R/S/S Zero were Japan-only trims", False),
    (r"Honda NSX '", "RHD", "Honda-badged NSX = Japan (US was Acura); Europe also had LHD Honda NSXs", True),
    (r"CIVIC SiR-II \(EG\)", "RHD", "SiR-II = the Japanese EG trim (owner: EG Civic can be either)", True),
    (r"Honda S2000", "RHD", "sold in Japan, the US and Europe under one name; Japan by default", True),
    (r"INTEGRA TYPE R \(DC2\) '9[89]", "RHD", "Honda-badged = Japan (US was Acura); Europe also had LHD", True),
    (r"Mitsubishi Galant 2\.0 DOHC Turbo VR-4", "RHD", "Japanese VR-4; the US also had an LHD Galant VR-4", True),
    # --- race, rally and concept cars: per team / build ---------------------
    (r"McLaren F1", "RHD", "centre driving seat - nearest is a guess (British build)", True),
    (r"Chaparral", "LHD", "US race car (Chaparral seats were offset to the left, unverified)", True),
    (r"Panoz", "RHD", "US team, British build; offset seat unverified", True),
    (r"Nissan R390 GT1 Road Car", "RHD", "TWR-built in Britain from the Le Mans car", False),
    (r"GT40", "RHD", "the GT40 race cars were built in Britain, RHD", True),
    (r"(787B|GT-ONE|R390 GT1 Race|88C-V|R89C|R92CP|Toyota 7 Race|XJR-9|905 Race|V12 LMR|C 9 Race|Lister Storm|CLK-GTR)",
     "RHD", "sports-prototype / GT1 race car: driver on the right for clockwise circuits (unverified per car)", True),
    (r"(Calibra Touring|190 E 2\.5 - 16 Evolution II Touring)", "LHD", "German touring car (DTM/ITC), LHD road car base", False),
    (r"(PENNZOIL|CALSONIC|MUGEN MOTUL|Gathers Drider|S800 RSC|FTO Super Touring)", "RHD", "Japanese race car on a Japanese road car", False),
    (r"(BLUEBIRD Rally Car \(510\)|Lancer 1600 GSR Rally)", "RHD", "Japanese works car for the Safari Rally (Kenya drives on the left)", True),
    (r"ESCUDO Dirt Trial", "RHD", "Japanese-built hill-climb car", True),
    (r"(Rally Car|Rally Raid)", "LHD", "works rally car: the European-run teams built them LHD (unverified per car)", True),
    (r"Concept", None, "concept car: home-market side assumed", True),
    # --- Japanese tuners and specials filed under 'eur' ---------------------
    (r"^(NISMO|Spoon|SILEIGHTY|DOME)", "RHD", "Japanese tuner / maker car on a JDM base", False),
    # --- British cars sold mostly abroad ------------------------------------
    (r"AC Cars 427 S/C", "LHD", "AC-built Cobra for Shelby's US market; British make", True),
    (r"Jaguar E-Type|Jaguar XKR", "RHD", "British make; most were exported LHD to the US", True),
    (r"Ford RS200", "LHD", "Ford of Britain's Group B homologation car; most were LHD", True),
]
ORIGIN = {  # the default by maker's country
    "jpn": ("RHD", "Japanese-market name/trim"),
    "gbr": ("RHD", "British make"),
    "aus": ("RHD", "Australian make"),
    "usa": ("LHD", "American make"),
    "ger": ("LHD", "German make"),
    "fra": ("LHD", "French make"),
    "ita": ("LHD", "Italian make"),
    "eur": ("LHD", "continental European make"),
}


def decide(car):
    name = car["name"].replace("`", "'")
    home = ORIGIN.get(car.get("origin", ""), ("LHD", "unknown origin"))
    for pat, side, why, amb in RULES:
        if re.search(pat, name):
            return (side or home[0]), why, amb
    if re.search(r"Holden|HSV", name):
        return "RHD", "Australian make", False
    return home[0], home[1], False


def main():
    if "--sync" in sys.argv:
        with open(DOC, encoding="utf-8") as f:
            text = f.read()
        with open(RES, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        print("synced", DOC, "->", RES)
        return
    with open(CARS, encoding="utf-8") as f:
        cars = json.load(f)["cars"]
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\n")
    w.writerow(["id", "name", "side", "reason", "ambiguous"])
    n = {"RHD": 0, "LHD": 0, "amb": 0}
    for c in cars:
        side, why, amb = decide(c)
        n[side] += 1
        n["amb"] += 1 if amb else 0
        w.writerow([c["id"], c["name"].replace("`", "'"), side, why, "yes" if amb else "no"])
    os.makedirs(os.path.dirname(DOC), exist_ok=True)
    for p in (DOC, RES):
        with open(p, "w", encoding="utf-8", newline="\n") as f:
            f.write(buf.getvalue())
    print("rows %d: RHD %d, LHD %d, ambiguous %d -> %s" % (len(cars), n["RHD"], n["LHD"], n["amb"], DOC))


main()
