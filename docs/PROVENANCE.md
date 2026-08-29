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


---

## Continuation log — build completion (Claude Opus 5)

The entries above were authored by GitHub Copilot and are preserved unchanged. Everything below
records the completion of the build. Architectural decisions, and every departure from
`governance.txt`, live in `docs/DECISIONS.md`; this log records what was done and when.

### Section 1 — Governed schema and ephemeral persistence (2026-08-29)

- Surveyed the inherited tree: `governance.txt` (Gemini AI) plus Python seed tooling, sample data
  and docs (GitHub Copilot). No .NET solution existed; governance Sections 2-4 mandate one.
- Confirmed three decisions with the project owner before writing code: build order (ADR-001),
  persistence mechanism (ADR-002), and pull request delivery (ADR-006).
- Transcribed the governance Section 2 entity model verbatim into `src/Governance.Domain/Entities`
  and marked it as a normative transcription (ADR-003).
- Wrote the Section 1 test suite first and recorded it failing, per governance Section 4:
  164 failed / 26 passed, `docs/tdd-evidence/section-1-red.txt`. Every invariant is an xUnit
  `[Theory]`. The 26 passing tests are reflection-only naming guards over the transcription and
  drive no implementation; this is disclosed in `docs/tdd-evidence/README.md` and in ADR-003.
- Implemented `EphemeralClaimStore` and `ClaimsDbContext`. Final state 190 passed / 0 failed,
  `docs/tdd-evidence/section-1-green.txt`.
- Two defects were found by the failing tests rather than by inspection, both in the money path:
  SQLite float coercion corrupting `9999999999999999.99` into `10000000000000000`, and loss of
  trailing zeros turning `1.00` into `1`. Both are Zero-Mutation violations; both are fixed and
  recorded in ADR-004.
- One governed guarantee is genuinely weakened by the absence of SQL Server: `StringLength` is not
  enforced by SQLite. Verified by probe, recorded in ADR-005, and carried forward as a debt owed by
  Section 2.
- One inherited defect recorded and deferred: `seed/charges_sample.csv` is referenced in three
  places but absent, and the loader hides this behind an existence check (ADR-007).

Data sources: no new external data was introduced in this section. The claim corpus used as Theory
data is deterministic and synthetic, contains no PHI, and is generated in-process by
`tests/Governance.Domain.Tests/Corpus/GovernedClaimCorpus.cs`.

### Section 2 — API contracts, DTO validation and decision traceability (2026-08-29)

- Authored `docs/api/swagger.json` before writing any controller, per governance Section 4. It is
  the published surface, and `Governance.Api.Tests` measures the application against it rather than
  the reverse. Governance names two routes; the four it does not name are recorded in ADR-009.
- Transcribed the governance Section 3 DTOs into `src/Governance.Contracts`.
- Adopted the decision provenance marking convention requested by the project owner (ADR-008).
  Every code-bearing decision now carries a `PROVENANCE: ADR-NNN` comment, and
  `Governance.Traceability.Tests` enforces the convention in both directions. Section 1 sources were
  retrofitted with markers as part of the GREEN commit.
- Recorded RED: 89 failed / 345 passed (`docs/tdd-evidence/section-2-red.txt`). Recorded GREEN:
  444 passed / 0 failed (`docs/tdd-evidence/section-2-green.txt`).
- ADR-005, the debt Section 1 left open, is discharged: the governed StringLength limits are
  declared on the DTOs, proven to mirror the entity model field by field, and proven to reject over
  HTTP.
- One defect was found by an end-to-end test that every contract-level test had missed. Validation
  metadata written as `[property: ...]` on a record is rejected by ASP.NET Core at runtime, and a
  request for 5000 bills returned 500 rather than the governed 400. Recorded in the ADR-010
  addendum, fixed, and guarded by a Theory against regression.
- Controllers are fully defined and answer 501 where the service logic beneath them is a later
  section's deliverable. They never answer 404: the contract exists from this section onward.
