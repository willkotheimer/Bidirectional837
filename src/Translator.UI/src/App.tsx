import { useState } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { GenerateTab } from './components/GenerateTab';
import { ImportTab } from './components/ImportTab';
import { cn } from './components/ui';
import { Logo } from './components/Logo';
import { clearAllClaims } from './helpers/claimStore';

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
      <main className="mx-auto max-w-[1400px] px-6 py-8">
        <header className="mb-6 overflow-hidden rounded-xl border border-line bg-white">
          <div className="flex items-center justify-between gap-6 bg-linear-to-r from-accent-deep to-accent px-6 py-5 text-white">
            <div className="flex items-center gap-4">
              <Logo className="h-11 w-11 shrink-0" />
              <div>
                <h1 className="text-xl font-semibold tracking-tight">837 Translator</h1>
                <p className="mt-0.5 text-sm text-white/80">
                  Both directions of an ASC X12 837 Professional translation, and a way to check that
                  neither one changed anything.
                </p>
              </div>
            </div>
          </div>

          {/*
            PROVENANCE: ADR-030 - the image is a licensed photograph, and says what it is.

            The caption is not decoration. This is a real clinical photograph on a page whose whole
            discipline is not implying clinical reality, so it states that it illustrates nothing on
            the page. Nothing in it is data, and no claim below relates to it.
          */}
          <figure className="relative m-0 block border-t border-accent-deep/20">
            <img
              src="/clinic.webp"
              alt=""
              width={1600}
              height={1067}
              className="block h-56 w-full object-cover sm:h-72"
              style={{ objectPosition: 'center 40%' }}
            />
            <figcaption
              className="absolute right-0 bottom-0 rounded-tl-md bg-black/55 px-2.5 py-1 text-xs
                text-white/90 backdrop-blur-sm"
            >
              Stock photograph. Illustrative only — unrelated to any bill shown here.
            </figcaption>
          </figure>
        </header>

        {/*
          PROVENANCE: ADR-024 - the charges are real published CMS figures of the right order of
          magnitude and the providers are real NPIs from the public NPPES snapshot, attached to
          invented subscribers in combinations that never happened. It looks like production data
          because most of it is real, which is exactly why it is labelled here rather than in a
          footnote (frontend governance Section 6).
        */}
        <p
          role="note"
          className="mb-6 flex gap-3 rounded-lg border border-differs/30 bg-differs-soft px-4 py-3 text-sm text-ink"
        >
          <span aria-hidden="true" className="mt-0.5 text-base leading-none text-differs">&#9888;</span>
          <span>
          <strong className="font-semibold">Example data.</strong> Charges come from published CMS fee
          schedules and providers from the public NPI registry, but subscribers and diagnoses are
          invented and no bill here describes anything that happened. Nothing on this page is a price
          or a medical record.
          </span>
        </p>

        <div
          role="tablist"
          aria-label="Translation direction"
          className="mb-5 flex gap-1 border-b border-line"
        >
          {TABS.map((tab) => (
            <button
              key={tab.id}
              role="tab"
              type="button"
              id={`tab-${tab.id}`}
              aria-selected={active === tab.id}
              aria-controls={`panel-${tab.id}`}
              onClick={() => setActive(tab.id)}
              className={cn(
                '-mb-px border-b-2 px-4 py-2 text-sm font-medium transition-colors',
                active === tab.id
                  ? 'border-accent text-accent'
                  : 'border-transparent text-muted hover:text-ink',
              )}
            >
              {tab.label}
            </button>
          ))}
        </div>

        <div role="tabpanel" id="panel-import" aria-labelledby="tab-import" hidden={active !== 'import'}>
          {active === 'import' && <ImportTab />}
        </div>

        <div role="tabpanel" id="panel-generate" aria-labelledby="tab-generate" hidden={active !== 'generate'}>
          {active === 'generate' && <GenerateTab />}
        </div>
      </main>
    </QueryClientProvider>
  );
}
