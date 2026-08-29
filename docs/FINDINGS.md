# Findings Register

Every defect, corruption, or governance gap discovered during the build, recorded so it can be
reported afterwards without depending on anyone's recollection or on a conversation transcript.

A finding is a fact about the system that was **discovered**. A decision about what to do next is an
ADR, and lives in `docs/DECISIONS.md`. The two are linked: most findings here name the ADR that
resolved them, and most ADRs that changed code name the finding that forced it.

The **Guard** column is enforced, not advisory. Where it names a test, that test is the standing
proof the finding stays fixed, and the test carries a `PROVENANCE: FIND-NNN` comment.
`Governance.Traceability.Tests` fails the build if a guarded finding has no marked test, or if a
marker cites a finding this register does not define. The convention is ADR-011.

| ID | Finding | Severity | Status | Section | Guard |
|----|---------|----------|--------|---------|-------|
| [FIND-001](#find-001) | Money silently corrupted by the store | Critical | Fixed | 1 | required |
| [FIND-002](#find-002) | Decimal scale dropped by the store | High | Fixed | 1 | required |
| [FIND-003](#find-003) | Governed length limits unenforced by the store | High | Mitigated | 1 | required |
| [FIND-004](#find-004) | Record validation silently disabled, 500 instead of 400 | High | Fixed | 2 | required |
| [FIND-005](#find-005) | Inherited seed loader hides a missing data file | Medium | Open | 1 | not applicable |
| [FIND-006](#find-006) | Naming guard rejected valid X12 segment identifiers | Low | Fixed | 1 | required |
| [FIND-007](#find-007) | Traceability scanner under-detected multi-citation markers | Low | Fixed | 2 | required |

A finding whose Guard reads `not applicable` is one no test yet holds shut. FIND-005 is the only
such entry, and it is open: the guard arrives with the fix, in the Feature 1 section.

Severity is judged by consequence to the governance Section 1 Reversibility Guarantee and to data
correctness, not by effort to fix.

---

## FIND-001

**Monetary values were silently corrupted in transit through the store.**
Critical. Fixed. Discovered 2026-08-29 by a test written before the persistence layer existed.

The governed schema declares `[Column(TypeName = "decimal(18,2)")]` on the claim charge amounts.
SQLite assigns NUMERIC affinity to a column so declared and coerces the value to a float. Written
`9999999999999999.99`, the store returned `10000000000000000`.

*Why it matters:* this is a direct breach of the Zero-Mutation Rule, and the worst kind. The claim
round-trips successfully, the export produces a well-formed 837, and the charge on it is wrong. No
downstream validation would catch it, because nothing downstream knows what the amount was
supposed to be.

*Resolution:* ADR-004. Monetary and quantity values are stored as exact integer minor units at their
governed scale. The Section 2 column declarations are unchanged.

*Guard:* `PersistenceRoundTripTheories.Claim_charge_amount_survives_the_store_without_drift`, a
Theory over monetary edge values including the maximum the governed precision permits.

*Reportable as:* a defect that a schema-only review would have missed entirely. The governed
annotation was correct, the entity was correct, and the data was still wrong, because the engine
beneath them interpreted a type name that engine does not implement.

## FIND-002

**Trailing zeros were dropped, changing the text an 837 amount would render to.**
High. Fixed. Discovered 2026-08-29 alongside FIND-001, by the same pre-implementation suite.

Written `1.00`, the store returned `1`. Written `0.10`, it returned `0.1`. The numeric values are
equal, so a test comparing decimals would have passed.

*Why it matters:* 837 amount elements are rendered from these values. A value that returns with a
different scale produces different bytes on export, which defeats byte-level reversibility even
though nothing appears to have been lost. This is the failure mode that would have surfaced much
later, in Feature 3, as an unexplained round-trip mismatch.

*Resolution:* ADR-004. The read conversion multiplies by a literal of the target scale rather than
dividing, because decimal multiplication adds operand scales: `100 * 0.01m` is `1.00m` where
`100m / 100m` is `1m`.

*Guard:* `PersistenceRoundTripTheories.Claim_charge_amount_retains_its_scale_through_the_store`,
which compares the rendered string rather than the numeric value.

*Reportable as:* an argument for asserting on the representation a downstream format will consume,
not only on value equality.

## FIND-003

**The store does not enforce the governed maximum lengths.**
High. Mitigated. Discovered 2026-08-29 by a deliberate probe, verified rather than assumed.

Governance Section 2 declares a `[StringLength(n)]` on every governed string field. SQL Server would
reject an over-length insert. SQLite does not. A probe wrote 40 characters into
`Loop2010AA_N402_BillingProviderState`, whose governed maximum is 2, and read all 40 back unchanged.

*Why it matters:* it is the one place where hosting the governed schema on SQLite genuinely weakens
a governed guarantee rather than merely relocating it. An over-length value reaching the store would
produce an 837 segment that violates the X12 element length rules and would be rejected by a
clearinghouse.

*Resolution:* mitigated, not eliminated. ADR-005 moved enforcement to the contract boundary and
ADR-010 implemented it. All 21 governed limits are declared on the DTOs, proven to mirror the entity
model field by field, and proven to reject over HTTP.

*Residual risk:* enforcement is at the API boundary only. Any future code path that writes to the
store without passing through a validated DTO can still persist an over-length value. A writer that
bypasses the contract needs its own validation, or the limits need enforcing in `SaveChanges`.

*Guard:* `ContractValidationTheories.Contract_mirrors_the_governed_maximum_length` and
`Contract_rejects_a_value_one_character_over_the_governed_limit`, both Theories over the governed
field table, plus `ApiContractConformanceTheories.Batch_generation_rejects_an_over_length_jurisdiction_state`
end to end.

## FIND-004

**Validation metadata in the wrong place disabled validation entirely and returned 500 instead of the governed 400.**
High. Fixed. Discovered 2026-08-29 by an end-to-end test, after every contract-level test had passed.

The DTO validation annotations were first written as `[property: Required, StringLength(n)]`.
`Validator.TryValidateObject` honours that form, so all 183 contract-level Theories passed. ASP.NET
Core does not honour it: it reads record validation metadata from the primary constructor parameter,
and throws when it finds it on the generated property instead.

> Record type 'BatchGenerationRequestDto' has validation metadata defined on property
> 'MedicalCodeCategories' that will be ignored. 'MedicalCodeCategories' is a parameter in the record
> primary constructor and validation metadata must be associated with the constructor parameter.

*Why it matters:* the consequence was worse than a validation that failed to fire. The request died
with a 500 before the governed ceiling was evaluated at all, so a request for 5000 bills was
answered with an internal server error rather than the 400 Bad Request User Story 1.3 requires. The
governed acceptance criterion was unmet while every test asserting it passed.

*Resolution:* annotations moved to the constructor parameters. `ContractValidationTheories` now
reads metadata from the parameters, which is where the framework reads it, so the suite can no
longer pass against metadata the framework ignores.

*Guard:* `ContractValidationTheories.Contract_declares_no_validation_metadata_on_a_generated_property`
fails the build if any governed contract regresses to the property form, and
`ApiContractConformanceTheories.Batch_generation_rejects_a_request_above_the_governed_ceiling`
exercises the ceiling through the real pipeline.

*Reportable as:* the clearest case in the build for testing a control at the layer that enforces it
rather than the layer that declares it. A green suite proved nothing here, because every test in it
shared the same wrong assumption about where the framework looks.

## FIND-005

**The inherited seed loader silently tolerates a data file that does not exist.**
Medium. Open. Discovered 2026-08-29 by inspection of the inherited code.

`seed/charges_sample.csv` is referenced by `seed/README.md`, by `docs/PROVENANCE.md`, and by
`scripts/load_to_sqlite.py`. The file is not in the repository. The loader guards every load with
`if charges_path.exists()`, so the absence produces no error: the `charges` table is created, left
empty, and the run reports success.

*Why it matters:* governance User Story 1.2 requires procedure codes to carry published standard
charges or a deterministic fallback, and requires that CLM02 equal the sum of the SV102 line
amounts. A silently empty charge table would produce claims with zero charges that still satisfy
that sum, so the generator would appear correct while emitting worthless bills.

*Status:* deliberately deferred, recorded in ADR-007. It is resolved in the Feature 1 section, where
charge data is first genuinely needed. The existence check should also be tightened: a seed file
named in the loader and missing from disk should fail loudly rather than be skipped.

## FIND-006

**The ASC X12 naming guard rejected valid single-letter segment identifiers.**
Low. Fixed. Discovered 2026-08-29 during the Section 1 RED run.

The regex enforcing governance Section 1 naming alignment required a 2-to-3 character segment
identifier, so it rejected `Loop2010AA_N301_...` and `Loop2010AA_N401_...`. `N3` (address) and `N4`
(geographic location) are legitimate X12 segment identifiers.

*Why it matters:* only mildly, and it was caught immediately. It is recorded because the failure was
in the governance guard itself rather than in the governed code, and a guard that rejects valid
input trains people to weaken it. The correct fix was to widen the identifier to 1-3 characters, not
to exempt the fields.

*Guard:* `SchemaContractTheories.Governed_property_name_carries_an_x12_provenance_token`.

## FIND-007

**The traceability scanner under-detected markers that cite more than one decision.**
Low. Fixed. Discovered 2026-08-29 during implementation, before the convention was relied upon.

The first marker regex captured only the token immediately following `PROVENANCE:`, so a marker
reading `PROVENANCE: GOVERNANCE-2, ADR-003` registered the governance citation and missed ADR-003
entirely.

*Why it matters:* a traceability tool that under-detects is worse than none, because it reports
coverage it has not verified. Had it gone unnoticed, ADR-003 would have been reported as unmarked
while its marker sat in the file, and the natural response would have been to add a duplicate
marker rather than to fix the scanner.

*Resolution:* citations are now read from the whole marker line, so one marker may cite several
decisions alongside a governed section.

*Guard:* `DecisionProvenanceTheories.Decision_marked_as_code_bearing_appears_in_the_source`, which
detects ADR-003 through exactly the multi-citation marker that previously defeated it.
