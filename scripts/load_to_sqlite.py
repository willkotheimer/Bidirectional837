"""
Load seed CSV/JSON files into a local SQLite database for testing.

Usage:
  python scripts/load_to_sqlite.py --db data/translator.db

Creates tables: icd10(code,description), hcpcs(code,description), charges(procedure_code,description,price), providers(...), patients(...)
"""
import argparse
import sqlite3
import csv
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SEED = ROOT / "seed"
DATA = ROOT / "data"
DATA.mkdir(parents=True, exist_ok=True)

DB_SCHEMA = {
    "icd10": ("code TEXT PRIMARY KEY", "description TEXT"),
    "hcpcs": ("code TEXT PRIMARY KEY", "description TEXT"),
    "charges": ("procedure_code TEXT", "description TEXT", "price REAL"),
}


def create_tables(conn: sqlite3.Connection):
    cur = conn.cursor()
    for table, cols in DB_SCHEMA.items():
        cur.execute(f"CREATE TABLE IF NOT EXISTS {table} ({', '.join(cols)})")
    cur.execute("CREATE TABLE IF NOT EXISTS providers (number TEXT PRIMARY KEY, json TEXT)")
    cur.execute("CREATE TABLE IF NOT EXISTS patients (id TEXT PRIMARY KEY, json TEXT)")
    conn.commit()


def load_csv_to_table(conn, csv_path: Path, table_name: str):
    with open(csv_path, newline='', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        rows = list(reader)
    if not rows:
        return
    cur = conn.cursor()
    if table_name == 'charges':
        cur.executemany('INSERT OR REPLACE INTO charges (procedure_code,description,price) VALUES (?,?,?)',
                        [(r.get('procedure_code') or r.get('code'), r.get('description'), float(r.get('price') or 0)) for r in rows])
    else:
        cur.executemany(f"INSERT OR REPLACE INTO {table_name} (code,description) VALUES (?,?)",
                        [(r.get('code'), r.get('description')) for r in rows])
    conn.commit()


def load_json_to_table(conn, json_path: Path, table_name: str):
    with open(json_path, encoding='utf-8') as f:
        data = json.load(f)
    cur = conn.cursor()
    if table_name == 'providers':
        for item in data:
            cur.execute('INSERT OR REPLACE INTO providers (number, json) VALUES (?,?)', (item.get('number'), json.dumps(item)))
    elif table_name == 'patients':
        for item in data:
            cur.execute('INSERT OR REPLACE INTO patients (id, json) VALUES (?,?)', (item.get('id'), json.dumps(item)))
    conn.commit()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--db', default='data/translator.db')
    args = parser.parse_args()
    db_path = Path(args.db)
    conn = sqlite3.connect(str(db_path))
    create_tables(conn)
    # CSV loads
    icd10_path = SEED / 'icd10_sample.csv'
    hcpcs_path = SEED / 'hcpcs_sample.csv'
    charges_path = SEED / 'charges_sample.csv'
    providers_path = SEED / 'providers_sample.json'
    patients_path = SEED / 'patients_sample.json'

    if icd10_path.exists():
        load_csv_to_table(conn, icd10_path, 'icd10')
        print('Loaded icd10_sample.csv')
    if hcpcs_path.exists():
        load_csv_to_table(conn, hcpcs_path, 'hcpcs')
        print('Loaded hcpcs_sample.csv')
    if charges_path.exists():
        load_csv_to_table(conn, charges_path, 'charges')
        print('Loaded charges_sample.csv')
    if providers_path.exists():
        load_json_to_table(conn, providers_path, 'providers')
        print('Loaded providers_sample.json')
    if patients_path.exists():
        load_json_to_table(conn, patients_path, 'patients')
        print('Loaded patients_sample.json')

    conn.close()
    print(f'Database written to {db_path}')

if __name__ == '__main__':
    main()
