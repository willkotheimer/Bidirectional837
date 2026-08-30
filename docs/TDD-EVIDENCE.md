# TDD Evidence

Governance Section 4 requires that unit and integration tests be *observed failing* before
implementation code exists, and flags any implementation that passes on its first build without a
recorded failing run as a governance violation.

This is the record of those runs. One row per section, appended as each section is delivered.

The raw console output is no longer versioned (ADR-020). It is written to `docs/tdd-evidence/`,
which is ignored, and the counts below are taken from it. Two artefacts still carry the evidence
independently of this file: the git history, in which every section is a `test(section-N)` commit
whose tree fails followed by a `feat(section-N)` commit whose tree passes, and the pull request for
each section, which quotes its own run.

The table is enforced, not advisory. `Governance.Traceability.Tests.TddEvidenceTheories` fails the
build if a recorded RED run has no failures, if a GREEN run has any, if a suite shrank between the
two, or if a section reaches `docs/DECISIONS.md` or `docs/FINDINGS.md` without recording a run here.

| Section | Deliverable | RED failed | RED passed | GREEN passed | GREEN failed | RED commit |
|---------|-------------|-----------:|-----------:|-------------:|-------------:|------------|
| 1 | Governed schema and ephemeral persistence | 164 | 26 | 190 | 0 | `842dc69` |
| 2 | API contracts, DTO validation, decision traceability | 89 | 345 | 444 | 0 | `c856a01` |
| 2a | Findings register and guard traceability | 6 | 462 | 468 | 0 | `65b4348` |
| 3 | Feature 1, the synthetic bill batch generator | 187 | 509 | 710 | 0 | `baa0bcb` |
| 4 | Feature 2, the 837 export and archival engine | 383 | 741 | 1130 | 0 | `f9523f5` |
| 4a | Executive summary of the TDD evidence | 6 | 1135 | 1163 | 0 | `4bf27e8` |
| 5 | Feature 3, ingestion, parsing and the reversibility proof | 191 | 1185 | 1393 | 0 | `e7588ac` |
| 6 | Provider data from a distilled NPPES snapshot | 383 | 1413 | 1815 | 0 | `00640a6` |
| 7 | Priced code catalogue and the routes that serve it | 16 | 1862 | 1889 | 0 | `fbb541b` |
| 7a | Governance names the guardrails; the application is Translator | 119 | 1902 | 2026 | 0 | `f7dfb7e` |
| 8 | Frontend governance, and the wire-naming defect it found | 37 | 2039 | 2085 | 0 | `f503b51` |

The GREEN commit is not recorded, because it cannot be: a row is written by the commit that turns
its own section green, so that commit cannot carry its own hash. FIND-013 records the discovery. It
is the RED commit that governance Section 4 asks for in any case — the commit whose tree was
observed failing — and the GREEN commit that answers it is the next `feat(section-N)` commit in the
history.

The suite grows between a RED run and the GREEN run that answers it. That is expected: a new
register row or a new source file adds cases to the traceability Theories, which are driven by
`MemberData` over the registers and the tree. It must never shrink, and that is asserted.

---

## What each run proved

### Section 1 — Governed schema and ephemeral persistence

The failing run existed before `EphemeralClaimStore` and `ClaimsDbContext` did. It found two
defects in the money path that inspection had not: SQLite coercing `9999999999999999.99` to
`10000000000000000`, and trailing zeros lost so that `1.00` returned as `1`. Both are Zero-Mutation
violations, and both are recorded as FIND-001 and FIND-002.

*Passing tests in this RED run:* 26. They are reflection-only naming guards over the governance
Section 2 transcription — the ASC X12 naming Theories in `SchemaContractTheories`, which assert that
governed property names carry their loop and segment tokens. They drive no implementation and exist
to fail on future drift. This is disclosed in ADR-003 and in the Section 1 pull request.

### Section 2 — API contracts, DTO validation and decision traceability

`docs/api/swagger.json` was authored before any controller, and the suite measures the application
against it rather than the reverse. The run found FIND-004, which every contract-level test had
missed: validation metadata written as `[property: ...]` on a record is ignored by ASP.NET Core, and
a request for 5000 bills returned 500 instead of the governed 400.

### Section 2a — Findings register and guard traceability

The smallest RED run of the build, and deliberately so: six failures, each one a finding recorded in
`docs/FINDINGS.md` with no test yet naming it. The section closes them.

### Section 3 — Feature 1, the synthetic bill batch generator

Three of the 187 failures were traceability rather than behaviour: the commit cited ADR-012,
ADR-013 and FIND-008 before the registers defined them. That is the marker contract working, and
the same pattern opens every section since.

### Section 4 — Feature 2, the 837 export and archival engine

The largest RED run so far. Two of the failures were again traceability, for ADR-018 and ADR-019.
The run also settled the shape of the writer: the Theories requiring that the same claim serialise
to the same bytes, and that storage identity never reach the stream, are what forbid reading the
clock or a counter for any element the standard requires and governance does not store.

### Section 8 — Frontend governance, and the wire-naming defect it found

The RED run began as a documentation section and became a defect section. Drafting the frontend
governance found FIND-020: the contract publishes CLM02_TotalClaimChargeAmount and the application
served clM02_TotalClaimChargeAmount, so the conformance suite's own Theories encoded the mangling.

Fixing it uncovered two more, both through the round-trip journey Theories added mid-section at the
project owner's request. FIND-021: the store returned a DateTime with its Kind lost, so the same
instant serialised two ways. FIND-022: batch generation returned the objects it had generated while
import returned what the store held, so a claim's representation depended on which route produced
it - the comparison the two-tab client is built to make.

### Section 7a — Governance names the guardrails; the application is Translator

A rename, so the RED run is unusual: the failing tests are the naming rule itself rather than a
missing capability. 119 failures, high because the rule is asserted per project and per file, so one
unrenamed project fails once for itself and once for every source file it holds.

One Theory exists only to catch a half-done rename - an application file still referencing a
Governance-rooted type would compile until the old assembly stopped being produced.

### Section 7 — Priced code catalogue and the routes that serve it

Sixteen failures: eleven routes answering 501 and five dangling markers. The distillation itself was
driven by inspection rather than by a failing test, and two defects in it were caught that way
before anything shipped - a code priced at $0.00 because a tiny positive RVU rounds to nothing, and
codes filed under the wrong category because a keyword matched an incidental mention late in a long
description rather than the subject at its head.

`CatalogIntegrityTheories` passes in this RED run and is disclosed as such. It guards distilled data
rather than driving an implementation, in the same way the Section 1 naming theories guard a
transcription.

### Section 6 — Provider data from a distilled NPPES snapshot

The run that found FIND-017: the live NPI registry query the application had been sending since
Section 3 was one the registry refuses, so the deployed system had never once retrieved a real
provider. Every existing Theory over that client answered a stub written to return what we expected
the registry to return, which proved the client could read a well-formed answer and nothing about
whether the registry would ever give one. The new Theories measure the request we send and the exact
rejection body the live service returns.

FIND-018 was found while wiring the replacement rather than by a failing test, and a test was
written for it before the fix.

### Section 5 — Feature 3, ingestion, parsing and the reversibility proof

The run that closed the round trip, and the largest of the build. It found three defects that
mattered and one that was only embarrassing. FIND-014: a governed decimal carries no canonical
scale until it has passed through the store or the reader, so the verifier reported a
representation difference as a mutation. FIND-015: the malformed-file suite was passing for a
reason unrelated to the damage it applied, and one of its own helpers was matching a segment
identifier inside the receiver name. FIND-016: problem documents had been served as
application/json since the contract was published in Section 2, and no test had ever asserted a
media type.

### Section 4a — Executive summary of the TDD evidence

Six failures, all in the new `TddEvidenceTheories`: the summary they read did not exist, and neither
did ADR-020. The run found FIND-013, the self-reference that removed the GREEN commit column from
this table.
