# React Client — Requirements

Stated by the project owner, 2026-08-30, and recorded here rather than left in a conversation.
ADR-001 deferred the React client until the API contracts were frozen. They are: every operation
`docs/api/swagger.json` publishes now has an engine behind it, and none answers 501.

These are requirements, not decisions. Decisions taken in building against them belong in
`docs/DECISIONS.md`, and anything discovered belongs in `docs/FINDINGS.md`.

## Stack

- **Vite.** React with the Vite dev server and build.
- **The APIs already built.** No new server behaviour is assumed by this document except where
  *Gaps in the published contract* below says otherwise, and each of those needs an ADR before it is
  added, because governance Section 5 names only two routes and ADR-009 governs the rest.
- **`src/data/`** holds the server boundary: one `useQuery` hook per GET, one `useMutation` per POST.
  Components consume the hooks and never call `fetch` directly, so request state has one home.

## Persistence

Nothing persists beyond the table on screen. No local storage, no session restore, no state kept
across a page refresh. The only client state worth keeping is what the current table is showing.

This sits with ADR-015 rather than against it: the store is already ephemeral and a restart is
already a clean slate, so a client that remembered more than the server does would be showing
claims that no longer exist.

## Bill creation

Formik (`useFormik`) for the generation form.

| Control | Behaviour |
|---------|-----------|
| Number of bills | Dropdown, up to 500. The governed ceiling is 500 and the API returns 400 above it, so the control must not offer a number the server will refuse. |
| Medical codes | Dropdown, **searchable, and scannable by top-level category**. Selecting a category selects everything under it: "500 cardiac bills" must be a two-interaction task, not fifteen. |
| State | Dropdown. The selection governs the provider, which the server already does — jurisdiction state drives the NPI registry lookup and the synthetic fallback (ADR-012). |

Subscriber names are made up, and charges are the standard charge for the procedure. Both are
already true server-side and neither is a client concern: the synthetic name list and the charge
schedule live in `Translator.Generation`, and the charge fallback is deterministic (ADR-013).

**Flow.** Creation is local first: the generated batch populates a React table on screen. From
there, one button exports to CSV and one to 837.

The 837 export is `GET /api/v1/claims/export-zip`, which returns a ZIP of one 837 file per claim
(ADR-017). CSV is a client-side concern — it is a view of the table, not a governed artefact, and
no 837 fidelity claim attaches to it.

## 837 to bill

- Accepts a single 837 file or a batch. `POST /api/v1/claims/import` already takes either and
  detects which by the payload's own ZIP signature rather than by filename.
- Renders the result as a table, exportable to CSV.
- A rejected import applies nothing (ADR-022), so the error path shows the problem document's
  `detail` and leaves the table as it was. Those messages name the segment at fault (ADR-021), so
  they are worth surfacing verbatim rather than replacing with "import failed".

## Example data, said out loud

The generated bills are **example data**, and the UI says so where a user can see it — not buried in
a footnote.

This is not modesty. The charges come from published CMS fee schedules, so they are real figures of
the right order of magnitude for the code, and that is exactly what makes the caveat necessary: they
look like prices. They are not what any particular provider bills, they do not vary by the state the
form selected, and they may not correspond to the randomly chosen provider on the claim at all. The
whole corpus exists to give the 837 translators something realistic to chew on.

The same goes for the providers: real NPIs from the public NPPES snapshot, attached to subscribers
and diagnoses that are invented, in combinations that never happened.

## Gaps in the published contract

Two things this document asks for have no endpoint behind them yet. Recorded here so the gap is
known before the client is written, not discovered halfway through it.

Both gaps recorded here are now closed, by `GET /api/v1/codes` and `GET /api/v1/jurisdictions`
(ADR-025). They are kept in the record because the reasoning still governs how the client uses them:

1. **The medical code catalog.** A searchable, category-scannable dropdown must be built against the
   served catalogue, never a hard-coded copy — a copy drifts from the server's seed corpus, and the
   drift surfaces as a batch-generate 400 for a category the dropdown offered.
2. **The jurisdiction list.** Same shape. The route serves only states the provider snapshot can
   actually source a provider for, so the selector cannot offer one the generator would refuse.

## Reversibility, and what the client should not imply

`POST /api/v1/claims/{id}/verify-reversibility` exists and returns a per-claim verdict with the
governed columns that moved. It is not in the requirements above, and is noted here only so it is a
deliberate omission rather than an oversight: governance Feature 3 calls for a Reversibility
Dashboard, and this document describes an import table instead.

If the table grows a reversibility column later, it must report the verdict the server gives —
including `EdiTextIsIdentical` and `RecordIsIdentical` separately. Collapsing them into one tick
would be the failure mode the verifier was built to avoid.
