# Frontend Governance

**Status: approved by the project owner, 2026-08-30. Binding.** Drafted by Claude Opus 5.
`governance.txt` was authored by Gemini AI and is binding as written; this document does not amend it
and cannot. Departures from either are ADRs in `docs/DECISIONS.md`.

---

## 1. Authority and precedence

This document governs the React client. It exists because `governance.txt` already binds the
frontend in two places and says nothing more:

> **Domain Authority:** ... APIs, DTOs, **and React state models are downstream views of these
> entities.**

> **Naming Alignment:** Attribute names across the database, DTOs, **and React forms** must reflect
> ASC X12 nomenclature (e.g. `Loop2000A_BillingProvider`, `CLM02_TotalClaimChargeAmount`). If a field
> is named otherwise (e.g. `total_amount`), it requires a documented mapping attribute linking it
> directly to its 837 segment counterpart.

Everything below either applies those two clauses or fills a gap they leave open.

**Precedence.** `governance.txt` wins on any conflict. Where this document is silent, the backend
rule applies by analogy. Where following it would require departing from `governance.txt`, that is an
ADR in `docs/DECISIONS.md`, exactly as on the backend — not a decision taken in a component.

**Requirements live elsewhere.** `docs/UI-REQUIREMENTS.md` says *what* to build. This says *how it
must be built*. The split mirrors `governance.txt`: Sections 1–4 are rules, Section 5 is a roadmap.

---

## 2. The contract is the boundary

`docs/api/swagger.json` is the source of truth for the client, as it is for the server. The server is
measured against it and so is the client.

- **No client type may invent a field.** Every model the client holds corresponds to a published
  schema. A field the contract does not declare does not exist.
- **No client may hard-code a vocabulary the server publishes.** Medical codes and jurisdictions come
  from `GET /api/v1/codes` and `GET /api/v1/jurisdictions`. This is ADR-025's whole reason: a
  hard-coded copy drifts from the server's seed corpus, and the drift surfaces as a 400 for a
  category the dropdown itself offered — a failure reported far from its cause.
- **Contract changes are backend changes.** The client never asks the server to bend; if the client
  needs something, the contract is amended first and the server follows, per governance Section 4.

---

## 3. Naming alignment

This is the sharpest clause governing the frontend, and it needs three rulings.

### 3.1 Governed fields keep governed names

Any state, prop or form field that is a downstream view of a governed Section 2 column carries that
column's name, unmangled:

```ts
claim.CLM02_TotalClaimChargeAmount     // yes
claim.totalCharge                      // no, unless mapped per §3.3
```

### 3.2 The wire must serve the governed name

**This is not currently true, and must be fixed before the client is written.**

`swagger.json` declares `CLM02_TotalClaimChargeAmount`. The application serves
`clM02_TotalClaimChargeAmount` — ASP.NET Core's default camelCase policy lowercases the leading
character of an acronym and produces `clM02_`, `bhT03_`, `loop2010AA_`. Those are not ASC X12
nomenclature; they are a mangling of it, and the published contract and the served payload disagree
on every governed field.

Recorded as **FIND-020**. Until it is fixed, a client has only bad options: carry the mangled name
and violate §1, or map every governed field and require a documented mapping attribute for each. The
fix is at the server — serialise with the declared names — after which the governed name is identical
in the entity, the DTO, the contract, the payload and the React state. That is what governance
Section 1 is asking for.

**No client code is written against the mangled names.** Baking them in would spread a defect from
one serializer setting into every component.

### 3.3 Fields with no 837 counterpart

Not every control is a view of a governed entity. The generation form's bill count, jurisdiction
selector and category selector are parameters of a request, not claim data: `BillCount` has no 837
segment and never will.

Such a field is exempt from §3.1, and the exemption is the "documented mapping attribute" governance
requires — the documentation being that there is nothing to map. Each is listed here, and the list is
exhaustive:

| Field | Why it has no 837 counterpart |
|-------|-------------------------------|
| `BillCount` | How many claims to generate. A request parameter. |
| `JurisdictionState` | Selects the provider, which then populates governed Loop2010AA fields. |
| `MedicalCodeCategories` | Selects which codes are drawn from. The code itself becomes SV101-2. |

A field not in this table and not carrying a governed name is a governance violation.

---

## 4. The data layer

The project is **`src/Translator.UI`**, alongside the .NET projects and outside the solution.
`src/data/` within it is the only place that talks to the server.

- One hook per published operation. `useQuery` for a GET, `useMutation` for a POST.
- **`fetch`, not axios.** The platform does this now; a dependency for it is surface without benefit.
- **Components never call `fetch` themselves.** A component that fetches has put request state —
  loading, error, retry — somewhere it cannot be tested or reused.
- **Invalidate on save.** A mutation that changes what a query would return invalidates that query.
  Generating or importing changes the claim set, so both invalidate it. Hand-patching a cache to
  match what the server *probably* did is how a client starts disagreeing with the server quietly.
- Errors surface the server's problem document `detail` verbatim. The reader refuses malformed 837
  files by naming the segment at fault (ADR-021); replacing that with "import failed" throws away the
  only part a user can act on.
- The client does not retry a rejected import. ADR-022 makes an import all-or-nothing; a silent retry
  risks duplicating everything that succeeded.

---

## 4a. React conventions

- **Functional components throughout.** No classes.
- **Derive during render; do not synchronise.** `useMemo` for anything computed from props, state or
  query data. `useEffect` is for a genuine side effect — reaching outside React — and nothing else.
  An effect that sets state from other state is a second source of truth that arrives one render
  late, and it is the most common way a React table starts disagreeing with the data behind it.
- **`useReducer` for complex state** that `useQuery` does not already hold. Server data lives in the
  query cache; genuinely local multi-field state that transitions together lives in a reducer, where
  the transitions can be read in one place and tested without rendering anything.
- **Helper functions are preferred** for anything reusable or worth testing. A function that takes
  values and returns values can be tested directly; the same logic inside a component can only be
  tested through the DOM. Formatting, CSV construction, grouping, filtering and validation are all
  helpers.

---

## 5. State and persistence

**Local storage is the only thing that ever saves a bill, and it is cleared on page load.** Nothing
else persists: no session storage, no IndexedDB, no cookies, no restored form state.

The effect is a clean slate on every page load; the mechanism is one named store rather than
component state, so both tabs are views over a single working set.

Three rules make that true rather than approximately true:

1. **Clear at startup, before the first read** — not on unload. An unload handler does not run
   reliably, and a session that skipped it would greet the next visitor with someone else's bills.
2. **Wrap every access.** Local storage throws rather than returning null in a private window or
   where site data is blocked. The page works with no store at all.
3. **Store bills, not settings.** Anything that would survive by being written somewhere else is
   outside this rule and therefore not allowed.

This agrees with the server rather than merely being simple: ADR-015 makes the store ephemeral and a
process restart a clean slate, so a client that remembered more than the server does would display
claims that no longer exist.

*Recorded honestly:* bills do exist on disk in the browser profile between being written and the next
load clearing them. That is acceptable because subscriber names are synthetic and no real patient
data enters the system. It would not be acceptable if that ever changed, and this clause is where to
look when it does.

---

## 6. Example data, disclosed

The UI states that its output is example data, where a user can see it — not in a footnote, not in a
tooltip only.

The reason is that the data is good enough to mislead. Charges are real published CMS figures of the
right order of magnitude (ADR-024) and providers are real NPIs from the public NPPES snapshot
(ADR-023), attached to invented subscribers and diagnoses in combinations that never happened. It
looks like production data because most of it is real. That is exactly why it must be labelled.

---

## 7. Test-driven protocol

Governance Section 4 applies to the client unchanged: tests are written first and **observed
failing** before the implementation exists, and an implementation that passes on its first build
without a recorded failing run is a governance violation.

**Vitest**, with Testing Library for anything that renders.

- **Every helper has tests, and they live in a `Helpers` subfolder of the test tree.** Helpers are
  where the testable logic is deliberately put (§4a), so this is where the bulk of the suite lives.
- **`it.each`, driven by a table of variants.** This is the same discipline the backend suite runs
  on: an invariant asserted over many inputs rather than one example, so a case can be added without
  writing a new test and a failure names the input that broke. `it.each` is to Vitest what
  `[Theory]` with `MemberData` is to xUnit, and the whole of `docs/TDD-EVIDENCE.md` is built on that
  distinction.
- **A test asserts what a user can do**, not which elements exist. Query by role and label.
- **No snapshot test stands alone.** A snapshot records that output changed, not that it is right,
  and it is updated by reflex. Snapshots may support an assertion; they may not be one.
- **The server is faked at the network boundary**, not by stubbing the hooks. A test that stubs
  `useClaims` proves the component renders what it was handed and nothing about whether it asked the
  right question — the FIND-017 failure, in a different layer.
- **RED and GREEN counts are recorded** in `docs/TDD-EVIDENCE.md` alongside the backend's, under the
  same enforced table.

---

## 8. Provenance markers

ADR-008's convention extends to the client: code that embodies a registered decision carries
`PROVENANCE: ADR-NNN`, and a test holding a finding shut carries `PROVENANCE: FIND-NNN`.

This requires `Governance.Traceability.Tests` to scan the client directory and `.ts`/`.tsx` files; it
currently scans only `src`, `tests`, `scripts`, `infra` for `.cs`, `.py`, `.bicep`. Extending the
scanner is preferred over exempting the client, because the alternative is a governed project with an
ungoverned half.

---

## 9. What the client must never do

- Invent a medical code, category, jurisdiction or provider.
- Compute or adjust a charge. Charges come from the server; the client displays them.
- Truncate a governed value to fit a layout. Wrap it, or widen the column.
- Present a reversibility verdict as a single tick. `EdiTextIsIdentical` and `RecordIsIdentical` are
  reported separately by the server precisely so that collapsing them is a visible choice rather than
  an accident — that collapse is the failure mode the verifier exists to prevent.
- Treat CSV export as a governed artefact. It is a view of the table. The 837 is the governed output
  and only the server produces it.

---

## 10. Open for the owner

1. **FIND-020** — the serializer fix in §3.2. It changes the payload every existing API test reads,
   so it lands before any client code.
2. **The reversibility verdict has no home in the UI.** Both of governance Feature 3's acceptance
   criteria are already met — 3.2 asks for an automated test, not a screen, and that test is green.
   "Dashboard" appears only in the feature's title and in 3.1's "so that they display on the Imported
   Bills Dashboard", which the 837 → Model tab satisfies. So this is an open choice rather than an
   unmet requirement: whether the imported table surfaces the per-claim verdict that
   `POST /api/v1/claims/{id}/verify-reversibility` already returns. If it does, §9 governs how —
   the two booleans are shown separately or not at all.

*Settled since drafting:* the project is `src/Translator.UI`; there is no progress tracker for now;
local storage is the only store and clears at page load.
