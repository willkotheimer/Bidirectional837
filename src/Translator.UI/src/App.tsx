import { useState } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { GenerateTab } from './components/GenerateTab';
import { clearAllClaims } from './helpers/claimStore';
import './App.css';

/**
 * PROVENANCE: ADR-027 - frontend governance Section 5: the store is cleared at startup, before
 * anything reads it, rather than on unload. An unload handler does not run reliably - a crashed tab,
 * a killed process or a hard refresh all skip it - and the next load would then show a previous
 * session's bills.
 *
 * Run from a useState initialiser rather than module scope or an effect, and each choice is
 * deliberate. Module scope runs once per *process*, which is once per page load in a browser but
 * not under test or hot reload, so a stale store would survive exactly where it is hardest to
 * notice. An effect runs after the first render, which is after the first read - and Section 5 says
 * before it. An initialiser is the one place that is both before the first read and on every mount.
 * Clearing twice costs nothing, which is what makes it safe under StrictMode's double invocation.
 */

const client = new QueryClient({
  defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
});

type TabId = 'import' | 'generate';

const TABS: { id: TabId; label: string }[] = [
  { id: 'import', label: '837 → Model' },
  { id: 'generate', label: 'Model → 837' },
];

export default function App() {
  useState(() => {
    clearAllClaims();
    return null;
  });

  const [active, setActive] = useState<TabId>('import');

  return (
    <QueryClientProvider client={client}>
      <main>
        <h1>837 Translator</h1>

        {/*
          PROVENANCE: ADR-024 - the charges are real published CMS figures of the right order of
          magnitude and the providers are real NPIs from the public NPPES snapshot, attached to
          invented subscribers in combinations that never happened. It looks like production data
          because most of it is real, which is exactly why it is labelled here rather than in a
          footnote (frontend governance Section 6).
        */}
        <p role="note">
          <strong>Example data.</strong> Charges come from published CMS fee schedules and providers
          from the public NPI registry, but subscribers and diagnoses are invented and no bill here
          describes anything that happened. Nothing on this page is a price or a medical record.
        </p>

        <div role="tablist" aria-label="Translation direction">
          {TABS.map((tab) => (
            <button
              key={tab.id}
              role="tab"
              type="button"
              id={`tab-${tab.id}`}
              aria-selected={active === tab.id}
              aria-controls={`panel-${tab.id}`}
              onClick={() => setActive(tab.id)}
            >
              {tab.label}
            </button>
          ))}
        </div>

        <div role="tabpanel" id="panel-import" aria-labelledby="tab-import" hidden={active !== 'import'}>
          <h2>837 → Model</h2>
          <p>
            Upload an 837 file, or a ZIP of them, and see the bills it reconstructs. Not built yet —
            the ingestion engine and its reversibility check are already running on the server.
          </p>
        </div>

        <div role="tabpanel" id="panel-generate" aria-labelledby="tab-generate" hidden={active !== 'generate'}>
          <h2>Model → 837</h2>
          {active === 'generate' && <GenerateTab />}
        </div>
      </main>
    </QueryClientProvider>
  );
}
