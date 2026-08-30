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
- Transcribed the governance Section 2 entity model verbatim into `src/Translator.Domain/Entities`
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
`tests/Translator.Domain.Tests/Corpus/GovernedClaimCorpus.cs`.

### Section 2 — API contracts, DTO validation and decision traceability (2026-08-29)

- Authored `docs/api/swagger.json` before writing any controller, per governance Section 4. It is
  the published surface, and `Translator.Api.Tests` measures the application against it rather than
  the reverse. Governance names two routes; the four it does not name are recorded in ADR-009.
- Transcribed the governance Section 3 DTOs into `src/Translator.Contracts`.
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
- The claim corpus moved to `tests/Translator.TestSupport` so the EDI, persistence and API suites
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

### Section 6 — Provider data from a distilled NPPES snapshot (2026-08-30)

- The project owner accepted a 1.1 GB download to remove per-claim provider lookups. Recorded as
  ADR-023: the snapshot leads, the live registry sits behind it for jurisdictions it does not carry,
  and the synthetic set stands behind both.
- Two findings, and the first is the most serious of the build so far. FIND-017: the live NPI
  registry query the application had sent since Section 3 is one the registry refuses, using HTTP
  200 and an error body, so a deployed instance had never once retrieved a real provider and had
  silently used the synthetic set for every claim. FIND-018: the latching fallback policy, correct
  for the registry, would have disabled the new local snapshot after a single miss.
- Both were invisible to a green suite. FIND-017 survived because every Theory over the registry
  client answers a stub written to match our expectations rather than the service; the fix asserts
  the *request* the client builds, which nothing had done. FIND-018 was found by reading the
  fallback policy against a new kind of primary, and a test was written for it before the fix.
- Recorded RED: 383 failed / 1413 passed. Recorded GREEN: see `docs/TDD-EVIDENCE.md`.

Data sources added in this section:

- **NPPES Data Dissemination, August 2026 V2** — `NPPES_Data_Dissemination_August_2026_V2.zip`,
  1,151,460,412 bytes, from https://download.cms.gov/nppes/NPI_Files.html. Public domain, published
  by CMS. Not committed; it lives in `seed/full/`, which `.gitignore` excludes.
- **`seed/providers_by_state.csv`** — 3,120 real providers across 52 jurisdictions, distilled from
  the above by `scripts/distill_providers.py`. No PHI: these are provider business records, which
  CMS publishes precisely so they can be looked up, not patient data. Subscriber names in generated
  claims remain synthetic.
- The snapshot is point-in-time and will age. It is regenerable in minutes from any monthly file,
  and nothing in the build depends on it being current.

### Section 7 — Priced code catalogue and the routes that serve it (2026-08-30)

- The catalogue is now built backwards from the published CMS fee schedules, at the project owner's
  direction: a code enters only because a schedule prices it, so nothing has to be assumed to exist
  (ADR-024). 980 codes across eight categories replace the fifteen hand-curated ones, with all three
  categories governance names as examples populated.
- `GET /api/v1/codes` and `GET /api/v1/jurisdictions` publish the catalogue and the states a provider
  can be sourced for, so the generation form is built against the server's own vocabulary rather than
  a copy that would drift (ADR-025).
- The project owner framed the whole corpus explicitly: this is **example data**. The charges are
  real published figures of the right order of magnitude, which is precisely why the caveat matters —
  they look like prices, and they are not what any provider bills. The UI is required to say so where
  a user can see it, recorded in `docs/UI-REQUIREMENTS.md`.
- One finding, and two defects caught by inspection before they shipped. FIND-019: the seed reader
  split on every comma and documented the assumption that seed files carried no quoted fields — true
  of fifteen hand-written rows, false as soon as the catalogue held CMS prose. The charge path
  survived only because price is the last column. Separately, and caught while reviewing the
  distillation output rather than by a test: a code priced at $0.00 because a tiny positive RVU rounds
  to nothing, and codes filed under the wrong category because a keyword matched an incidental
  mention late in a long description rather than its subject.
- FIND-010 re-measured. 500 bills over HTTP now run 0.99–1.08 s against the governed 3.0 s, against
  1.6–2.4 s before Section 6. Kept Mitigated rather than closed, because the budget is still spent on
  work governance does not name.

Data sources added in this section, all public domain and all HCPCS Level II only:

- **HCPCS Level II, October 2026** — `october-2026-alpha-numeric-hcpcs-file.zip`, from
  https://www.cms.gov/medicare/coding-billing/healthcare-common-procedure-system/quarterly-update.
  8,769 described codes; supplies the authoritative long descriptions.
- **Physician Fee Schedule RVUs, 2026 Q4** — `rvu26d-updated-08-26-2026.zip`, from
  https://www.cms.gov/medicare/payment/fee-schedules/physician/pfs-relative-value-files. 544 priced
  Level II services. Charge = total RVU x the CY 2026 non-qualifying-APM conversion factor of
  $33.40 (CMS-1832-F); the RVU member read is the nonQPP file, so that is the factor matching it.
- **DMEPOS Fee Schedule, January 2026** — `dme26.zip`, from
  https://www.cms.gov/medicare/payment/fee-schedules/dmepos/dmepos-fee-schedule/dme26. 1,265 priced
  Level II items, at the published national ceiling.
- **Medicare Part B payment limits (ASP), July 2026** — `july-2026-medicare-part-b-payment-limit-files.zip`,
  from https://www.cms.gov/medicare/payment/part-b-drugs/asp-pricing-files. 855 priced Level II drugs.

None of the four archives is committed; they live in `seed/full/`, which `.gitignore` excludes, and
`scripts/distill_codes.py` re-derives `seed/hcpcs_categories.csv` and `seed/charges_sample.csv` from
them. CPT is never read: Level I is five digits and AMA copyright, the D series is CDT and ADA
copyright, and both exclusions are asserted by `CatalogIntegrityTheories` rather than remembered.

### Section 7a — Governance names the guardrails; the application is Translator (2026-08-30)

- The project owner asked why every namespace was `Governance` rather than the application's own
  name, and set the rule: if the application itself is called Governance that is wrong, and if
  Governance means the development guardrails then that is what it should mean.
- The origin was not a design choice. governance.txt declares `namespace Governance.Domain.Entities`
  and `namespace Governance.Contracts.DTOs` as literal text in its mandatory schema and DTOs, ADR-003
  transcribed them character for character as instructed, and every later project took the same root
  for consistency. The application ended up named after the document governing it.
- Renamed: the five application projects, their five test projects, and the shared test support
  library. Kept: `Governance.Traceability.Tests`, which reads the registers and measures the code
  against them - it is the governance, not the translator.
- Recorded as ADR-026, which is an explicit departure from the letter of governance Sections 2 and 3.
  ADR-003 is amended rather than left standing, because it claimed a character-for-character
  transcription and that claim now has exactly one exception: the namespace line. Every field name,
  type, length and column mapping in the governed schema is untouched.
- The rule is enforced by `NamespaceTheories` rather than agreed, including one Theory whose only job
  is to catch a half-done rename - an application file still referencing a Governance-rooted type
  would compile until the old assembly stopped being produced.
- No behaviour changed. Recorded RED: 119 failed / 1902 passed, the failures being the naming rule
  itself. Recorded GREEN: see `docs/TDD-EVIDENCE.md`.

### Frontend governance approved (2026-08-30)

- `docs/GOVERNANCE-FRONTEND.md` was drafted at the project owner's request and approved by them the
  same day. It is binding from that point. It governs the React client only, derives its authority
  from the two clauses of `governance.txt` Section 1 that already bind the frontend, and cannot amend
  `governance.txt` - departures from either remain ADRs.
- Recorded alongside it: governance Feature 3's two acceptance criteria are both already met by
  Section 5. User Story 3.2 asks for an automated test rather than a screen, and that test is green.
  The word "Dashboard" appears only in the feature title and in User Story 3.1's "so that they
  display on the Imported Bills Dashboard", which the 837 to Model tab satisfies. Surfacing the
  per-claim reversibility verdict in the UI is therefore an open choice, not an unmet requirement.

### Section 8 — Frontend governance, and the wire-naming defect it found (2026-08-30)

- `docs/GOVERNANCE-FRONTEND.md` was drafted, approved and adopted (ADR-027). CORS is granted to the
  development client and nowhere else (ADR-028). The marker convention now reaches `.ts` and `.tsx`,
  so the client is inside the governance rather than beside it.
- Three findings, and the section changed character twice as they appeared.

**The bidirectional round-trip test, and what it caught.**

The project owner asked for a single integration test carrying a bill out to an 837 and back, and
offered the other direction as optional. Both were written. They found two defects within minutes of
each other that the existing suite could not have found, and the reason is worth recording as much as
the defects are.

Every prior round-trip test measured one host against itself. `ReversibilityTheories` serialises and
re-parses in memory; the API's reversibility check asks a host whether *its own* stored record
survives export and re-import. Both are true and both are blind in the same way: a value handled
identically in each direction satisfies them. So does a value that two endpoints of the same host
disagree about, because no test used two endpoints.

The journey Theory generates on one host, exports, imports into a second, and compares every governed
column of the claim that arrived against the claim that left. That is the first test in this build
that ever carried a claim across a boundary and looked back.

It failed twice, immediately:

| Field | Sent | Arrived | Cause |
|-------|------|---------|-------|
| `BHT04_TransactionSetCreationDate` | `2026-01-01T00:00:00Z` | `2026-01-01T00:00:00` | FIND-021 — SQLite has no timestamp type, so the store returned the same instant with its `Kind` lost |
| `SV104_ServiceUnitCount` | `2` | `2.0000` | FIND-022 — batch generation returned the objects it made; import returned what the store held, and only the store applies the governed scale |

Neither is visible from inside one host. Both hosts were internally consistent; the disagreement
existed only between them. FIND-022 is the more serious: it is FIND-014's recorded residual risk
arriving exactly where that entry predicted, and it meant a client could not compare a generated
claim against an imported one — which is the single thing a two-tab translator UI is for. A
difference in representation would have read as a mutation.

The reverse direction, 837 to bill and back, passed on its first run. That is worth stating plainly
rather than quietly: governance Section 4's roundtrip standard already held end to end through the
published API, and had only ever been proven against the engines beneath it.

- FIND-020 is fixed: the naming policy is null, so every DTO serialises with the name it declares.
  The mangled names had been read by the conformance suite's own Theories since Section 2, which made
  that suite a record of the defect rather than a guard against it. Fixing it is what made FIND-021
  and FIND-022 visible at all.

### Section 9 — The client: scaffold, helpers, data layer, Model → 837 (2026-08-30)

- `src/Translator.UI` exists: Vite, React, TypeScript, Vitest, TanStack Query and Formik, governed by
  the now-binding `docs/GOVERNANCE-FRONTEND.md`.
- The helpers carry the testable logic, as Section 4a requires, and hold most of the suite: the CSV
  writer, the governed-column formatting, the problem-document reader and the local store. Every one
  is driven by `it.each` over a table of variants, which is `[Theory]` with `MemberData` in another
  language.
- The data layer is the only code that talks to the server, and its tests fake the network rather
  than the module under test - a test that stubbed `fetchMedicalCodes` would prove the caller renders
  what it was handed and nothing about whether it asked the right question.
- The Model → 837 tab generates, tables and exports. The 837 → Model tab is an honest placeholder.
- Verified running: both servers up, the catalogue reaching the client across origins, 980 codes
  served, and the CORS grant confirmed to answer the Vite origin and refuse another.
- Three things the register's own history shaped: the CSV writer quotes because FIND-019 was that
  defect facing the other way, amounts render at the governed scale because FIND-022 was that
  difference in representation, and the fixtures carry unmangled ASC X12 names because a fixture
  written against `clM02_` would have hidden FIND-020 from the client as the backend suite hid it
  from the server.

### Header image (2026-08-30)

- `src/Translator.UI/public/clinic.webp` — a clinician taking a patient's blood pressure. Generated
  with Google Gemini by the project owner, supplied as a 1408x768 JFIF and converted here to WebP
  (818 KB to 110 KB, quality 82).
- It depicts no real person and no real clinical encounter. It is not licensed stock, so there is no
  licence to carry, which is why this route was preferred to a photograph of real people.
- It is captioned on its own face as generated (ADR-030), because the page spends a paragraph
  explaining that its data describes nothing that happened and a photorealistic image works against
  that unless it says what it is.
- Three near-identical variants were supplied; the one showing the room's privacy notice was the
  one the project owner asked for.

### Section 10 — The 837 → Model uploader, the reversibility verdict, and colour (2026-08-30)

- The import direction is built. A single 837 or a ZIP of them is uploaded as multipart, the
  reconstructed bills are tabled, and a refusal shows the server's own words while leaving the
  existing table untouched, as ADR-022 requires.
- Each row carries a **Verify** button rather than a batch summary. There is no bulk verify endpoint,
  so verifying a table would be one request per claim - the anti-pattern ADR-023 cost a section to
  remove - and a Theory asserts no verification request is made until one is asked for.
- The verdict is two pills, never one. The endpoint compares a stored record against its own
  re-export and never sees the bytes the user uploaded, so a claim can be perfectly preserved while
  the text differs. Colour reinforces the words and never replaces them.
- Styling is Tailwind, and only Tailwind. shadcn is a CLI that copies Tailwind-written source into a
  project rather than a component library, so choosing it chooses Tailwind; Bootstrap alongside would
  mean two resets fighting and colliding class names. `components/ui.tsx` follows the shadcn
  convention - component source living in the repository - at about a hundred lines, without the
  Radix dependency a generated set would bring. Native `select` elements are kept deliberately: Radix
  cannot do the multi-select the category picker needs.
- Two styling defects were found by reading the compiled CSS rather than the source, which is the
  only reason either surfaced. Both are recorded in `docs/TDD-EVIDENCE.md`.

Image added in this section:

- **`src/Translator.UI/public/clinic.webp`** — a close-up of a clinician measuring a patient's blood
  pressure with a manual sphygmomanometer. Supplied by the project owner as `licensed-image.jfif`,
  2048x1366, converted here to WebP at 1600px wide (174 KB to 53 KB, quality 82).
- Nobody in it is identifiable - the frame shows hands and an instrument - and no product branding is
  legible. It is captioned on its own face as a stock photograph illustrating nothing on the page
  (ADR-030).
- **Licence: not recorded, and owed.** The filename says it is licensed and the project owner
  supplied it as such, but the terms, the library and the licence identifier are not derivable from
  the file and are not written down here. Every other byte of data in this repository names its
  source; this one does not yet. It should be completed before anything is published.
- An earlier generated image was used first and replaced. Its caption said "Illustration, generated"
  and would have been false of this one, so it was rewritten rather than carried over.

### Section 11 — One host for the client and the API, declared in Bicep (2026-08-30)

- Deployed and verified live at https://bidirectional837.azurewebsites.net. The full governed loop
  was exercised against it: generate, export, import, verify - the reversibility verdict returning
  text identical, record identical, no differences, against a real Ohio provider drawn from the NPPES
  snapshot.
- One App Service serves the client and the API. The topology changed mid-section: a Static Web App
  plus an App Service was built first, and the project owner observed that an App Service takes the
  name you give it while a Static Web App's hostname is generated. That was the better point, and it
  had a consequence beyond the address - one origin makes ADR-028's original reasoning true as
  written, so there is no cross-origin grant anywhere (ADR-031, ADR-032).
- No recurring cost. The App Service plan already existed, already hosted three applications, and is
  referenced rather than created - deleting this deployment must not take another application down.
- A SPA fallback was written and removed. It answered every unrecognised path with the client shell,
  which broke `Route_outside_the_published_contract_is_not_served` - a guard that has held the
  published surface since Section 2. The client has no router and needs no fallback, so the
  nine-section-old assertion outranked the convenience.
- ADR-015 is now felt rather than theoretical. Always On is set, without which the platform idles the
  app out and a generated batch disappears between two clicks. That does not make the store durable;
  it moves the loss from twenty minutes of inactivity to a restart, and the limit is recorded.

**Outstanding, and worth raising:** `dotnet publish` warns that `Microsoft.OpenApi 2.0.0` carries a
known high-severity advisory (GHSA-v5pm-xwqc-g5wc). It has warned locally since Section 2; it now
sits on a public URL, which changes what it means. It should be upgraded.
