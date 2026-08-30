"""Distil a committable provider set out of the NPPES bulk download.

PROVENANCE: ADR-023 - the bulk NPPES file and the NPI registry query API are the same data from
the same authority. Reading a distilled snapshot gives the real providers governance User Story 1.1
asks for, and turns a batch of 500 claims from 500 network calls into none.

The bulk file is ~1.1 GB compressed and ~11.6 GB expanded, so it is never committed: it lives in
seed/full/, which .gitignore excludes. This script streams it out of the zip without extracting and
writes seed/providers_by_state.csv, which is small enough to version.

Every filter below is driven by the governance Section 2 column it feeds, so a provider that would
have to be truncated to fit Loop2010AA is dropped rather than trimmed. Truncating it here would put
a provider in the seed whose name is not that provider's name.

Usage:
    python scripts/distill_providers.py [--per-state 60] [--scan-limit 3000000]
"""

from __future__ import annotations

import argparse
import csv
import io
import os
import sys
import zipfile
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
BULK_DIR = os.path.join(ROOT, "seed", "full")
OUTPUT = os.path.join(ROOT, "seed", "providers_by_state.csv")

# NPPES column indices, from the file's own header row.
NPI = 0
ENTITY_TYPE = 1
ORG_NAME = 4
LAST_NAME = 5
FIRST_NAME = 6
ADDRESS_1 = 28
CITY = 30
STATE = 31
POSTAL = 32
COUNTRY = 33
DEACTIVATION_DATE = 39

# Governance Section 2 StringLength limits on the Loop2010AA columns.
MAX_ORG_OR_LAST = 100
MAX_FIRST = 35
MAX_ADDRESS = 55
MAX_CITY = 30
MAX_POSTAL = 15

# The four ASC X12 delimiters. A value carrying one cannot be serialised (the writer refuses it),
# so it must not reach the seed.
X12_DELIMITERS = set("*~:^")

JURISDICTIONS = [
    "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "DC", "FL", "GA", "HI", "ID", "IL", "IN",
    "IA", "KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH",
    "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "PR", "RI", "SC", "SD", "TN", "TX",
    "UT", "VT", "VA", "WA", "WV", "WI", "WY",
]

ISSUER_PREFIX = "80840"


def npi_check_digit_is_valid(candidate: str) -> bool:
    """The NPI check digit rule: Luhn over the identifier prefixed with the CMS issuer 80840.

    Reimplemented here rather than shared with Translator.Domain so the seed and the domain agree
    by independent arrival rather than by construction. If they ever disagree, the seed integrity
    theories fail, which is the point.
    """
    if len(candidate) != 10 or not candidate.isdigit():
        return False

    total = 0
    source = ISSUER_PREFIX + candidate[:9]
    for offset, character in enumerate(reversed(source)):
        digit = int(character)
        if offset % 2 == 0:
            digit *= 2
            if digit > 9:
                digit -= 9
        total += digit

    return str((10 - (total % 10)) % 10) == candidate[9]


def clean(value: str) -> str:
    return " ".join(value.strip().upper().split())


def usable(value: str, limit: int) -> bool:
    return 0 < len(value) <= limit and not (X12_DELIMITERS & set(value))


def distil(archive_path: str, per_state: int, scan_limit: int) -> list[list[str]]:
    wanted = set(JURISDICTIONS)
    picked: dict[str, list[list[str]]] = defaultdict(list)
    organisations: dict[str, int] = defaultdict(int)
    scanned = 0

    with zipfile.ZipFile(archive_path) as archive:
        name = next(n for n in archive.namelist() if n.startswith("npidata_pfile") and "fileheader" not in n)
        print(f"streaming {name}", flush=True)

        with archive.open(name) as handle:
            reader = csv.reader(io.TextIOWrapper(handle, encoding="latin-1", newline=""))
            next(reader)  # header

            for row in reader:
                scanned += 1
                if scanned % 500_000 == 0:
                    full = sum(1 for s in JURISDICTIONS if len(picked[s]) >= per_state)
                    print(f"  scanned {scanned:,} - {full}/{len(JURISDICTIONS)} jurisdictions filled", flush=True)

                if scanned >= scan_limit:
                    break

                if len(row) <= DEACTIVATION_DATE:
                    continue
                if row[DEACTIVATION_DATE].strip():
                    continue
                if row[COUNTRY].strip() not in ("", "US"):
                    continue

                state = clean(row[STATE])
                if state not in wanted or len(picked[state]) >= per_state:
                    continue

                npi = row[NPI].strip()
                if not npi_check_digit_is_valid(npi):
                    continue

                address = clean(row[ADDRESS_1])
                city = clean(row[CITY])
                postal = clean(row[POSTAL])
                if not (usable(address, MAX_ADDRESS) and usable(city, MAX_CITY) and usable(postal, MAX_POSTAL)):
                    continue

                entity = row[ENTITY_TYPE].strip()
                if entity == "2":
                    # A ceiling, not a target: organisations are the minority of NPIs in the range
                    # this scan reaches, so in practice the cap is never hit and the mix lands
                    # around a quarter organisational. It exists only to stop one populous state
                    # from filling its whole quota with organisations, because Loop2010AA_NM104
                    # being null or not is what drives the NM102 person / non-person branch, and
                    # both branches need generated claims behind them.
                    if organisations[state] >= (per_state * 2) // 3:
                        continue
                    name_value, first = clean(row[ORG_NAME]), ""
                    if not usable(name_value, MAX_ORG_OR_LAST):
                        continue
                    organisations[state] += 1
                elif entity == "1":
                    name_value, first = clean(row[LAST_NAME]), clean(row[FIRST_NAME])
                    if not usable(name_value, MAX_ORG_OR_LAST) or not usable(first, MAX_FIRST):
                        continue
                else:
                    continue

                picked[state].append([npi, entity, name_value, first, address, city, state, postal])

                if all(len(picked[s]) >= per_state for s in JURISDICTIONS):
                    print(f"  every jurisdiction filled after {scanned:,} rows", flush=True)
                    break

    rows: list[list[str]] = []
    for state in JURISDICTIONS:
        rows.extend(picked[state])
    return rows


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--per-state", type=int, default=60)
    parser.add_argument("--scan-limit", type=int, default=3_000_000)
    args = parser.parse_args()

    archives = [f for f in os.listdir(BULK_DIR) if f.lower().startswith("nppes") and f.lower().endswith(".zip")] \
        if os.path.isdir(BULK_DIR) else []
    if not archives:
        print(f"No NPPES archive in {BULK_DIR}. Download the monthly file from", file=sys.stderr)
        print("https://download.cms.gov/nppes/NPI_Files.html and put it there.", file=sys.stderr)
        return 1

    archive_path = os.path.join(BULK_DIR, sorted(archives)[-1])
    print(f"distilling from {os.path.basename(archive_path)}", flush=True)

    rows = distil(archive_path, args.per_state, args.scan_limit)

    with open(OUTPUT, "w", encoding="utf-8", newline="\n") as handle:
        writer = csv.writer(handle, lineterminator="\n")
        writer.writerow(["npi", "entity_type", "org_or_last_name", "first_name",
                         "address_line", "city", "state", "postal_code"])
        writer.writerows(rows)

    covered = len({r[6] for r in rows})
    print(f"wrote {len(rows):,} providers across {covered} jurisdictions to {OUTPUT}")
    return 0 if covered == len(JURISDICTIONS) else 1


if __name__ == "__main__":
    raise SystemExit(main())
