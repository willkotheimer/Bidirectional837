/**
 * The mark: a document with a cardiac trace running through it, and an arrowhead at each end.
 *
 * Drawn rather than sourced. A stock clinical photograph would carry a licence to track and would
 * quietly argue against the thing the page says in its own banner - that none of this describes
 * anything that happened. An abstract mark makes the claim the application actually makes: this is
 * a document, it goes both ways, and the signal inside it survives the trip.
 *
 * Inline SVG rather than a file, so it inherits `currentColor` and themes with everything else.
 */
export function Logo({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 48 48"
      role="img"
      aria-label="837 Translator"
      className={className}
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <rect x="9" y="4" width="30" height="40" rx="4" className="fill-white/10" />
      <rect x="9" y="4" width="30" height="40" rx="4" stroke="currentColor" strokeWidth="2.5" />

      {/* The cardiac trace: flat, a beat, flat again - the signal that has to come back unchanged. */}
      <path
        d="M4 24h9l3.5-7 4.5 14 4-7h2.5"
        stroke="currentColor"
        strokeWidth="2.5"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path d="M31 24h13" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" />

      {/* Both directions, which is the whole point of the application. */}
      <path d="M40 20l4 4-4 4" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M8 28l-4-4 4-4" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

/**
 * The header illustration: a clinical scene reduced to the three things this application touches -
 * a patient record, a vital sign, and a charge.
 */
export function HeaderArt({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 240 120" aria-hidden="true" className={className} fill="none" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <linearGradient id="art-sweep" x1="0" y1="0" x2="240" y2="120" gradientUnits="userSpaceOnUse">
          <stop stopColor="var(--color-accent)" stopOpacity="0.14" />
          <stop offset="1" stopColor="var(--color-vital)" stopOpacity="0.05" />
        </linearGradient>
      </defs>

      <rect width="240" height="120" rx="10" fill="url(#art-sweep)" />

      {/* A cuff and its bulb: the blood-pressure reading, abstracted. */}
      <rect x="24" y="46" width="52" height="30" rx="6" stroke="var(--color-accent)" strokeWidth="2.5" opacity="0.8" />
      <path d="M76 61h12" stroke="var(--color-accent)" strokeWidth="2.5" strokeLinecap="round" opacity="0.8" />
      <circle cx="96" cy="61" r="8" stroke="var(--color-accent)" strokeWidth="2.5" opacity="0.8" />

      {/* The trace it produces. */}
      <path
        d="M112 61h14l5-14 7 28 6-14h20"
        stroke="var(--color-vital)"
        strokeWidth="2.5"
        strokeLinecap="round"
        strokeLinejoin="round"
      />

      {/* And the claim it becomes. */}
      <rect x="176" y="36" width="40" height="50" rx="5" stroke="var(--color-accent)" strokeWidth="2.5" opacity="0.8" />
      <path
        d="M185 50h22M185 60h22M185 70h13"
        stroke="var(--color-accent)"
        strokeWidth="2.5"
        strokeLinecap="round"
        opacity="0.55"
      />
    </svg>
  );
}
