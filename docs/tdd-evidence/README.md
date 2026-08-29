# TDD Evidence

Governance section 4 requires that unit and integration tests be *observed failing* before
implementation code exists, and flags any implementation that passes on first build without a
recorded failing run as a governance violation.

This directory holds the recorded console output of each RED run, one file per section:

- `section-N-red.txt` — the failing run, captured before the implementation commit.
- `section-N-green.txt` — the passing run for the same tests, captured after.

The git history carries the same evidence independently: every section contains a `test(section-N)`
commit whose tree fails, followed by a `feat(section-N)` commit whose tree passes. Either artefact
alone is sufficient for audit; both are kept because the console output records counts and timings
that a tree alone does not.

Tests that pass in a RED run are noted in the section's pull request. They are permitted only where
the test guards a *transcription* of governance rather than an implementation — for example, the
ASC X12 naming theories in `SchemaContractTheories`, which assert that governed property names
carry their loop and segment tokens. Those exist to fail on future drift, not to drive new code.
