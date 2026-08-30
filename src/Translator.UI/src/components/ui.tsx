import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';
import type { ButtonHTMLAttributes, ReactNode, SelectHTMLAttributes } from 'react';

/**
 * The shadcn convention, kept small: components are source in this project rather than a dependency,
 * built on Tailwind, so anything here can be changed without fighting a library's opinions.
 *
 * Only what this application actually uses is here. A component library's worth of unused variants
 * would be surface to maintain for a utility page with two tabs.
 */
export const cn = (...classes: ClassValue[]) => twMerge(clsx(classes));

const VARIANTS = {
  primary: 'bg-accent text-white hover:opacity-90 disabled:opacity-40',
  secondary: 'border border-line bg-white hover:bg-surface disabled:opacity-40',
} as const;

export function Button({
  variant = 'secondary',
  className,
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: keyof typeof VARIANTS }) {
  return (
    <button
      {...rest}
      className={cn(
        'inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium',
        'transition-opacity disabled:cursor-not-allowed',
        VARIANTS[variant],
        className,
      )}
    />
  );
}

/**
 * A native select, styled.
 *
 * Deliberately not a Radix listbox: the category picker is multi-select, which Radix Select cannot
 * do, and a native control keeps keyboard behaviour, mobile pickers and test queries working
 * without any of it being reimplemented.
 */
export function Field({
  label,
  hint,
  id,
  className,
  children,
  ...rest
}: SelectHTMLAttributes<HTMLSelectElement> & { label: string; hint?: string; id: string }) {
  const hintId = hint ? `${id}-hint` : undefined;

  return (
    // The hint sits outside the label and is linked by aria-describedby rather than nested inside
    // it. Nested, it became part of the control's accessible name - "State Selects the billing
    // provider." - which is wrong for a screen reader and was caught by a Playwright query looking
    // for a control named exactly "State". A description is not a name.
    <div className="flex flex-col gap-1.5">
      <label className="text-sm font-medium" htmlFor={id}>
        {label}
      </label>
      <select
        {...rest}
        id={id}
        aria-describedby={hintId}
        className={cn(
          'rounded-md border border-line bg-white px-2.5 py-1.5 text-sm',
          'focus:outline-2 focus:outline-offset-1 focus:outline-accent',
          className,
        )}
      >
        {children}
      </select>
      {hint && (
        <span id={hintId} className="text-xs text-muted">
          {hint}
        </span>
      )}
    </div>
  );
}

export function Panel({ title, description, children }: { title: string; description?: string; children: ReactNode }) {
  return (
    <section className="rounded-lg border border-line bg-white p-5">
      <h2 className="text-base font-semibold">{title}</h2>
      {description && <p className="mt-1 max-w-2xl text-sm text-muted">{description}</p>}
      <div className="mt-4">{children}</div>
    </section>
  );
}

export function Alert({ children }: { children: ReactNode }) {
  return (
    <p role="alert" className="mt-4 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-900">
      {children}
    </p>
  );
}

/**
 * A verdict, coloured for what it means rather than for decoration.
 *
 * PROVENANCE: ADR-027 - Section 9: the two reversibility verdicts are shown separately or not at
 * all, so each gets its own pill. Colour reinforces the words here; it never replaces them, because
 * a colour alone is unreadable to anyone who cannot distinguish it.
 */
export function Verdict({ label, identical }: { label: string; identical: boolean }) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap',
        identical ? 'bg-identical-soft text-identical' : 'bg-moved-soft text-moved',
      )}
    >
      <span aria-hidden="true" className="text-[0.9em] leading-none">
        {identical ? '\u2713' : '\u2260'}
      </span>
      {label} {identical ? 'identical' : 'differs'}
    </span>
  );
}
