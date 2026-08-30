"""Distil a priced, categorised HCPCS Level II catalog out of the published CMS fee schedules.

PROVENANCE: ADR-024 - the catalog is built backwards from the fee schedules rather than forwards
from a list of codes. A code reaches the catalog only if a published schedule prices it, so every
catalogued code is real, current, billable and priced by construction. Nothing has to be assumed to
exist and no gap has to be discovered later.

Two schedules are read, both public domain and both HCPCS Level II only:

  * the Medicare Physician Fee Schedule relative value file, which prices a service in RVUs;
  * the Medicare Part B payment limit (ASP) file, which prices a drug in dollars per unit.

CPT is never read. Level I codes are five digits and belong to the AMA; the D series is dental and
belongs to the ADA. Both are excluded by pattern, and Translator.Generation.Tests asserts the
exclusion so the copyright boundary is checked by the build rather than remembered.

Usage:
    python scripts/distill_codes.py
"""

from __future__ import annotations

import csv
import io
import os
import re
import sys
import zipfile
from decimal import Decimal, ROUND_HALF_UP

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
BULK_DIR = os.path.join(ROOT, "seed", "full")
CATEGORY_OUTPUT = os.path.join(ROOT, "seed", "hcpcs_categories.csv")
CHARGE_OUTPUT = os.path.join(ROOT, "seed", "charges_sample.csv")

# CY 2026 non-qualifying-APM conversion factor, from the CY 2026 PFS final rule (CMS-1832-F).
# 2026 is the first year with two: $33.57 for qualifying APM participants and $33.40 for everyone
# else. The RVU member read below is the nonQPP file, so this is the factor that matches it -
# pairing the QPP factor with the nonQPP RVUs would mix two schedules.
CONVERSION_FACTOR = Decimal("33.40")

# A HCPCS Level II code: one letter then four digits. Level I (CPT) is five digits and is AMA
# copyright; the D series is CDT and is ADA copyright. Neither can match this pattern.
LEVEL_II = re.compile(r"^[A-CE-V][0-9]{4}$")

# Governance User Story 1.2 names Anesthesia, Physical Therapy and Cardiac as its examples, so those
# three lead. The rest are the clinically meaningful groupings the remaining priced codes fall into.
#
# The rules are keyword matches over the CMS short descriptor, listed here rather than buried in
# code so they can be read and argued with. First match wins, so order is meaningful: a code
# matching both "cardiac" and "therapy" is cardiac rehabilitation and belongs under Cardiac.
CATEGORY_RULES: list[tuple[str, tuple[str, ...]]] = [
    ("Anesthesia", (
        "anesth", "sedation", "lidocaine", "bupivacaine", "mepivacaine", "ropivacaine",
        "midazolam", "propofol", "fentanyl", "ketamine", "etomidate", "succinylcholine",
    )),
    ("Cardiac", (
        "cardiac", "cardio", "heart", "ekg", "ecg", "electrocardiog", "echocardiog", "coronary",
        "myocard", "aortic", "arrhythm", "defibrill", "pacemaker", "counterpulse",
    )),
    ("PhysicalTherapy", (
        "physical therap", "occupational therap", "therapist", "rehabilit", "gait", "orthotic",
        "prosthet", "exercise", "electrical stim", "traction", "ultrasound therap",
    )),
    ("Radiology", (
        "x-ray", "xray", "radiolog", "imaging", "tomograph", "mri", "magnetic res", "ultrasound",
        "mammog", "fluoroscop", "angiograph", "contrast", "radiopharm", "gadol", "tc99", "f18",
    )),
    ("Laboratory", (
        "assay", "screen", "specimen", "culture", "blood", "urine", "serum", "cytolog",
        "patholog", "biopsy", "smear", "antibod", "antigen",
    )),
    ("Oncology", ("chemotherap", "oncolog", "tumor", "tumour", "antineoplast", "radiation therap")),
    ("Vaccines", ("vaccine", "toxoid", "immuniz")),
    ("RespiratoryCare", ("oxygen", "nebuliz", "ventilat", "respirat", "tracheost", "cpap", "airway")),
    ("WoundCare", ("wound", "dressing", "bandage", "gauze", "ulcer", "debride")),
    ("Ambulance", ("ambulance", "mileage", "transport")),
    # Last, and deliberately: the drug schedule prices ~850 codes whose descriptions all begin
    # "Injection, ...", so a drug rule placed any earlier would swallow the clinical categories
    # above it. Everything reaching this rule is a drug that no clinical rule claimed.
    ("Drugs", ("injection", "infusion", "intravenous", "oral", "per mg", "per ml")),
]


def money(value: Decimal) -> str:
    return str(value.quantize(Decimal("0.01"), rounding=ROUND_HALF_UP))


# A code's category comes from what the code *is*, which is the head of its description. Matching
# the whole of it files "Office or other outpatient visit ... esketamine" under Anesthesia because
# a drug is named at the end, and "skilled nursing services ... in a comprehensive outpatient
# rehabilitation facility" under PhysicalTherapy because a building is named at the end. The head
# is the subject; the tail is the qualifying detail.
SUBJECT_LENGTH = 70


def categorise(description: str) -> str | None:
    subject = description[:SUBJECT_LENGTH].lower()
    for category, keywords in CATEGORY_RULES:
        if any(keyword in subject for keyword in keywords):
            return category
    return None


def read_hcpcs_descriptions() -> dict[str, str]:
    """The authoritative CMS long description for every HCPCS Level II code.

    The fee schedules carry heavily abbreviated short descriptors - "Elec stim unattend for press",
    "Extrnl counterpulse" - which are too compressed to categorise on and too cryptic to show a
    user. The HCPCS Level II file carries the full sentence, so it is the source for both.

    The file lists two-character modifiers alongside five-character procedure codes, right-justified
    in the same field, so "A1" the dressing modifier sits under an entry that looks like a code.
    Only codes matching the Level II pattern are kept.
    """
    import openpyxl

    with zipfile.ZipFile(archive("hcpcs")) as bundle:
        name = next(n for n in bundle.namelist()
                    if n.upper().endswith(".XLSX") and "ANWEB" in n.upper() and "TRANSACTION" not in n.upper())
        workbook = openpyxl.load_workbook(io.BytesIO(bundle.read(name)), read_only=True)

    described: dict[str, str] = {}
    rows = workbook.active.iter_rows(values_only=True)
    heading = [str(cell or "").strip().upper() for cell in next(rows)]
    code_at = heading.index("HCPC")
    long_at = heading.index("LONG DESCRIPTION")

    for row in rows:
        if len(row) <= long_at:
            continue

        code = str(row[code_at] or "").strip().upper()
        if not LEVEL_II.match(code):
            continue

        description = " ".join(str(row[long_at] or "").split())
        if description and code not in described:
            described[code] = description

    return described


def archive(prefix: str) -> str:
    matches = [f for f in os.listdir(BULK_DIR) if f.lower().startswith(prefix) and f.lower().endswith(".zip")]
    if not matches:
        raise SystemExit(
            f"No '{prefix}*.zip' in {BULK_DIR}. See docs/PROVENANCE.md for the source URLs.")
    return os.path.join(BULK_DIR, sorted(matches)[-1])


def read_physician_fee_schedule() -> dict[str, tuple[str, Decimal]]:
    """Priced services: total RVU times the conversion factor."""
    priced: dict[str, tuple[str, Decimal]] = {}

    with zipfile.ZipFile(archive("rvu")) as bundle:
        name = next(n for n in bundle.namelist() if "PPRRVU" in n and n.endswith("nonQPP.csv"))
        rows = list(csv.reader(io.StringIO(bundle.read(name).decode("latin-1"))))

    def number(cell: str) -> Decimal:
        try:
            return Decimal(cell.strip() or "0")
        except Exception:
            return Decimal("0")

    for row in rows:
        if len(row) < 11:
            continue

        code = row[0].strip().upper()
        if not LEVEL_II.match(code):
            continue

        # Columns 5, 6 and 10 are the work, non-facility practice expense and malpractice RVUs.
        total = number(row[5]) + number(row[6]) + number(row[10])
        if total <= 0:
            continue

        charge = total * CONVERSION_FACTOR

        # A positive RVU can still round to nothing at two decimal places, and a service line
        # billing $0.00 is not a charge. It would also let a whole claim total zero, which satisfies
        # the governed CLM02 sum invariant vacuously - the failure mode the parser already refuses.
        if charge.quantize(Decimal("0.01"), rounding=ROUND_HALF_UP) <= 0:
            continue
        description = " ".join(row[2].split())

        # A code appears once per modifier; keep the highest-valued row, which is the unmodified
        # service. Modifiers reduce payment and are not part of the governed SV101 element.
        if code not in priced or charge > priced[code][1]:
            priced[code] = (description, charge)

    return priced


def read_part_b_payment_limits() -> dict[str, tuple[str, Decimal]]:
    """Priced drugs: the published payment limit per billing unit, already in dollars."""
    priced: dict[str, tuple[str, Decimal]] = {}

    with zipfile.ZipFile(archive("asp")) as bundle:
        name = next(n for n in bundle.namelist()
                    if n.lower().endswith(".csv") and "payment limit" in n.lower())
        rows = list(csv.reader(io.StringIO(bundle.read(name).decode("latin-1"))))

    for row in rows:
        if len(row) < 4:
            continue

        code = row[0].strip().upper()
        if not LEVEL_II.match(code):
            continue

        try:
            charge = Decimal(row[3].strip())
        except Exception:
            continue

        if charge.quantize(Decimal("0.01"), rounding=ROUND_HALF_UP) <= 0:
            continue

        priced[code] = (" ".join(row[1].split()), charge)

    return priced


def read_dmepos_fee_schedule() -> dict[str, tuple[str, Decimal]]:
    """Priced equipment, prosthetics, orthotics and supplies, in dollars.

    The published ceiling is used rather than one of the fifty state columns beside it. It is a
    single national figure CMS publishes for the code, so it needs no arbitrary choice of state -
    and this catalog does not vary charge by jurisdiction, because the governed request varies the
    *provider* by jurisdiction and the charge by procedure.
    """
    priced: dict[str, tuple[str, Decimal]] = {}

    with zipfile.ZipFile(archive("dme")) as bundle:
        name = next(n for n in bundle.namelist() if n.upper().startswith("DMEPOS") and n.upper().endswith(".CSV"))
        rows = list(csv.reader(io.StringIO(bundle.read(name).decode("latin-1"))))

    heading_at = next(i for i, row in enumerate(rows) if row and row[0].strip().upper() == "HCPCS")
    heading = [cell.strip().upper() for cell in rows[heading_at]]
    ceiling_at = heading.index("CEILING")

    for row in rows[heading_at + 1:]:
        if len(row) <= ceiling_at:
            continue

        code = row[0].strip().upper()
        if not LEVEL_II.match(code):
            continue

        # A modified rate is a variant of the same code; the unmodified rate is the one the governed
        # SV101 element carries.
        if row[1].strip() or row[2].strip():
            continue

        try:
            charge = Decimal(row[ceiling_at].strip().replace("$", "").replace(",", ""))
        except Exception:
            continue

        if charge.quantize(Decimal("0.01"), rounding=ROUND_HALF_UP) <= 0:
            continue

        priced[code] = ("", charge)

    return priced


def main() -> int:
    described = read_hcpcs_descriptions()
    equipment = read_dmepos_fee_schedule()
    services = read_physician_fee_schedule()
    drugs = read_part_b_payment_limits()

    print(f"hcpcs level II        : {len(described):,} described codes")
    print(f"dmepos fee schedule   : {len(equipment):,} priced Level II items")
    print(f"physician fee schedule: {len(services):,} priced Level II services")
    print(f"part B payment limits : {len(drugs):,} priced Level II drugs")

    # Layered most-specific-last. The three schedules are almost disjoint by code letter, but where
    # they overlap the drug schedule governs a drug and the physician schedule governs a service.
    merged = dict(equipment)
    merged.update(services)
    merged.update(drugs)

    catalogued: list[tuple[str, str, str, Decimal]] = []
    unnamed = 0
    for code, (short_description, charge) in sorted(merged.items()):
        # The long description is authoritative. A priced code the HCPCS file does not describe is
        # dropped rather than catalogued under a cryptic abbreviation.
        description = described.get(code)
        if not description:
            unnamed += 1
            continue

        category = categorise(description)
        if category is None:
            continue

        # Governance Section 2 caps the line description nowhere, but a catalog entry that cannot
        # be read is no use to the dropdown it feeds.
        catalogued.append((code, category, description, charge))

    print(f"priced but undescribed: {unnamed:,} (dropped)")

    if not catalogued:
        print("nothing survived categorisation", file=sys.stderr)
        return 1

    with open(CATEGORY_OUTPUT, "w", encoding="utf-8", newline="\n") as handle:
        writer = csv.writer(handle, lineterminator="\n")
        writer.writerow(["code", "category", "description"])
        writer.writerows((code, category, description) for code, category, description, _ in catalogued)

    with open(CHARGE_OUTPUT, "w", encoding="utf-8", newline="\n") as handle:
        writer = csv.writer(handle, lineterminator="\n")
        writer.writerow(["procedure_code", "description", "price"])
        writer.writerows((code, description, money(charge)) for code, _, description, charge in catalogued)

    counts: dict[str, int] = {}
    for _, category, _, _ in catalogued:
        counts[category] = counts.get(category, 0) + 1

    print(f"wrote {len(catalogued):,} priced, categorised codes")
    for category, count in sorted(counts.items(), key=lambda pair: -pair[1]):
        print(f"  {category:<16} {count:>5}")

    for governed in ("Anesthesia", "PhysicalTherapy", "Cardiac"):
        if counts.get(governed, 0) == 0:
            print(f"governance names {governed} as a category and none survived", file=sys.stderr)
            return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
