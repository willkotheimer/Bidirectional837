# Decision Register

Every decision that departs from `governance.txt`, or that governance leaves open, is recorded
here with its justification. Governance is binding to the letter; where this register overrides it,
the entry states which governed clause is affected, why the letter of that clause could not be kept,
and what preserves its intent.

Authorship is recorded per entry. `governance.txt` itself was authored by Gemini AI; the seed
tooling, sample data and initial docs were authored by GitHub Copilot; entries below marked
*Claude Opus 5* were made during the completion of the build.

| ID | Decision | Status | Section |
|----|----------|--------|---------|
| [ADR-001](#adr-001) | API and engine first; React deferred | Accepted | 1 |
| [ADR-002](#adr-002) | Ephemeral SQLite replaces SQL Server | Accepted | 1 |
| [ADR-003](#adr-003) | Governed entities are a verbatim transcription | Accepted | 1 |
| [ADR-004](#adr-004) | Money stored as integer minor units | Accepted | 1 |
| [ADR-005](#adr-005) | String length enforcement moves to the API layer | Accepted | 1 |
| [ADR-006](#adr-006) | One pull request per section, delivered by branch push | Accepted | 1 |
| [ADR-007](#adr-007) | Inherited seed tooling retained, `charges_sample.csv` outstanding | Open | 1 |

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
