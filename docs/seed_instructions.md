Seeding and dataset instructions for 837Translator

1) Install Python deps

```bash
python -m pip install -r requirements.txt
```

2) Fetch authoritative datasets (optional)

The `scripts/fetch_and_convert.py` script can download public datasets and write them to `seed/full/`.

Examples:

```bash
python scripts/fetch_and_convert.py --icd10
python scripts/fetch_and_convert.py --hcpcs
python scripts/fetch_and_convert.py --mpfs
python scripts/fetch_and_convert.py --npi
```

Notes:
- Some CMS files are ZIP archives and may require manual extraction.
- Update `DEFAULT_URLS` in the script if authoritative URIs change.

3) Load sample seeds into SQLite

```bash
python scripts/load_to_sqlite.py --db data/translator.db
```

This creates `data/translator.db` with tables: `icd10`, `hcpcs`, `charges`, `providers`, `patients` using the small samples in `seed/`.

4) Refreshing seeds

Place full, downloaded CSVs in `seed/full/` (these files are expected to be large). If you place canonical CSVs there, you can run custom transformations to create the normalized CSVs consumed by the loader.

5) CI considerations

- CI should avoid calling external APIs unless explicitly allowed. Instead, include the required normalized CSVs in the pipeline or generate them from authoritative downloads in a controlled job with caching.

