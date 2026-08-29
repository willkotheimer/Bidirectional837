Seed data for 837Translator

Contents:
- `icd10_sample.csv` — tiny ICD‑10-CM sample (code,description)
- `hcpcs_sample.csv` — tiny HCPCS sample (code,description)
- `charges_sample.csv` — example procedure pricing
- `providers_sample.json` — NPI-like provider mock objects
- `patients_sample.json` — lightweight synthetic patient examples

Notes and provenance:
- Full ICD‑10-CM files available from CDC: https://www.cdc.gov/nchs/icd/icd10cm.htm
- HCPCS and Physician Fee Schedule data available from CMS:
  - HCPCS: https://www.cms.gov/medicare/coding/medhcpcsgeninfo
  - MPFS: https://www.cms.gov/medicare/physician-fee-schedule
- NPI Registry API: https://npiregistry.cms.hhs.gov/registry/help-api
- Synthea for richer synthetic patient data: https://github.com/synthetichealth/synthea
- Faker library for lightweight fake data generation: https://faker.readthedocs.io/

Licensing & privacy:
- Use CDC/CMS data per their terms. CPT is proprietary (AMA); avoid unless licensed.
- Do not use real patient PHI in persistent cloud stores. These samples are synthetic.

How to refresh/update:
- Download authoritative CSVs from the linked sources and transform to the CSV/JSON shapes above. Place full files in `seed/full/` (ignored by default) and update any loader code accordingly.
