import { expect, test } from '@playwright/test';

/**
 * The portfolio walkthrough, recorded against the running application.
 *
 * The route is the argument the project makes: generate bills, export them as 837, feed that same
 * archive back in, and ask whether what came back is what went out. Doing it in that order matters -
 * the file being imported is one the viewer just watched leave, so the round trip is visible as a
 * round trip rather than as two unrelated screens.
 *
 * Pacing is deliberate. Playwright records wall-clock time, so the waits below are the edit: long
 * enough on the results to read a row, short enough between steps that nothing drags.
 *
 * One honest limitation: setInputFiles hands the file to the input directly, so no operating-system
 * file picker appears. The upload reads as the filename arriving rather than as someone choosing it.
 *
 * Record:
 *   npx playwright test walkthrough --config capture/playwright.config.ts
 */

test.use({
  // Smaller than the screenshot viewport and unscaled: this is a video, and 1500x1000 at 2x makes a
  // file far larger than the detail justifies.
  viewport: { width: 1280, height: 800 },
  deviceScaleFactor: 1,
  video: { mode: 'on', size: { width: 1280, height: 800 } },
});

test('walkthrough', async ({ page }) => {
  await page.goto('/');
  await page.waitForTimeout(2500);

  // ---------------------------------------------------------------- Model → 837
  await page.getByRole('tab', { name: /Model → 837/ }).click();
  await page.waitForTimeout(1200);

  // Nothing can be chosen until the server has said what there is to choose.
  const state = page.getByLabel(/^State$/);
  await expect(state.locator('option')).not.toHaveCount(1);

  await state.selectOption({ label: 'Ohio' });
  await page.waitForTimeout(900);
  await page.getByLabel(/Number of bills/).selectOption('10');
  await page.waitForTimeout(900);
  await page.getByLabel(/Medical code categories/).selectOption(['Cardiac']);
  await page.waitForTimeout(1400);

  await page.getByRole('button', { name: /Generate bills/ }).click();
  await expect(page.getByRole('table')).toBeVisible({ timeout: 30_000 });
  await page.waitForTimeout(2600);

  // The table carries nine governed columns and scrolls rather than truncating. Show that it does.
  const scroll = async (to: 'end' | 'start') => {
    await page.evaluate((where) => {
      const el = document.querySelector('div.overflow-x-auto');
      if (el) el.scrollTo({ left: where === 'end' ? el.scrollWidth : 0, behavior: 'smooth' });
    }, to);
    await page.waitForTimeout(2000);
  };
  await scroll('end');
  await scroll('start');

  // ------------------------------------------------------- out, then back in
  const archive = page.waitForEvent('download');
  await page.getByRole('button', { name: /Export 837/ }).click();

  // Saved under the name the server actually gave it. Playwright's own download path is a GUID, and
  // uploading straight from there puts a GUID on screen where a viewer expects to see a filename.
  const saved = test.info().outputPath('claims-837.zip');
  await (await archive).saveAs(saved);
  await page.waitForTimeout(1600);

  await page.getByRole('tab', { name: /837 → Model/ }).click();
  await page.waitForTimeout(1600);

  await page.getByLabel(/837 file/).setInputFiles(saved);
  await page.waitForTimeout(1800);

  await expect(page.getByRole('table')).toBeVisible({ timeout: 30_000 });
  await page.waitForTimeout(2600);

  // ------------------------------------------------------------- the verdict
  await page.getByRole('button', { name: /^Verify$/ }).first().click();
  await expect(page.getByText(/Text identical|Text differs/).first()).toBeVisible({
    timeout: 30_000,
  });
  await page.waitForTimeout(1200);
  await scroll('end');
  await page.waitForTimeout(3000);
});
