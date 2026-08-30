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

**Local storage is the only thing that ever saves a bill, and it clears on page load and refresh.**

So the effect is a clean slate every time the page opens, and the mechanism is one named place rather
than component state. Both tabs read and write the same store, which is why it exists: the
837 → Model table and the Model → 837 table are two views over one working set, not two islands.

Rules that follow from it:

- **Clearing happens before the first read**, at startup, not on unload. An unload handler does not
  run reliably — a crashed tab, a killed process or a hard refresh can all skip it, and the next load
  would then show bills from a previous session.
- **Every access is wrapped.** Local storage throws rather than returning null in a private window or
  where site data is blocked, and the page must still work with no store at all.
- **Nothing else persists.** No session storage, no IndexedDB, no cookies, no restore of form state.

This agrees with the server rather than merely being tidy: ADR-015 makes the store ephemeral and a
process restart a clean slate, so a client remembering more than the server does would show claims
that no longer exist.

*Recorded honestly:* bills do briefly exist on disk in the browser profile, between being written and
the next page load clearing them. That is acceptable here and would not be everywhere — subscriber
names are drawn from a synthetic list and no real patient data enters the system, so there is no PHI
to leak. If that ever stops being true, this rule stops being safe.

## Shape

**One page, two tabs.** Both directions of the translator, side by side, because the point of the
application is that the two are inverses.

| Tab | Direction | Ends in |
|-----|-----------|---------|
| **837 → Model** | Upload an 837 file or a ZIP of them | A table |
| **Model → 837** | Fill in a form, generate | A download button |

Nothing else is a top-level destination. The Imported Bills Dashboard governance Feature 3 names is
the 837 → Model table, not a third place.

## Tab: Model → 837

Formik (`useFormik`) for the generation form.

| Control | Behaviour |
|---------|-----------|
| Number of bills | Dropdown, up to 500. The governed ceiling is 500 and the API returns 400 above it, so the control must not offer a number the server will refuse. |
| Medical codes | Dropdown, **searchable, and scannable by top-level category**. Selecting a category selects everything under it: "500 cardiac bills" must be a two-interaction task, not fifteen. Fed by `GET /api/v1/codes`. |
| State | Dropdown, fed by `GET /api/v1/jurisdictions`. The selection governs the provider, which the server already does — jurisdiction drives the NPI snapshot lookup. |

Subscriber names are made up and charges are standard for the procedure. Both are already true
server-side; neither is a client concern.

**Flow.** Generation is local-first: the batch populates a table on screen. From there one button
exports CSV and one exports 837. The 837 export is `GET /api/v1/claims/export-zip`, a ZIP of one file
per claim (ADR-017). CSV is a view of the table, not a governed artefact, and no 837 fidelity claim
attaches to it.

## Tab: 837 → Model

- Accepts a single 837 file or a batch. `POST /api/v1/claims/import` already takes either and detects
  which from the payload's own ZIP signature rather than from a filename.
- Renders the reconstructed claims as a table, exportable to CSV.
- A rejected import applies nothing (ADR-022), so the error path shows the problem document's
  `detail` and leaves the table as it was. Those messages name the segment at fault (ADR-021), so
  they are worth surfacing verbatim rather than replacing with "import failed".

## Progress tracker

Both tabs show progress while the user waits, with information about what the backend is doing.

**It must not invent stages it cannot observe.** This is the one requirement here with a trap in it.
The API is request/response: there is no progress stream, and a 500-bill generation returns in about
a second. A tracker animating through "Validating… Generating… Persisting…" on a timer is a UI
telling the user something it does not know, which is the same class of dishonesty as a reversibility
tick that collapses two verdicts into one.

Two honest options, and the choice is the project owner's:

1. **Narrate what is genuinely observable client-side** — request sent, response received, parsing,
   rendering — and describe the governed backend steps as *what this operation does* rather than as
   *what is happening right now*. No fabricated timing. Costs nothing and needs no backend change.
2. **Report real server progress**, which needs a streaming or polling endpoint the contract does not
   have. That is a backend section, and for a one-second operation it is a large amount of machinery
   for a small amount of truth.

Option 1 is recommended unless the owner wants the operations to be genuinely long-running later.

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
