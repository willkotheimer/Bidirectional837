"""
Fetch authoritative datasets and normalize to seed/full/

Usage:
  python scripts/fetch_and_convert.py --icd10 --hcpcs --mpfs --npi

Notes:
- This script attempts to download publicly available CSVs. Some sources are ZIP archives or require manual download. It writes raw files to `seed/full/` and attempts a lightweight normalization.
- Adjust URLs as needed in the DEFAULT_URLS dict.
"""
import argparse
import os
import requests
from pathlib import Path
import shutil
import csv

ROOT = Path(__file__).resolve().parents[1]
SEED_FULL = ROOT / "seed" / "full"
SEED_FULL.mkdir(parents=True, exist_ok=True)

DEFAULT_URLS = {
    "icd10": "https://www.cdc.gov/nchs/data/icd/icd10cm_2024_code_descriptions.txt",
    "hcpcs": "https://www.cms.gov/files/zip/hcpcs-codes.zip",
    "mpfs": "https://www.cms.gov/files/zip/physician-fee-schedule-data.zip",
    "npi_sample": "https://npiregistry.cms.hhs.gov/registry/api?version=2.1&number=1234567890"
}

HEADERS = {"User-Agent": "837TranslatorSeedFetcher/1.0 (+https://example.org)"}


def download(url: str, target: Path) -> Path:
    print(f"Downloading {url} -> {target.name}")
    r = requests.get(url, headers=HEADERS, stream=True, allow_redirects=True)
    r.raise_for_status()
    with open(target, "wb") as f:
        shutil.copyfileobj(r.raw, f)
    return target


def normalize_icd10(src: Path, dest: Path):
    # Very small heuristic: if tab-delimited descriptions file, copy as CSV with code,description
    print(f"Normalizing ICD-10 from {src} to {dest}")
    with open(src, "r", encoding="utf-8", errors="ignore") as inf, open(dest, "w", newline='', encoding='utf-8') as outf:
        writer = csv.writer(outf)
        writer.writerow(["code", "description"])
        for line in inf:
            parts = line.strip().split("\t")
            if len(parts) >= 2:
                code = parts[0].strip()
                desc = parts[1].strip()
                writer.writerow([code, desc])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--icd10", action="store_true")
    parser.add_argument("--hcpcs", action="store_true")
    parser.add_argument("--mpfs", action="store_true")
    parser.add_argument("--npi", action="store_true")
    args = parser.parse_args()

    if args.icd10:
        url = DEFAULT_URLS["icd10"]
        raw = SEED_FULL / "icd10_raw.txt"
        try:
            download(url, raw)
            normalize_icd10(raw, SEED_FULL / "icd10.csv")
            print("ICD-10 saved to seed/full/icd10.csv")
        except Exception as e:
            print("Failed to fetch/normalize ICD-10:", e)

    if args.hcpcs:
        url = DEFAULT_URLS["hcpcs"]
        raw = SEED_FULL / "hcpcs_raw.zip"
        try:
            download(url, raw)
            print("HCPCS downloaded. Manual extraction likely required; please inspect the ZIP and move the CSVs into seed/full/ as needed.")
        except Exception as e:
            print("Failed to fetch HCPCS:", e)

    if args.mpfs:
        url = DEFAULT_URLS["mpfs"]
        raw = SEED_FULL / "mpfs_raw.zip"
        try:
            download(url, raw)
            print("MPFS downloaded. Manual extraction likely required; please inspect the ZIP and transform to charges CSV.")
        except Exception as e:
            print("Failed to fetch MPFS:", e)

    if args.npi:
        url = DEFAULT_URLS["npi_sample"]
        out = SEED_FULL / "npi_sample.json"
        try:
            download(url, out)
            print("NPI sample saved to seed/full/npi_sample.json")
        except Exception as e:
            print("Failed to fetch NPI sample:", e)

if __name__ == '__main__':
    main()
