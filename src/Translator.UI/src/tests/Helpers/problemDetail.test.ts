import { describe, expect, it } from 'vitest';
import { messageFor } from '../../helpers/problemDetail';

/**
 * PROVENANCE: ADR-021 - the ingestion reader refuses a malformed file by naming the segment at
 * fault. These cases are real messages the API produces, so the assertion is that they survive to
 * the user rather than being replaced by a generic failure.
 */
describe('messageFor', () => {
  it.each([
    [
      'a parser refusal names the segment',
      { detail: 'The interchange carries 0 CLM segment(s); exactly one is required.' },
      'The interchange carries 0 CLM segment(s); exactly one is required.',
    ],
    [
      'an unknown category refusal lists the known ones',
      { detail: 'No medical codes are catalogued for: Dentistry. Known categories are: Anesthesia, Cardiac.' },
      'No medical codes are catalogued for: Dentistry. Known categories are: Anesthesia, Cardiac.',
    ],
    [
      'a title is used when there is no detail',
      { title: 'Not Found' },
      'Not Found',
    ],
    [
      'detail is preferred over title',
      { title: 'Bad Request', detail: 'CLM02 is 1.00 but the SV102 line amounts sum to 2.00.' },
      'CLM02 is 1.00 but the SV102 line amounts sum to 2.00.',
    ],
    [
      'validation errors are joined when there is nothing better',
      { errors: { BillCount: ['The field BillCount must be between 1 and 500.'] } },
      'The field BillCount must be between 1 and 500.',
    ],
    [
      'multiple validation errors are all shown',
      { errors: { BillCount: ['Too many.'], JurisdictionState: ['Too long.'] } },
      'Too many. Too long.',
    ],
  ] as const)('%s', (_description, problem, expected) => {
    expect(messageFor(problem, 'Something went wrong.')).toBe(expected);
  });

  it.each([
    ['null falls back', null],
    ['undefined falls back', undefined],
    ['a string body falls back', 'Internal Server Error'],
    ['an empty document falls back', {}],
    ['a document with only blank fields falls back', { detail: '   ', title: '' }],
  ])('%s', (_description, problem) => {
    expect(messageFor(problem, 'The import could not be completed.')).toBe('The import could not be completed.');
  });
});
