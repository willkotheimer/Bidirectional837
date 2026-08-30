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

### Section 2a — Findings register (2026-08-29)

- Added `docs/FINDINGS.md` at the project owner's request, so that findings can be reported after
  the fact without depending on a conversation transcript. Seven findings from Sections 1 and 2 are
  recorded, backfilled from the ADRs, commit messages and test evidence that held them until now.
- Findings and decisions are deliberately separated. A finding is something discovered about the
  system; a decision is what was chosen in response. They cross-reference each other.
- Extended `Governance.Traceability.Tests` so a finding marked as guarded must name the test that
  holds it shut (ADR-011). Recorded RED: 6 failed / 462 passed
  (`docs/tdd-evidence/section-2a-red.txt`). Recorded GREEN: 468 passed / 0 failed
  (`docs/tdd-evidence/section-2a-green.txt`).
- FIND-005 is the one finding carrying no guard, and is recorded as Open rather than quietly
  omitted: the inherited seed loader hides a missing charge file behind an existence check. Its
  guard arrives with its fix, in the Feature 1 section.

### Section 3 — Feature 1, the synthetic bill batch generator (2026-08-29)

- Implemented governance Feature 1: provider acquisition (User Story 1.1), procedure code and charge
  assignment (1.2), and bulk generation (1.3). The `POST /api/v1/bills/batch-generate` operation
  published in Section 2 now has an engine behind it, and `GET /api/v1/claims` and
  `GET /api/v1/claims/{id}` read what it produced.
- Recorded RED: 187 failed / 509 passed (`docs/tdd-evidence/section-3-red.txt`). Recorded GREEN:
  710 passed / 0 failed (`docs/tdd-evidence/section-3-green.txt`).
- Governed acceptance criteria are asserted as invariants over every generated claim rather than as
  spot checks: CLM02 equals the sum of its SV102 line amounts, codes match the HCPCS pattern and
  come from a requested category, every provider carries a check-digit-valid NPI in the requested
  jurisdiction, line numbers run sequentially from one, and a seed reproduces a batch exactly.
- The NPI check-digit rule is implemented in the domain, as a property of the governed
  Loop2010AA_NM109 field, and verified against the published worked example.
- Three findings recorded: FIND-008, invalid NPIs in the inherited provider seed, now corrected;
  FIND-009, a routing test that proved something weaker than it claimed; and FIND-010, the
  decomposition of the User Story 1.3 timing budget.
- FIND-005 and ADR-007 are closed. `seed/charges_sample.csv` now exists alongside
  `seed/hcpcs_categories.csv`, and a Theory reads the file names out of the Python loader itself so
  a reference without a file fails the build.
- Data provenance: the code catalog is 15 real HCPCS Level II codes across the three categories
  governance names. CPT is excluded as proprietary to the AMA, per the inherited Copilot notes.
  Descriptions are abbreviated; CMS remains the authoritative source. No PHI is involved: subscriber
  names are drawn from a synthetic list and no real patient data enters the system.
- The live NPI registry is queried by the deployed application and switched off in the test host, so
  the suite is deterministic and no governed timing budget is measured against a third-party
  service (ADR-012).

### Section 4 — Feature 2, the 837 export and archival engine (2026-08-30)

- Implemented governance Feature 2: EDI 837 serialisation (User Story 2.1) and ZIP packaging
  (User Story 2.2). `GET /api/v1/claims/export-zip`, published in Section 2, now has an engine
  behind it and no longer answers 501.
- Recorded RED: 383 failed / 741 passed (`docs/tdd-evidence/section-4-red.txt`). Recorded GREEN:
  1130 passed / 0 failed (`docs/tdd-evidence/section-4-green.txt`).
- The six segments User Story 2.1 names — ISA, GS, ST, BHT, CLM, SE — each have their own Theory
  asserted over the whole claim corpus. The 5010 syntax rules are asserted as structure rather than
  as spot checks: envelopes nest and agree at both ends, GE01 and IEA01 state true counts, SE01
  counts the segments it closes, segments arrive in guide order, and no element outside the ISA
  header carries a delimiter.
- Two invariants exist solely to protect the Section 1 Reversibility Guarantee, and they constrain
  the writer more than the standard does. Serialising the same claim twice must yield the same
  bytes, which forbids reading the clock or a counter for any element; and the database identity of
  a claim must not reach the stream, because an importer cannot recover a Guid. Both are recorded as
  ADR-016.
- The tests measure the writer with a reader written independently of the ingestion parser, which is
  a Feature 3 deliverable. A writer measured by a reader built to match it agrees with itself about
  any shared misreading of the standard and proves nothing about reversibility.
- Two findings recorded. FIND-011: a diagnosis code stored without its decimal point would
  round-trip into a different code, mitigated by refusing it rather than converting it, with the
  residual risk stated. FIND-012: the delimiter guard rejected ISA, the one segment whose purpose is
  to declare the delimiters — a test defect, recorded rather than quietly fixed, because the
  tempting repair was to make the writer wrong.
- The claim corpus moved to `tests/Governance.TestSupport` so the EDI, persistence and API suites
  measure against the same deterministic claims rather than against corpora that could drift apart.
- Verified outside the suite as well as inside it: the running application was driven end to end,
  a batch generated over HTTP and the archive downloaded and unpacked, and the emitted interchange
  read by eye against the guide.
- Data provenance: no new external data. No PHI: the corpus is synthetic and generated in process,
  and the trading partner identifiers in the envelope are constants of this build, disclosed in
  ADR-016.

### Section 4a — Executive summary of the TDD evidence (2026-08-30)

- At the project owner's request, the raw console output of each test run is no longer versioned.
  `docs/tdd-evidence/` is ignored, its five sections of accumulated log removed from the tree, and
  `docs/TDD-EVIDENCE.md` carries the summary: per section, the RED and GREEN counts, the commit
  whose tree was observed failing, and what the failing run proved. Recorded as ADR-020.
- The evidence was never carried by those files alone, and is not weakened. The git history holds
  the same fact independently — a `test(section-N)` commit whose tree fails, then a
  `feat(section-N)` commit whose tree passes — and either can be checked out and re-run. What is
  lost is the convenience of re-reading an old run without re-running it, which is accepted.
- Because a hand-written summary can drift where a log could not, it is enforced.
  `TddEvidenceTheories` fails the build on a RED run recorded with no failures, a GREEN run with
  any, a suite that shrank between the two, or a section that reaches either register without
  recording a run at all. That last check is what stops a section from escaping the others by
  omitting its row.
- Recorded RED: 6 failed / 1135 passed. Recorded GREEN: 1163 passed / 0 failed. Both are in
  `docs/TDD-EVIDENCE.md`, which is now where they live.
- One finding: FIND-013, the self-reference. The RED run asserted that each row names both its RED
  and its GREEN commit, and no commit can contain its own hash. The column is removed rather than
  filled with something plausible.
- Two pointers in `docs/DECISIONS.md` that named the deleted `docs/tdd-evidence/README.md` and
  directory now name `docs/TDD-EVIDENCE.md`. The decisions themselves are unchanged; a register with
  dead pointers is worse than one with corrected ones.

### Section 5 — Feature 3, ingestion, parsing and the reversibility proof (2026-08-30)

- Implemented governance Feature 3: EDI ingestion and translation (User Story 3.1) and zero-mutation
  verification (User Story 3.2). `POST /api/v1/claims/import` and
  `POST /api/v1/claims/{id}/verify-reversibility` now have engines behind them. No published
  operation answers 501 any longer.
- Recorded RED: 191 failed / 1185 passed. Recorded GREEN: 1393 passed / 0 failed. Both are in
  `docs/TDD-EVIDENCE.md`, and the evidence guard caught a bookkeeping error in the GREEN count
  before it was committed.
- The governance Section 4 Roundtrip Reversibility Test Standard is satisfied at its strict reading:
  Import then Export reproduces the original interchange byte for byte, over every claim in the
  corpus, and re-importing the regenerated file yields an identical record. The strict reading is
  available because the writer is a pure function of the record (ADR-016).
- The reader takes its delimiters from the ISA segment positionally, as the standard directs, so an
  interchange written with any delimiter set is read by the ones it declares rather than the ones
  this build happens to emit.
- Two decisions: ADR-021, the reader refuses what it cannot map exactly rather than salvaging part
  of it; ADR-022, an import applies whole or not at all.
- Three findings, and two of them were only visible because a test was strict. FIND-014: a governed
  decimal has no canonical scale in memory, so the verifier reported representation as mutation.
  FIND-015: the malformed-file suite was passing for a reason unrelated to the damage it applied,
  and its own repair helper was matching a segment identifier inside the receiver name — twelve
  Theories that had never tested what they claimed. FIND-016: problem documents had been served as
  `application/json` since Section 2, because a class-level `[Produces]` is a result filter rather
  than documentation, and nothing had ever asserted a response media type.
- Data provenance: no new external data, no PHI. The corpus remains synthetic and in-process.

### React client requirements recorded (2026-08-30)

- The project owner set out the React client requirements during the Section 5 work. They are
  recorded in `docs/UI-REQUIREMENTS.md` rather than left in the conversation that produced them,
  which is the same reason `docs/FINDINGS.md` exists (ADR-011).
- They are requirements, not decisions, so no ADR is raised by the document itself. Two gaps are
  recorded in it: the medical code catalog and the jurisdiction state list are both needed by the
  form and neither is published by `docs/api/swagger.json`. Both are routes governance does not
  name, so both need a register entry under the ADR-009 convention before they are added.
- ADR-001 deferred the client until the contracts were frozen. They are: as of Section 5 every
  published operation has an engine behind it and none answers 501.
