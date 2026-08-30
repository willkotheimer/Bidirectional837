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
| [FIND-005](#find-005) | Inherited seed loader hides a missing data file | Medium | Fixed | 1 | required |
| [FIND-006](#find-006) | Naming guard rejected valid X12 segment identifiers | Low | Fixed | 1 | required |
| [FIND-007](#find-007) | Traceability scanner under-detected multi-citation markers | Low | Fixed | 2 | required |
| [FIND-008](#find-008) | Inherited provider seed carried invalid NPIs | Medium | Fixed | 3 | required |
| [FIND-009](#find-009) | Routing test conflated a missing route with a missing resource | Low | Fixed | 3 | required |
| [FIND-010](#find-010) | Governed timing budget met, but margin is thin and not in generation | Low | Mitigated | 3 | required |
| [FIND-011](#find-011) | Undotted diagnosis code would round-trip into a different code | High | Mitigated | 4 | required |
| [FIND-012](#find-012) | Delimiter guard rejected the segment that declares the delimiters | Low | Fixed | 4 | required |
| [FIND-013](#find-013) | Evidence summary cannot record the commit that writes it | Low | Fixed | 4a | required |
| [FIND-014](#find-014) | Governed decimal columns have no canonical scale in memory | Medium | Mitigated | 5 | required |
| [FIND-015](#find-015) | Malformed-file suite passed for a reason unrelated to the damage | Medium | Fixed | 5 | required |
| [FIND-016](#find-016) | Problem documents served under the wrong media type since Section 2 | Medium | Fixed | 5 | required |
| [FIND-017](#find-017) | Live NPI registry query was never answerable; fallback was permanent | High | Fixed | 6 | required |
| [FIND-018](#find-018) | Latching fallback would disable a local source after one miss | Medium | Fixed | 6 | required |
| [FIND-019](#find-019) | Seed reader documented an assumption the data outgrew | Medium | Fixed | 7 | required |
| [FIND-020](#find-020) | Governed field names mangled on the wire since Section 2 | High | Open | 8 | required |

A finding whose Guard reads `not applicable` is one no test yet holds shut. There are none at
present: every recorded finding is named by a test that fails if it returns. FIND-020 is recorded as
**Open** rather than Fixed - its guard exists and is failing, which is what a finding under repair
looks like in this register.

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

*Resolution:* closed in the Feature 1 section, where charge data was first genuinely needed.
`seed/charges_sample.csv` now exists, alongside `seed/hcpcs_categories.csv`, holding the curated
public HCPCS Level II corpus recorded in ADR-013.

*Guard:* `SeedIntegrityTheories.Seed_file_referenced_by_the_loader_exists` reads the file names out
of `scripts/load_to_sqlite.py` itself rather than hard-coding them, so adding a reference to the
loader without adding the file fails the build. A companion Theory asserts each file carries rows
beneath its header, because a file that exists and is empty would satisfy the loader just as
silently.

*Residual risk:* the loader still guards each load with an existence check, so it remains capable of
skipping a file in silence if run against a tree where one is missing. The guard above prevents that
tree from being committed, which is the cheaper place to enforce it; tightening the Python loader to
fail loudly would be a belt-and-braces improvement rather than a fix.

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

## FIND-008

**The inherited provider seed carried NPIs that fail the NPI check digit.**
Medium. Fixed. Discovered 2026-08-29 by a Theory written against the newly implemented check-digit
rule, and confirmed against the real data before being recorded.

`seed/providers_sample.json` carried the identifiers `1234567890` and `9876543210`. Both are ten
digits, and both are invalid: the final digit of an NPI is a Luhn check digit computed over the
identifier prefixed with 80840, the ANSI issuer prefix assigned to CMS. The correct values are
`1234567893` and `9876543213`. In both cases the inherited check digit was 0 where it should be 3.

*Why it matters:* the file is loaded by `scripts/load_to_sqlite.py` into the `providers` table and
was the obvious source of provider data for the generator. Governance User Story 1.1 requires a
*valid* NPI in Loop2010AA. Ten digits that look like an NPI but fail the check digit would produce
claims that pass every internal test and are rejected on arrival at a clearinghouse, with the
failure surfacing as far as possible from its cause.

*Resolution:* both identifiers corrected in place. The check-digit rule now lives in the domain, as
a property of the governed `Loop2010AA_NM109` field, and the synthetic provider directory mints
identifiers through it rather than formatting arbitrary digits.

*Guard:* `SeedIntegrityTheories.Every_npi_in_a_provider_seed_file_satisfies_the_check_digit`, and
`ClaimGeneratorInvariantTheories.Every_billing_provider_carries_a_valid_npi` over every generated
claim.

*Reportable as:* plausible-looking test data that is quietly wrong. Nothing in the inherited
repository was checking it, and a length check, which is the obvious validation to write, would
have passed it.

## FIND-009

**A contract test proved something weaker than it claimed, and broke when the code got better.**
Low. Fixed. Discovered 2026-08-29 when implementing a genuine 404.

`Published_operation_is_routed_by_the_application` asserted that each published operation did not
answer 404. While every operation returned 501, that was a fair proxy for "the route exists". Once
`GET /api/v1/claims/{id}` was implemented and correctly answered 404 for an absent claim, the test
failed, not because routing had regressed but because the assertion could not tell "this route does
not exist" apart from "this resource does not exist".

*Why it matters:* the tempting fix is to assert a narrower status, which preserves the ambiguity
rather than removing it. More importantly it is a test that passed for the wrong reason: it was
never measuring routing, only the absence of one particular status.

*Resolution:* routing is now read from the `EndpointDataSource` of the application, comparing the
published contract against the route patterns the application actually registers, with constraints
normalised so `{id:guid}` and `{id}` compare equal. The assertion now measures exactly what it
claims, and status-code behaviour is asserted separately by the tests that care about it.

*Guard:* `ApiContractConformanceTheories.Published_operation_is_routed_by_the_application`.

## FIND-010

**The governed 3.0 second budget is met, but almost none of it is spent generating.**
Low. Mitigated. Measured 2026-08-29 while implementing User Story 1.3; re-measured 2026-08-30 after
Section 6, which changed the arithmetic materially. See the addendum at the end of this entry.

Governance User Story 1.3 requires that "generation of 500 bills finishes in under 3.0 seconds".
Measured on the build machine, with the batch of 500 claims carrying 1,443 service lines:

| Stage | Time |
|-------|------|
| Generation alone, in process | **0.011 s** |
| Full request: generate, persist, serialise, over HTTP | **1.6 - 2.4 s** |
| First request of a process (JIT, DI, schema creation) | 7.4 s |

Generation is roughly 0.4% of the governed budget. The remainder is EF Core persisting 500 headers
and 1,443 lines, and serialising the same graph back to the client.

*Why it matters:* the literal governed requirement passes with about a 280-fold margin, and the
test asserts the stricter end-to-end reading instead, which passes with roughly a third of the
budget to spare. That is a real but thin margin, and it lives entirely in work governance did not
name. A slower CI machine could breach the stricter assertion while the governed requirement
remains comfortably met, and the failure would look like a generator regression when it would not
be one.

*Mitigation applied:* EF change detection is switched off for the insert. Change detection is
quadratic in tracked entities and there is nothing to detect for a graph that is entirely new; this
took a typical run from about 2.4 s to about 1.9 s.

*Residual risk, and what to do if it bites:* the first request of a process costs 7.4 s, which no
governed budget covers because governance measures generation rather than cold start. If the
end-to-end assertion becomes flaky on slower hardware, the honest fixes in order are: assert the
governed reading (generation alone) and measure the end-to-end figure separately as a tracked
number rather than a pass/fail gate; or reduce the response payload, since returning all 500 claims
in full is a client convenience rather than a governed requirement.

*Deliberately not done:* the budget was not met by weakening what is measured. The end-to-end
reading is the stricter one and it is what the suite asserts.

*Guard:* `BatchGenerationTheories.Governed_batch_size_completes_within_its_time_budget`.

## FIND-011

**A diagnosis code stored without its decimal point would round-trip into a different code.**
High. Mitigated. Discovered 2026-08-30 while specifying the HI segment, before the writer existed.

X12 forbids the decimal point in a diagnosis element, so `E11.9` is emitted as `E119`. The point
always follows the third character, so restoring it on the way back is deterministic - for a code
that carried one. A code stored as `E119` would be emitted unchanged and read back as `E11.9`.

*Why it matters:* the file produced is perfectly valid, the import succeeds, and the stored
diagnosis is a different diagnosis. It is the FIND-001 pattern applied to a clinical field rather
than a monetary one: a Zero-Mutation breach that nothing downstream can detect, because nothing
downstream knows what the code was supposed to be. Governance Section 2 declares the column only as
ten characters, so the schema does not exclude the undotted form.

*Resolution:* ADR-019. The writer refuses a code that is not in the canonical dotted form rather
than converting it, so the mutation cannot occur silently.

*Residual risk, and why this is Mitigated rather than Fixed:* nothing enforces canonical form at the
persistence boundary. Every write path that exists today produces canonical codes - the generator
draws from a dotted seed corpus, and the Feature 3 parser will restore the point on the way in - but
a future path that stored `E119` would make that claim unexportable, failing loudly at export
instead of at the point the bad value was written. Moving the constraint onto the governed column
would settle it, and that is a Section 2 change, which is the project owner's to make.

*Guard:* `CanonicalElementTheories.Diagnosis_code_that_is_not_in_the_governed_canonical_form_is_refused`,
alongside the round-trip Theory over the corpus diagnoses.

*Reportable as:* a mutation that a schema review could not have found. The governed column
declaration is correct, the value stored in it is a real ICD-10-CM code, and the round trip still
changes it, because the standard and the database disagree about one character.

## FIND-012

**The delimiter guard rejected the one segment whose job is to declare the delimiters.**
Low. Fixed. Discovered 2026-08-30 by the first green run of the serializer.

`No_emitted_element_carries_a_delimiter_character` asserted that no element anywhere in the
interchange carries an X12 delimiter, and failed on ISA - correctly, in the sense that ISA11 is the
repetition separator and ISA16 the component separator, each carried as its own literal value.

*Why it matters:* the guard is real and worth keeping. An unescaped separator inside a name or an
address splits one element into two and shifts every element after it, which is how an EDI file
becomes silently wrong rather than loudly invalid. A guard that fails on correct output gets
weakened or deleted, and the protection is lost with it. The same failure mode is recorded in
FIND-006, where a naming guard rejected valid X12 segment identifiers.

*Resolution:* the Theory is now
`No_emitted_element_outside_the_ISA_header_carries_a_delimiter_character`, and the exemption is
stated as a property of ISA rather than as a concession. The writer is unchanged: it was correct.

*Guard:* the renamed Theory itself, which still asserts the rule over every other segment and
refuses to pass vacuously.

*Reportable as:* a test defect, not a product defect - but one worth recording, because the tempting
fix was to make the writer wrong to keep the test green.

## FIND-013

**The evidence summary cannot record the hash of the commit that writes it.**
Low. Fixed. Discovered 2026-08-30 while implementing ADR-020, between its RED and GREEN runs.

The RED run for Section 4a asserted that each row of `docs/TDD-EVIDENCE.md` names a distinct RED
commit and GREEN commit. It cannot. A row is written by the commit that turns its own section
green, and no commit can contain its own hash.

*Why it matters:* it is small, but it is the kind of defect that gets fixed by writing something
untrue. The available workarounds were to leave the cell blank for the current section, to fill it
in during the *next* section — which leaves the most recent section, the one most likely to be
audited, as the one with no evidence — or to write a value that is not a commit.

*Resolution:* the column is removed. The RED commit is the one governance Section 4 asks for, being
the commit whose tree was observed failing, and it is knowable when the row is written. The GREEN
commit that answers it is the next `feat(section-N)` commit in the history, which is a convention
the log makes checkable without recording anything.

*Guard:* `TddEvidenceTheories.Row_names_the_commit_whose_tree_was_observed_failing`, which still
requires the RED commit and no longer requires what cannot exist.

*Reportable as:* a control that would have been satisfied by a plausible-looking placeholder. The
Theory would have passed against any seven hex characters, so the defect was in what the assertion
could be made true by, not in whether it passed.

## FIND-014

**A governed decimal column has no canonical scale until its value has passed through the store or
the reader.**
Medium. Mitigated. Discovered 2026-08-30 by the reversibility verifier reporting a mutation on
every corpus claim.

Governance Section 2 declares `decimal(18,2)` on the charge amounts and `decimal(18,4)` on the
service unit count. Neither the entity nor C# enforces that: `decimal.Round(4m, 4)` is `4`, at scale
zero, and an entity built in memory carries whatever scale the arithmetic that produced it left
behind. The store normalises to the governed scale (ADR-004) and the EDI reader normalises to it
(ADR-018), but an entity that has touched neither is at no particular scale.

The verifier compared amounts as text, correctly, because 1.00m and 1m are equal as decimals and are
not the same 837 — that is the whole of FIND-002. So it reported `4` becoming `4.0000` as a
mutation on every claim in the corpus.

*Why it matters:* the difference is real but it is a difference in representation, not in the
governed amount, and reporting it as mutation is how a reversibility dashboard becomes noise that
nobody reads. The opposite mistake is worse: comparing by value would have hidden a genuine scale
loss, which is the defect FIND-002 recorded.

*Resolution:* the verifier compares each amount as text at the scale its governed column declares,
normalising both sides first. The Section 2 column declaration is what defines the canonical form,
not the arithmetic that produced a particular value. Scale is still guarded at both boundaries it
actually crosses: `PersistenceRoundTripTheories` over the store, and
`Recovered_amounts_carry_the_scale_their_governed_columns_declare` over the reader.

*Residual risk, and why this is Mitigated rather than Fixed:* nothing enforces the governed scale on
an entity in memory. Every path that reaches the store or the wire normalises, so no stored or
emitted value is affected, but two in-memory representations of the same governed amount remain
possible. Enforcing scale on the entity would be a Section 2 change and is the project owner's to
make.

*Reportable as:* a defect found only because the comparison was strict. A verifier written to
compare decimals by value would have passed on day one and would have been unable to detect the
scale loss it existed to catch.

## FIND-015

**The malformed-file suite passed for a reason unrelated to the damage it applied.**
Medium. Fixed. Discovered 2026-08-30 while implementing the reader, in two stages.

Each Theory removed one required segment from a valid interchange and asserted the reader refused
the result. Every case passed. None of them passed because of the segment removed: removing any
segment also makes SE01 untrue, and the envelope check fires before any segment is looked for. The
suite proved that the reader counts segments, twelve times over.

Repairing SE01 after the removal exposed a second defect in the repair itself. The helper located
the SE trailer by searching for the substring `SE*`, which occurs inside the receiver name
`CLEARINGHOUSE*` earlier in the file. The "repair" was therefore rewriting the middle of the
functional group header, and every case then failed on a mangled GS06 — again unrelated to the
segment removed.

*Why it matters:* this is the FIND-009 pattern, and it is the most dangerous kind of green. Twelve
Theories reported that malformed files were refused; none of them had tested what they claimed. The
reader's per-segment checks were entirely unexercised, and a reader that silently accepted a claim
with no HI segment would have shipped with a full green suite behind it.

*Resolution:* SE01 is recomputed after damage so the intended check fires, and segment identifiers
are located at segment boundaries — the start of the interchange, or after a terminator — rather
than as substrings. A separate Theory now asserts that each refusal names the segment at fault,
which is what makes the per-segment checks observable at all.

*Guard:* `MalformedInterchangeTheories.Interchange_missing_a_required_segment_is_refused` and the
`StartOf` helper beneath it, both marked.

*Reportable as:* a reminder that a passing test proves only that the assertion held, not that the
scenario was the one described in the test's name.

## FIND-016

**Problem documents were served under the wrong media type, and had been since Section 2.**
Medium. Fixed. Discovered 2026-08-30 by the first API-level test that asserted a response media
type.

`docs/api/swagger.json` declares `application/problem+json` for every 400 and 404 the contract
publishes. The application served `application/json` for all of them. `[Produces("application/json")]`
on the controller class is not documentation: it is a result filter, and it forces its media type
onto every response the controller produces, including the problem documents that
`ControllerBase.Problem()` would otherwise have typed correctly.

*Why it matters:* the status codes were right and the bodies were right, so nothing looked wrong. A
client that dispatches on `Content-Type` — which is what the media type is for, and what RFC 7807
clients do — would not recognise these as problem documents. The API conformance suite had checked
routes, paths and status codes since Section 2 and had never asserted a media type, so the defect
survived three sections and two features.

*Resolution:* the class-level `[Produces]` is removed from both controllers. Response content types
are documented by `[ProducesResponseType]`, which describes without overriding. The action-level
`[Produces("application/zip")]` on the export route stays: that one is correct and load-bearing.

*Guard:* `ApiContractConformanceTheories.Error_response_is_served_as_the_problem_document_the_contract_declares`,
over a 404, a second 404 on a different route, and a validation 400.

*Reportable as:* a contract violation that a schema review would not have found, because the schema
was right — the published contract said `application/problem+json` all along. Only the application
disagreed, and nothing compared the two.

## FIND-017

**The live NPI registry query was never one the registry would answer, so the fallback was
permanent.**
High. Fixed. Discovered 2026-08-30 by querying the live service directly rather than a stub.

`NpiRegistryProviderDirectory` sent `?version=2.1&state={state}&enumeration_type=NPI-2&limit=20`.
The registry refuses that combination — `state` requires a companion search criterion — and refuses
it with **HTTP 200** and a body containing an `Errors` array rather than results:

> Field state requires additional search criteria

So `EnsureSuccessStatusCode` passed, no `results` property was found, the client threw "returned no
providers", and `ResilientProviderDirectory` set the registry aside for the lifetime of the
instance. Every subsequent claim in every subsequent batch used the synthetic set.

*Why it matters:* governance User Story 1.1's acceptance criterion is "System retrieves valid NPI,
Provider Name, and Physical Address" from the registry. A deployed instance has never done so. ADR-012
argued the governed intent held because `Generation:UseLiveNpiRegistry` defaults to true; that
reasoning was wrong, and the configuration flag was the only thing anyone checked.

The defect survived because every Theory over this client answers a stub, and the stub returns what
we expected the registry to return. That proves the client can read a well-formed answer. It says
nothing about whether the registry would ever give one to the question we ask, and the question was
the broken part. This is the FIND-016 pattern — a control proven only at the layer that declares it —
applied to an outbound request rather than a response.

*Resolution:* the query now carries the jurisdiction's ZIP prefix as its companion criterion, which
narrows *within* the jurisdiction rather than across it. An `Errors` body is reported as a refusal
naming the registry's own complaint, rather than as an empty result set, because both end in a
fallback but only one tells an operator the query is wrong. ADR-023 additionally moves the primary
source to a local snapshot, so the live path is no longer the only way to get a real provider.

*Guard:* `RegistryQueryTheories`, which asserts the shape of the request the client builds — that it
carries a companion criterion, that the criterion has a value, and that the state is still the
requested one — and replays the live rejection body verbatim. Asserting the request rather than the
response is the part that was missing.

*Reportable as:* a governed acceptance criterion that was never met in production, behind a fully
green test suite, for three sections. The test double was faithful to our expectations and not to
the service.

## FIND-018

**A latching fallback would have disabled the local provider source after a single miss.**
Medium. Fixed. Discovered 2026-08-30 while wiring the snapshot, before it shipped.

`ResilientProviderDirectory` sets its primary aside permanently after one failure. ADR-012 gives the
reason and it is a good one: a batch of 500 claims must not become 500 failed network calls each
waiting out a timeout.

Applied to the snapshot that reasoning inverts. The snapshot fails only for a jurisdiction it does
not carry — a territory outside the 52, or a typo — and can still serve every other jurisdiction at
no cost. Wrapping it in the latching policy would mean one request for an uncovered jurisdiction
silently dropped the entire application to synthetic providers, for every state, until restart.

*Why it matters:* the failure is invisible. Generation keeps succeeding, claims keep validating, the
NPIs are still check-digit valid, and nothing distinguishes the output except that the providers are
no longer real. It is the same class of silent degradation as FIND-017, which is what made it worth
looking for.

*Resolution:* `LayeredProviderDirectory` tries its sources in order on every request and sets none of
them aside. The registry keeps `ResilientProviderDirectory`, because that is the remote dependency
the latch was designed for. The distinction is the cost of a retry, and it is now stated in both
types.

*Guard:* `LayeredProviderDirectoryTheories`, which asserts that an unserviceable jurisdiction does
not cost the others, that the primary is consulted on every one of 500 requests, and — as a
contrast, so the two policies cannot quietly converge — that the resilient wrapper still latches.

*Reportable as:* a defect found by reading the fallback policy against a new kind of primary rather
than by a failing test. It would have shipped green.

## FIND-019

**A seed reader documented an assumption that the data later outgrew.**
Medium. Fixed. Discovered 2026-08-30 by reading the API response, not by a failing test.

`SeedResource.ReadRows` split every seed CSV on every comma, and said so in its own summary:
"Deliberately minimal: the seed files carry no quoted fields." That was true of the fifteen
hand-written codes ADR-013 curated. It stopped being true the moment ADR-024 distilled the catalogue
from CMS, whose descriptions are prose and are full of commas.

The consequence was visible in the served catalogue, once anyone looked:

> `"Moderate sedation services provided by the same physician or other qualified health care
> professional performing a gastrointestinal endoscopic service that sedation supports`

— a stray leading quote and everything after the first comma gone.

*Why it matters, and why it is Medium rather than Low.* A description reaches no 837 element, so no
claim was corrupted. But two things about how it survived are worth more than the defect:

The charge path escaped **by accident**. `SeedChargeSchedule` reads its price from `cells[^1]`, the
last cell, and price is the last column — so naive splitting still landed on it. Had the columns been
ordered any other way, every catalogued code would have silently fallen through to the deterministic
fallback charge, which is positive and plausible and would have passed every assertion in the suite.

And the guard that should have caught it asserted the wrong property. `Every_catalogued_code_carries_a_description`
asserted the description was not empty. A truncated description is not empty.

*Resolution:* the reader honours quoted fields, and `SeedProviderDirectory` — which had grown its own
private copy of the same splitter, because provider names contain commas too — now uses the one
implementation rather than a second one that happened to be correct.

*Guard:* `CatalogIntegrityTheories.Descriptions_survive_the_seed_reader_whole`, which asserts
completeness rather than presence, and asserts that the corpus actually contains a quoted field so
the guard cannot pass vacuously.

*Reportable as:* a stale assumption that was written down. The comment was accurate when authored and
became false without anyone editing it, which is the failure mode documentation cannot protect
against and a test can.

## FIND-010 — addendum, 2026-08-30

Re-measured after Section 6 replaced per-claim registry lookups with a local snapshot (ADR-023), and
after Section 7 grew the catalogue from 15 codes to 980.

| Stage | 2026-08-29 | 2026-08-30 |
|-------|-----------|-----------|
| Full request: 500 bills, over HTTP | 1.6 - 2.4 s | **0.99 - 1.08 s** |
| First request of a process | 7.4 s | 3.9 s |

The margin against the governed 3.0 second budget is now roughly threefold rather than the third it
was. The finding stays **Mitigated** rather than closed, because its substance was never the number:
the budget is still spent almost entirely on persistence and serialisation, which governance does not
name, so a slower machine could still breach the end-to-end assertion while the governed requirement
stays comfortably met. What has changed is that the headroom is no longer thin.

## FIND-020

**Every governed field name is mangled on the wire, and has been since the contract was published.**
High. Open — the guard exists and fails; the fix is Section 8's GREEN. Discovered 2026-08-30 while
drafting `docs/GOVERNANCE-FRONTEND.md`.

`docs/api/swagger.json` declares the governed ASC X12 names. The application serves something else:

| Contract publishes | Application serves |
|--------------------|--------------------|
| `CLM02_TotalClaimChargeAmount` | `clM02_TotalClaimChargeAmount` |
| `BHT03_ClaimSubmitterTransactionId` | `bhT03_ClaimSubmitterTransactionId` |
| `Loop2010AA_NM103_BillingProviderLastNameOrOrg` | `loop2010AA_NM103_BillingProviderLastNameOrOrg` |

ASP.NET Core's default camelCase policy lowercases the leading character of a name, and on a name
beginning with an acronym that produces `clM02_`, `bhT03_`, `hI01_2_`.

*Why it matters:* governance Section 1 is explicit — "Attribute names across the database, DTOs, and
React forms must reflect ASC X12 nomenclature ... If a field is named otherwise, it requires a
documented mapping attribute linking it directly to its 837 segment counterpart." `clM02_` is not
ASC X12 nomenclature and has no documented mapping. The database column is right, the entity is
right, the DTO is right, the published contract is right, and the payload — the only one of them a
client ever sees — is wrong.

It is High rather than Medium because of what happens next. The React client is about to be written,
governance binds *React forms* to the same names, and every component written against `clM02_` would
carry the mangling into the frontend, where it would be far more work to remove than a serializer
setting.

*How it survived:* the conformance suite has checked routes and status codes since Section 2 and
media types since FIND-016, and has never checked a field name. Worse, the existing API Theories
*read* the mangled names — `claim.GetProperty("clM02_TotalClaimChargeAmount")` — so the suite encodes
the defect and would fail if it were fixed. They are a record of the bug, not a guard against it.
This is the third time in this build that a control was proven only at the layer that declares it
(FIND-004, FIND-016), and the second time a test agreed with the code about a shared mistake
(FIND-017).

*Resolution:* serialise with the declared names rather than a naming policy, and correct the Theories
that encode the mangled form. After that the governed name is identical in the column, the entity,
the DTO, the contract, the payload and the React state, which is what Section 1 asks for.

*Guard:* `ContractNamingTheories` — every property the contract publishes must appear in the served
claim, no served property may be absent from the contract, and the governed names are additionally
asserted literally, so a contract quietly edited to match a mangled payload would still fail.

*Reportable as:* a naming rule stated in the first paragraph of the governance document, satisfied at
four layers out of five, and broken at the only one that is externally visible.
