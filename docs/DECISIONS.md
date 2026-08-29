# Decision Register

Every decision that departs from `governance.txt`, or that governance leaves open, is recorded
here with its justification. Governance is binding to the letter; where this register overrides it,
the entry states which governed clause is affected, why the letter of that clause could not be kept,
and what preserves its intent.

Authorship is recorded per entry. `governance.txt` itself was authored by Gemini AI; the seed
tooling, sample data and initial docs were authored by GitHub Copilot; entries below marked
*Claude Opus 5* were made during the completion of the build.

The **Code marker** column below is enforced, not advisory. Where it reads `required`, the decision
changes code, and every place the code embodies that decision carries a comment reading
`PROVENANCE: ADR-NNN`. `Governance.Traceability.Tests` fails the build if a required marker is
absent from the source, or if source carries a marker for an ADR that this register does not
define. The convention itself is ADR-008.

| ID | Decision | Status | Section | Code marker |
|----|----------|--------|---------|-------------|
| [ADR-001](#adr-001) | API and engine first; React deferred | Accepted | 1 | not applicable |
| [ADR-002](#adr-002) | Ephemeral SQLite replaces SQL Server | Accepted | 1 | required |
| [ADR-003](#adr-003) | Governed entities are a verbatim transcription | Accepted | 1 | required |
| [ADR-004](#adr-004) | Money stored as integer minor units | Accepted | 1 | required |
| [ADR-005](#adr-005) | String length enforcement moves to the API layer | Discharged by ADR-010 | 1 | required |
| [ADR-006](#adr-006) | One pull request per section, delivered by branch push | Accepted | 1 | not applicable |
| [ADR-007](#adr-007) | Inherited seed tooling retained, `charges_sample.csv` outstanding | Open | 1 | not applicable |
| [ADR-008](#adr-008) | Decision provenance is marked in code and enforced by test | Accepted | 2 | required |
| [ADR-009](#adr-009) | Routes governance does not name | Accepted | 2 | required |
| [ADR-010](#adr-010) | Validation annotations added to the governed DTOs | Accepted | 2 | required |
| [ADR-011](#adr-011) | Findings are recorded in a durable register and guarded | Accepted | 2 | required |

---

## ADR-001

**API and engine first; React deferred.**
Decided by the project owner, 2026-08-29. Recorded by Claude Opus 5.

Governance Section 1 names React state models as downstream views, and Feature 3 calls for an
Imported Bills Dashboard. The build order puts the .NET solution first: governed schema, OpenAPI
contracts, generation engine, 837 serializer, ingestion parser, reversibility proof, then Bicep
deployment. The React client follows once the contracts are frozen.

This follows the letter of governance Section 4, which requires API contracts to be fully defined
before the logic beneath them. A frontend built against unfrozen contracts would have to be rebuilt
when they settle.

## ADR-002

**The governed schema is hosted on ephemeral SQLite, not SQL Server.**
Constraint set by the project owner; mechanism chosen by Claude Opus 5, 2026-08-29.

*Governed clause affected:* Section 2 mandates an EF Core Code-First model. It does not name a
database engine, so no clause is contradicted; the entity model is used exactly as written.

The project owner has ruled out SQL Server on the grounds that the application needs no long-term
persistence. The store is therefore SQLite in shared-cache in-memory mode, opened per
`EphemeralClaimStore` instance and destroyed with it. Nothing reaches disk and nothing outlives the
process.

SQLite was chosen over the EF Core in-memory provider because that provider enforces no relational
constraints whatsoever. Referential integrity is load-bearing here: an orphaned service line would
round-trip into a claim that had silently lost a charge, defeating the Section 1 Reversibility
Guarantee. `Line_item_without_a_parent_claim_is_rejected` proves the constraint is live, and would
pass vacuously on the in-memory provider.

*Consequences:* see ADR-004 and ADR-005, both discovered by the tests this decision made possible.

## ADR-003

**`Entities/ClaimHeader.cs` is a verbatim transcription of governance Section 2, not a design
artefact.**
Claude Opus 5, 2026-08-29.

The entity classes are copied character for character from the governed schema, including its
comments and its declared column types. The file carries a header saying so. Any change to a
property name, type, nullability or length is a governance amendment and requires an entry here.

Because the transcription is not designed, it is not test-driven; the naming theories in
`SchemaContractTheories` exist to fail if it ever drifts, and consequently passed in the Section 1
RED run. This is recorded in `docs/tdd-evidence/README.md` and in the Section 1 pull request. The
implementation that *was* test-driven is the persistence layer beneath it.

## ADR-004

**Monetary and quantity columns are stored as exact integer minor units.**
Claude Opus 5, 2026-08-29. Discovered by a failing test.

*Governed clause affected:* Section 2 declares `[Column(TypeName = "decimal(18,2)")]` on
`CLM02_TotalClaimChargeAmount` and `SV102_LineItemChargeAmount`, and `decimal(18,4)` on
`SV104_ServiceUnitCount`. **These declarations are preserved verbatim.** Only the storage encoding
beneath them changes.

SQLite assigns NUMERIC affinity to a column declared `decimal(18,2)` and coerces the stored value to
a float. The Section 1 round-trip theories caught two distinct corruptions:

| Written | Read back | Effect |
|---------|-----------|--------|
| `9999999999999999.99` | `10000000000000000` | Charge amount silently altered |
| `1.00` | `1` | Trailing zeros dropped, changing rendered 837 amount text |
| `0.10` | `0.1` | As above |

Both violate the Zero-Mutation Rule. `ClaimsDbContext.OnModelCreating` therefore applies a value
converter scaling each decimal to integer minor units at its governed scale. Integers store
losslessly under NUMERIC affinity, and unlike a TEXT encoding they remain correctly orderable and
comparable in SQL, so future queries that filter or sort on an amount stay sound.

The read conversion multiplies by a literal of the target scale rather than dividing, because
decimal multiplication adds operand scales: `100 * 0.01m` is `1.00m` where `100m / 100m` is `1m`.
This restores the governed scale exactly, trailing zeros included.

*Side effect, intentional:* a value carrying more precision than its governed scale is rounded to
that scale on write, which is the behaviour a `decimal(18,2)` column in SQL Server would have had.
Amounts in the store are therefore canonical at the governed scale, which strengthens rather than
weakens byte-level reversibility.

## ADR-005

**Governed `StringLength` limits are enforced at the API validation layer, not by the store.**
Claude Opus 5, 2026-08-29. Empirically verified.

*Governed clause affected:* Section 2 declares a `[StringLength(n)]` on every governed string field.
SQL Server would have rejected an over-length value at insert. SQLite does not: a probe wrote 40
characters into `Loop2010AA_N402_BillingProviderState`, whose governed maximum is 2, and read all 40
back unchanged.

This is a direct consequence of ADR-002 and the one place where that decision genuinely weakens a
governed guarantee. The model metadata still declares every limit — `Governed_string_field_declares_the_governed_max_length`
proves all 21 of them — but the engine will not enforce them.

*Compensating control, owed by Section 2:* the DTO layer must carry the same `StringLength`
annotations and the API must reject violations with a 400 before anything reaches the store, with
Theory coverage over the full governed field table. Until that control lands, over-length data is
possible in principle. This entry stays open in the Section 2 pull request until the control exists.

## ADR-006

**One pull request per completed section, delivered by pushing a section branch.**
Decided by the project owner, 2026-08-29. Recorded by Claude Opus 5.

The GitHub CLI is not installed on the build machine, so each section is committed to a
`section-N/<topic>` branch, pushed to `origin`, and handed over as a compare link for the owner to
open. Each section contributes at least two commits: a `test(section-N)` commit whose tree fails,
and a `feat(section-N)` commit whose tree passes, so the Red-Green sequence required by governance
Section 4 is legible in history as well as in `docs/tdd-evidence/`.

## ADR-007

**The inherited Python seed tooling is retained; `charges_sample.csv` is outstanding.**
Claude Opus 5, 2026-08-29.

`scripts/fetch_and_convert.py` and `scripts/load_to_sqlite.py`, the `seed/` samples and the original
`docs/PROVENANCE.md` were authored by GitHub Copilot. They are retained: the acquisition and
licensing research they encode is sound, and Feature 1 needs code and charge reference data.

One inherited defect is recorded rather than fixed here. `seed/charges_sample.csv` is referenced by
`seed/README.md`, by `docs/PROVENANCE.md` and by `load_to_sqlite.py`, but does not exist. The loader
guards its absence with `if charges_path.exists()`, so it fails silently: the `charges` table is
created and left empty. Governance User Story 1.2 requires published standard charges or a
deterministic fallback, so this is resolved in the Feature 1 section, not before, and this entry
stays open until then.

## ADR-008

**Decision provenance is marked in code and the marking is enforced by test.**
Requested by the project owner, 2026-08-29. Recorded by Claude Opus 5.

A decision register that no one can trace back to the code it governs decays into a document
nobody reads. Every code element that embodies a decision therefore carries a comment of the form
`PROVENANCE: ADR-NNN`, and code that transcribes governance rather than deciding anything carries
`PROVENANCE: GOVERNANCE-N`, naming the governed section.

`Governance.Traceability.Tests` enforces both directions as Theories:

- every `ADR-NNN` marker in the source resolves to an entry in this register, so a marker cannot
  outlive the decision it cites;
- every register entry whose **Code marker** column reads `required` appears at least once in the
  source, so a decision cannot claim to change code without showing where.

The register's table is the single source of truth for which decisions are code-bearing. Marking a
decision `required` and then failing to mark the code breaks the build, which is the point: the
register cannot silently drift out of step with the system it governs.

## ADR-009

**Routes that governance does not name.**
Claude Opus 5, 2026-08-29.

Governance names two routes explicitly: `POST /api/v1/bills/batch-generate` (User Story 1.3) and
`GET /api/v1/claims/export-zip` (User Story 2.2). Both are used verbatim.

User Stories 3.1 and 3.2 require ingestion and reversibility verification but name no route, and the
Imported Bills Dashboard needs a way to read what has been ingested. Four routes are therefore
added, following the versioning and pluralisation already established by the two governed routes:

| Route | Serves | Rationale |
|-------|--------|-----------|
| `POST /api/v1/claims/import` | User Story 3.1 | Accepts a single 837 file or a ZIP of them, mirroring the export route's packaging |
| `GET /api/v1/claims` | User Story 3.1 | Backs the Imported Bills Dashboard listing |
| `GET /api/v1/claims/{id}` | User Story 3.1 | Retrieval of one reconstructed claim |
| `POST /api/v1/claims/{id}/verify-reversibility` | User Story 3.2 | Re-exports and re-imports a claim and reports the verdict; POST rather than GET because the operation performs a full round trip rather than reading state |

These are additions to the governed surface, not deviations from it: no governed route is changed,
renamed or removed. The authored contract in `docs/api/swagger.json` is the full published surface,
and `Governance.Api.Tests` asserts the application exposes exactly the paths that contract declares
and no others.

`ReversibilityReportDto` is a new response contract with no counterpart in governance Section 3. It
carries no claim field; it reports a verdict about a claim (`EdiTextIsIdentical`,
`RecordIsIdentical`, and the differences found). Governance Section 3 governs the shape of claim
DTOs, so a verdict object does not deviate from it, but it is recorded here because User Story 3.2
specifies the assertion without specifying how the result is surfaced.

## ADR-010

**Validation annotations are added to the governed DTOs, discharging ADR-005.**
Claude Opus 5, 2026-08-29.

*Governed clause affected:* Section 3 states that the DTO must match the schema and 837 mappings
directly, and that custom field additions or deviations require explicit documentation and architect
approval. This entry is that documentation; approval is sought through the Section 2 pull request.

ADR-005 established that the ephemeral SQLite store cannot enforce the Section 2 `StringLength`
limits, and made the API layer responsible for them. The governed Section 3 text carries no
validation metadata, so the annotations are an addition to it.

The addition is strictly non-structural. **No field is added, removed, renamed or retyped.** Every
annotation mirrors a limit that governance Section 2 already states for the corresponding column,
and `Governance.Contracts.Tests` proves the mirroring rather than trusting it: a Theory walks the
governed entity properties and asserts that each DTO property of the same name carries the same
maximum length and the same optionality. A limit that drifts from Section 2 fails the build.

`BatchGenerationRequestDto.BillCount` additionally carries the range 1 to 500. The upper bound is
governed directly - User Story 1.3 requires that a request above 500 return 400 Bad Request. The
lower bound of 1 is an addition: a request for zero or a negative number of bills has no meaningful
result, and rejecting it at the contract boundary is cheaper than defining what an empty batch
means. `JurisdictionState` is constrained to exactly 2 characters, matching the governed
`Loop2010AA_N402_BillingProviderState` column it ultimately populates.

Annotations rather than an `IValidatableObject` implementation were chosen so the limits appear in
the published OpenAPI schema as `maxLength`, making the contract self-describing to clients. A
validation method would have kept the Section 3 text character-identical but left the published
contract silent about the very limits ADR-005 made the API responsible for.

### ADR-010 addendum: annotations bind to the constructor parameter, not the property

Recorded 2026-08-29 after an end-to-end test caught the mistake.

The annotations were first written as `[property: Required, StringLength(n)]`, which keeps the
governed Section 3 text closer to verbatim by attaching metadata to the generated property rather
than to the parameter list. In-process validation with `Validator.TryValidateObject` accepted this
and every contract-level Theory passed.

The framework does not. ASP.NET Core reads record validation metadata from the primary constructor
parameter, and on encountering it on a generated property it throws:

> Record type 'BatchGenerationRequestDto' has validation metadata defined on property
> 'MedicalCodeCategories' that will be ignored. 'MedicalCodeCategories' is a parameter in the record
> primary constructor and validation metadata must be associated with the constructor parameter.

The consequence was worse than a failed validation: the request died with a 500 before the governed
ceiling was ever evaluated, so a request for 5000 bills was answered with an internal error instead
of the 400 User Story 1.3 requires. A contract-level test alone would have shipped this.

Three changes follow. The annotations now sit on the constructor parameters.
`ContractValidationTheories` reads metadata from the parameters, which is where the framework reads
it, so the suite can no longer pass against metadata the framework ignores. And a Theory,
`Contract_declares_no_validation_metadata_on_a_generated_property`, fails the build if any governed
contract regresses to the property form.

The lesson is recorded rather than quietly fixed: a control proven only at the layer that declares
it is not proven. `Batch_generation_rejects_an_over_length_jurisdiction_state` now exercises the
control over HTTP, through the real pipeline.

## ADR-011

**Findings are recorded in a durable register, and a fixed finding is held shut by a marked test.**
Requested by the project owner, 2026-08-29. Recorded by Claude Opus 5.

Findings were previously scattered: some in ADRs, some in commit messages, some only in the
conversation that produced them. The project owner asked that they be recorded so they can be
reported later even if that conversation is gone.

`docs/FINDINGS.md` is therefore the durable register. A **finding** is a fact about the system that
was discovered; a **decision** about what to do next is an ADR. Most findings name the ADR that
resolved them, and most code-bearing ADRs name the finding that forced them.

Each entry records what was found, why it matters in terms of the governed guarantees, how it was
discovered, what resolved it, and any residual risk. Severity is judged by consequence to the
Section 1 Reversibility Guarantee and to data correctness, not by effort to fix. A finding recorded
as Mitigated rather than Fixed states its residual risk explicitly; FIND-003 is the current example.

The register's **Guard** column is enforced by `Governance.Traceability.Tests` on the same contract
ADR-008 applies to decisions. A finding marked `required` must name the test that holds it shut,
through a `PROVENANCE: FIND-NNN` comment on that test, and a marker must not cite a finding the
register does not define. A fix that is not guarded is a fix that can silently regress, which for
FIND-001 would mean returning to silently corrupted money.
