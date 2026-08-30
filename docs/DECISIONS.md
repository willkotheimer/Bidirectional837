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
| [ADR-007](#adr-007) | Inherited seed tooling retained, `charges_sample.csv` outstanding | Closed by ADR-013 | 1 | not applicable |
| [ADR-008](#adr-008) | Decision provenance is marked in code and enforced by test | Accepted | 2 | required |
| [ADR-009](#adr-009) | Routes governance does not name | Accepted | 2 | required |
| [ADR-010](#adr-010) | Validation annotations added to the governed DTOs | Accepted | 2 | required |
| [ADR-011](#adr-011) | Findings are recorded in a durable register and guarded | Accepted | 2 | required |
| [ADR-012](#adr-012) | NPI registry live by default, off in tests | Accepted | 3 | required |
| [ADR-013](#adr-013) | Curated public HCPCS Level II catalog, no CPT | Accepted | 3 | required |
| [ADR-014](#adr-014) | Generation is seeded and reproducible | Accepted | 3 | required |
| [ADR-015](#adr-015) | The ephemeral store is a singleton | Accepted | 3 | required |
| [ADR-016](#adr-016) | Ungoverned 837 elements are constants, never ambient state | Accepted | 4 | required |
| [ADR-017](#adr-017) | One 837 file per claim, named for its control number | Accepted | 4 | required |
| [ADR-018](#adr-018) | X12 numerics are rendered without scale and read back at it | Accepted | 4 | required |
| [ADR-019](#adr-019) | ICD-10 codes are stored dotted and emitted undotted | Accepted | 4 | required |
| [ADR-020](#adr-020) | Run logs are summarised in the repository, not versioned | Accepted | 4a | required |
| [ADR-021](#adr-021) | The reader refuses what it cannot map exactly | Accepted | 5 | required |
| [ADR-022](#adr-022) | An import applies whole or not at all | Accepted | 5 | required |
| [ADR-023](#adr-023) | Provider data comes from a distilled NPPES snapshot | Accepted | 6 | required |

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
RED run. This is recorded in `docs/TDD-EVIDENCE.md` and in the Section 1 pull request. The
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
Section 4 is legible in history as well as in `docs/TDD-EVIDENCE.md`.

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

## ADR-012

**The live NPI registry is queried by default and switched off in the test host.**
Constraint set by the project owner; mechanism chosen by Claude Opus 5, 2026-08-29.

Governance User Story 1.1 asks the generator to query the open-source NPI registry using the
requested jurisdiction state, and requires that an unreachable API fall back gracefully to a mock
state-compliant provider set.

The deployed application does query the registry: `Generation:UseLiveNpiRegistry` defaults to true,
so the governed intent holds where it matters. The test host sets it to false. Three reasons, in
order of weight. A suite that calls a third-party service is not reproducible, and governance
Section 4 requires recorded, repeatable runs. The User Story 1.3 timing budget of 3.0 seconds for
500 bills would otherwise be measured partly against network latency outside our control. And CI
must not depend on an external service being up.

The live path is not left untested. `NpiRegistryProviderDirectory` is exercised against a stubbed
transport, including the mapping of a real registry response onto the governed Loop2010AA fields.
The fallback is exercised across every way the registry can fail in practice: 503, 500, 404, an
empty result set, a body that will not parse, a DNS failure and a timeout. The governed phrase "if
API is unreachable" is read broadly on purpose, because each of those leaves the generator without
a provider.

`ResilientProviderDirectory` sets the registry aside for its own lifetime after the first failure.
A batch of 500 claims must not become 500 failed network calls, each waiting out a timeout.

## ADR-013

**The medical code catalog is a curated public HCPCS Level II set; CPT is excluded.**
Claude Opus 5, 2026-08-29.

Governance User Story 1.2 asks for valid medical codes from selected categories carrying published
standard charges or deterministic fallback charges, and governance Section 2 comments the
`SV101_2_ProcedureCode` column as "CPT / HCPCS Code".

CPT is proprietary to the AMA, as the inherited provenance notes from GitHub Copilot record. The
catalog therefore holds HCPCS Level II codes only: one letter followed by four digits, within the
governed five-character limit. Fifteen real codes across the three categories governance names as
examples, being Anesthesia, Physical Therapy and Cardiac. This resolves FIND-005, which recorded
that `seed/charges_sample.csv` was referenced in three places and absent from the repository.

Charges are published-style values for the catalogued codes, with a deterministic fallback for any
code outside it, which governance explicitly permits. The fallback derives the charge from the code
text using a stable accumulator rather than `string.GetHashCode`, because .NET randomises string
hashing per process: a hash-derived charge would differ between runs and destroy the reproducibility
ADR-014 depends on. The same reasoning governs the synthetic provider set.

The seed corpus is embedded in the assembly rather than copied beside it, so a deployed instance
cannot start with an empty catalog.

*Limitation, recorded honestly:* fifteen codes is a demonstration corpus, not a clinical one. The
descriptions are abbreviated and the authoritative source remains CMS. Widening the catalog is a
data task that needs no code change, and the seed integrity theories will hold the wider set to the
same shape.

## ADR-014

**Generation is seeded and reproducible.**
Claude Opus 5, 2026-08-29.

Governance User Story 1.2 asks the generator to pull codes *randomly*. Randomness and
reproducibility are not in tension here: the generator takes a seed, and the same seed yields a
byte-identical batch. The API supplies a fresh seed per request, so a client sees the variety
governance asks for.

Reproducibility is not a nicety. Governance Section 4 requires recorded failing runs, and a
generated claim that fails a downstream assertion is only investigable if the batch that produced it
can be regenerated exactly. Both directions are held by Theory: the same seed yields an identical
batch, and different seeds do not.

## ADR-015

**The ephemeral store is registered as a singleton.**
Claude Opus 5, 2026-08-29.

An in-memory SQLite database exists only while a connection to it is open, and `EphemeralClaimStore`
holds that connection for its own lifetime (ADR-002). Registered per request, the store would
therefore discard every claim at the end of the request that created it, and the Imported Bills
Dashboard, the ZIP export and the reversibility check would all read an empty database.

The store *is* the database in this topology, so its lifetime must be the application lifetime. The
consequence, which follows directly from the project owner decision to carry no durable persistence,
is that claims live until the process restarts and no further: a restart is a clean slate, and a
scaled-out deployment gives each instance its own claims. Both are recorded here rather than left to
be discovered, and both are carried into the Bicep deployment section.

## ADR-016

**Every 837 element governance does not store is a constant of the writer or a function of the
record, never ambient state.**
Claude Opus 5, 2026-08-30.

*Governed clause affected:* none is contradicted. Governance Section 2 defines the columns the
system stores; the 005010X222A2 guide requires elements it does not name, and governance is silent
on where those come from.

The 837 needs a submitter, a receiver, an interchange control number, an interchange date and time,
a subscriber member identifier and several structural qualifiers. Governance stores none of them.
There are three ways to supply such an element: read it from the environment, count it, or fix it.
Only the third is compatible with the Section 1 Zero-Mutation Rule.

A control number drawn from a counter, or an interchange timestamp read from the clock, would make
the same unedited claim export differently on every call. The governed round-trip test would then
fail against itself, and no parser however faithful could repair it, because the varying element is
not recoverable from the record. So the interchange date and time are derived from the stored
BHT04, the control numbers are fixed, and the trading partner identifiers are constants a
deployment overrides.

The same reasoning forbids the opposite mistake. The database identity of a claim is a storage
artefact with no 837 counterpart, so an importer cannot recover it; letting it reach the stream
would mean a re-imported claim could never re-export to the file it came from.
`Storage_identity_does_not_reach_the_stream` holds both halves: no identifier appears in the text,
and the same governed content under fresh identities serialises identically.

*Limitation, recorded honestly:* the subscriber member identifier in Loop 2010BA NM109 is a
required element with no governed column behind it, and is emitted as the claim control number. It
is unique per claim and stable under re-export, which is what reversibility needs, but it is a
placeholder and not a real member identifier. A deployment that carries member identifiers needs a
governed column for one, which is a Section 2 change and therefore the project owner's to make.

## ADR-017

**The archive holds one 837 file per claim, named for its governed claim control number.**
Claude Opus 5, 2026-08-30.

Governance User Story 2.2 asks for "valid individual or batched 837 files matching the database
records", and explicitly permits either. One file per claim is chosen because it makes the Section 1
Reversibility Guarantee checkable at the granularity the guarantee is stated at: an 837 file
corresponds to exactly one stored record, so a mismatch names the claim it belongs to instead of
naming a batch.

Entry names lead with the control number, because that is what a user opening the archive is
looking for, and carry the storage identity after it. Two claims may legitimately share a control
number - a replacement claim carries the original's CLM01 under a different CLM05-3 frequency code -
and neither may overwrite the other in the archive.

Anything in the control number that a filesystem would read as structure is replaced before it
becomes a name. An archive is extracted onto someone else's machine, and a control number is a
governed free-text column, so a traversal or a path separator in one must not become a path.

Entry timestamps are fixed rather than taken from the clock, for the reason ADR-014 gives: the same
claims must package to the same bytes, or no export can be compared against an earlier one to show
that nothing changed.

## ADR-018

**X12 numeric elements are rendered without trailing zeros and read back at the governed scale.**
Claude Opus 5, 2026-08-30.

The 5010 guide asks that a numeric element carry no insignificant trailing zeros and no trailing
decimal point, so a charge of 1.00 is written `1`. Scale is therefore not recoverable from the text,
and must come from somewhere else.

It comes from the governed Section 2 column: `decimal(18,2)` for the two charge amounts,
`decimal(18,4)` for the service unit count. Reading restores that scale rather than the scale the
text happened to carry. This is the same defect FIND-002 recorded in the store, arriving at a
different boundary: 1.00 read back as 1 changes the text of the next file written from the record,
which is a Zero-Mutation violation even though every value compares equal.

Restoring the scale is done by adding a zero of that scale, because decimal addition takes the
larger of its operands' scales. `decimal.Round` does not do this - `decimal.Round(1m, 2)` is 1, not
1.00 - and reaching for it here would reintroduce FIND-002 exactly.

An element carrying more precision than the governed column can hold is refused rather than
rounded. Rounding it would store an amount the file does not state, which is the FIND-001 corruption
arriving through a different door: a well-formed file, a successful import, and the wrong money.

## ADR-019

**ICD-10-CM codes are stored with their decimal point and emitted without it.**
Claude Opus 5, 2026-08-30.

Governance Section 2 declares `HI01_2_PrincipalDiagnosisCode` at 10 characters and says nothing
about format. X12 forbids the decimal point in a diagnosis element, and the seed corpus and
generator both carry the dotted form the CDC publishes, so the two forms differ and one must be
converted.

The point always follows the third character of an ICD-10-CM code, so removing it and restoring it
are exact inverses and the conversion is safe - but only for a code that is in the dotted form to
begin with. FIND-011 records what happens otherwise. A code stored as `E119` would be emitted
unchanged and read back as `E11.9`, silently changing a clinical field while producing a perfectly
valid file.

The writer therefore refuses a code that is not in canonical form rather than converting it. A loud
failure on a malformed diagnosis is a better outcome than a quiet edit to one, and the refusal is
what makes the round trip provable rather than merely likely.

## ADR-020

**The raw console output of each test run is summarised in the repository rather than versioned.**
Requested by the project owner, 2026-08-30. Recorded by Claude Opus 5.

*Governed clause affected:* Section 4 requires that tests be observed failing before implementation
exists, and flags an implementation that passes on its first build "without a recorded failing test
run in CI" as a violation. The clause requires the run to be recorded. It does not say in what form,
and this decision changes the form.

Five sections of raw `dotnet test` output had accumulated to some eleven thousand lines of console
log, none of it read after the day it was produced, and it was growing faster than the code. The
project owner asked for an executive summary instead.

The evidence is not weakened, because it was never carried by those files alone. Three artefacts
carry it now. `docs/TDD-EVIDENCE.md` records, per section, how many tests failed at RED, how many
passed, and what the failing run proved. The git history carries the same fact independently and
cannot be edited after the fact without rewriting it: every section is a `test(section-N)` commit
whose tree fails, followed by a `feat(section-N)` commit whose tree passes, and either commit can be
checked out and run. Each pull request quotes its own run.

What is genuinely lost is the ability to re-read the console output of a run from months ago without
checking out its commit and re-running it. That is a real cost and it is accepted deliberately: the
output is reproducible from the tree that produced it, which is the property that made it evidence
in the first place.

Because a summary is hand-written where a log was mechanical, it is enforced.
`TddEvidenceTheories` fails the build on a recorded RED run with no failures, on a GREEN run with
any, on a suite that shrank between the two, and — the check that makes the others load-bearing —
on any section that reaches this register or the findings register without recording a run at all.
A section cannot escape the per-row checks by omitting its row.

*Consequence:* see FIND-013. The summary cannot record the hash of the commit that writes it, so the
GREEN commit is identified by convention rather than by hash.

## ADR-021

**The reader refuses anything it cannot map exactly, rather than salvaging what it can.**
Claude Opus 5, 2026-08-30.

Governance User Story 3.1 requires integration tests with malformed files, without saying what
should happen to one. Two readings are available: read what can be read, or read nothing.

The reader reads nothing. A partially read claim is worse than a rejected file, and the reason is
the Section 1 Reversibility Guarantee rather than tidiness. A partial claim reaches the store, and
from that moment it is indistinguishable from a claim that arrived whole. It re-exports as a
well-formed 837 that says something the sender never said, and every downstream check passes,
because the file is valid — it is simply not the file that was sent.

So the reader validates before it maps: the envelope must balance and its counts must be true, the
transaction must be an 837 Professional rather than an 835 remittance carrying CLM-like segments,
every required segment must be present, every element must be a value its governed column can hold,
and the claim total must agree with its own service lines. Each refusal names the segment at fault,
because governance requires malformed files to be *handled*, and a reader handed "input string was
not in a correct format" cannot act on it.

The CLM02 check deserves its own note. CLM02 and the SV102 amounts state the same fact twice, and
governance User Story 1.2 requires them to agree. A file where they disagree cannot be stored
without choosing which to believe, and either choice is a mutation the import introduced rather than
one it found.

## ADR-022

**An import applies whole or not at all.**
Claude Opus 5, 2026-08-30.

The whole payload is read and parsed before anything is written, and one unreadable file rejects the
upload entire.

The alternative — import what parses, report what did not — is superficially friendlier and is the
worse failure. A partially applied batch leaves the store holding claims the sender never
successfully sent, mixed indistinguishably with ones they did, and the sender's natural response is
to correct the bad file and upload the batch again, which duplicates everything that succeeded the
first time.

The response to a successful import is read back out of the store rather than built from the parsed
objects. The store is the layer FIND-001 and FIND-002 were found in, and a response assembled from
what went in would report a mutation the store had introduced as though it had not happened.

## ADR-023

**Provider data comes from a distilled NPPES snapshot, with the live registry behind it.**
Constraint set by the project owner; mechanism chosen by Claude Opus 5, 2026-08-30.

*Governed clause affected:* governance User Story 1.1 says "query the open-source NPI registry API
using the user-provided JurisdictionState". This departs from the mechanism while keeping what the
mechanism was for.

The bulk NPPES data dissemination file and the NPI registry API are the same data, published by the
same authority. The API is a query interface over the file. So a provider read out of the file is
not a substitute for a real provider; it *is* the real provider, and it satisfies the acceptance
criterion — "System retrieves valid NPI, Provider Name, and Physical Address" — more reliably than
the API path did, which FIND-017 shows had never satisfied it at all.

The reason for the change is arithmetic. The registry answers one provider per request. A governed
batch is 500 claims, so the live path was 500 round trips against the 3.0 second budget User Story
1.3 sets. The project owner accepted a 1.1 GB download to remove them, and the trade is heavily in
its favour: the download happens once, offline, and generation then makes no network calls at all.

Three tiers, most real first: the snapshot, then the live registry for jurisdictions the snapshot
does not carry, then the synthetic set that User Story 1.1 requires as a graceful fallback. The
registry is still queried, still on by default, and now sends a query the registry will actually
answer.

*What is committed:* `scripts/distill_providers.py` streams the 11.6 GB member out of the archive
without extracting it and writes `seed/providers_by_state.csv` — 3,120 real providers across 52
jurisdictions, 225 KB. The archive itself stays in `seed/full/`, which `.gitignore` excludes, and
the script re-derives the snapshot from any monthly file.

Every filter in the script is driven by the governance Section 2 column it feeds. A provider whose
name would not fit `Loop2010AA_NM103`, or whose address would not fit `N301`, is dropped rather than
truncated: a truncated name is not that provider's name, and storing one would put a falsehood in
the seed. Providers carrying an X12 delimiter are dropped for the same reason — the writer refuses
them, so they could never be serialised.

The check-digit rule is reimplemented in Python rather than shared with `Governance.Domain`, so the
seed and the domain agree by independent arrival. If they ever disagree, the seed integrity Theories
fail, which is the point of writing it twice.

*Limitation, recorded honestly:* the snapshot is a point-in-time copy and will age. Providers close,
move and are deactivated. It is regenerable in minutes from the current monthly file, the file it
came from is named in `docs/PROVENANCE.md`, and nothing about the build depends on it being fresh —
but it is a snapshot, not a live directory, and a demonstration corpus rather than a clinical one.
