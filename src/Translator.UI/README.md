# Translator.UI

The React client for the 837 translator. Governed by [`docs/GOVERNANCE-FRONTEND.md`](../../docs/GOVERNANCE-FRONTEND.md),
which is binding.

## Running it

The API must be running, **in Development**, before the client can reach it:

```
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5000 \
  dotnet run --project src/Translator.Api --no-launch-profile
```

The environment matters. ADR-028 grants CORS to `http://localhost:5173` and withholds it entirely in
Production, because a deployed instance serves the client from its own origin and needs no grant. An
API started without an environment defaults to Production, and every call from the client is then
refused by the browser — correctly, but confusingly if you were not expecting it.

Then:

```
npm install
npm run dev      # http://localhost:5173, pinned - it is the origin the API grants
npm test         # vitest
npm run build    # tsc -b && vite build
```

`VITE_API_BASE_URL` overrides the API address, which defaults to `http://localhost:5000`.

## Layout

| Path | What lives there |
|------|------------------|
| `src/data/` | The only code that talks to the server. One hook per published operation. |
| `src/helpers/` | Pure functions: CSV, governed-column formatting, the problem-document reader, the store. |
| `src/components/` | Presentation. |
| `src/tests/Helpers/` | Every helper's tests, driven by `it.each` over tables of variants. |

Helpers carry the testable logic deliberately: a function taking values and returning values can be
tested directly, while the same logic inside a component can only be tested through the DOM.
