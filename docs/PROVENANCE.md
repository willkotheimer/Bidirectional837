837Translator — Provenance and Audit Log

Project provenance and decision history

- Project: 837Translator
- Primary governance document: `governance.txt` (authorship noted below)
- Governance authorship: governance.txt was authored by Gemini AI (as recorded by project stakeholders).

Record of assistant actions:
- 2026-08-29: Reviewed `governance.txt` and drafted 837Translator-specific governance additions (hosting, persistence policy, CI/CD guidance).
- 2026-08-29: Created `seed/` samples (ICD-10, HCPCS, providers, patients, charges) to support offline and CI runs.
- The seed files are intentionally small and synthetic; they are not PHI.

Data sources referenced (official links):
- CDC ICD-10-CM: https://www.cdc.gov/nchs/icd/icd10cm.htm
- CMS HCPCS general info: https://www.cms.gov/medicare/coding/medhcpcsgeninfo
- CMS Physician Fee Schedule (MPFS): https://www.cms.gov/medicare/physician-fee-schedule
- NPI Registry API docs: https://npiregistry.cms.hhs.gov/registry/help-api
- Synthea synthetic patient generator: https://github.com/synthetichealth/synthea
- Faker library: https://faker.readthedocs.io/

Decision notes and rationale:
- We avoid persisting real PHI to cloud; prefer synthetic or masked data for demos/tests.
- CPT is proprietary; use HCPCS or licensed CPT subsets for demo data.
- NPI Registry is allowed for lookups; cache results and provide offline fallbacks for CI.

Suggested next steps (for auditability):
- Commit this file alongside changes to the code and CI so provenance is versioned.
- Add a CHANGELOG.md to record future governance updates and decisions.

