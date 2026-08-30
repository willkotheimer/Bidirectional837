import { expect, test, type Page } from '@playwright/test';

/**
 * Portfolio screenshots, captured from the running application rather than mocked up.
 *
 * Each shot has to earn its caption: the generate tab showing selectors the server actually served,
 * the import tab showing a claim reconstructed from a real interchange, and the reversibility
 * verdict showing two separate results. A composed screenshot could show any of those without them
 * being true, so every one is driven through the real UI against the real API.
 *
 * Run against the deployed app:
 *   npx playwright test capture --config capture/playwright.config.ts
 */

const OUT = '../../../firebase-portfolio-site/public/screenshots';
const SAMPLES = '../../../firebase-portfolio-site/public/samples';

/** Generates a batch through the form and waits for the table to arrive. */
async function generate(page: Page) {
  await page.getByRole('tab', { name: /Model → 837/ }).click();

  // The selectors are populated from the server, so nothing can be chosen until they arrive.
  const state = page.getByLabel(/^State$/);
  await expect(state.locator('option')).not.toHaveCount(1);

  await state.selectOption({ label: 'Ohio' });
  await page.getByLabel(/Number of bills/).selectOption('10');
  await page.getByLabel(/Medical code categories/).selectOption(['Cardiac']);

  await page.getByRole('button', { name: /Generate bills/ }).click();
  await expect(page.getByRole('table')).toBeVisible({ timeout: 30_000 });
}

test.describe('portfolio screenshots', () => {
  test('generate tab', async ({ page }) => {
    await page.goto('/');
    await generate(page);

    await page.screenshot({ path: `${OUT}/bd_generate.jpg`, quality: 88, type: 'jpeg' });
  });

  test('import tab and the reversibility verdict', async ({ page }) => {
    await page.goto('/');

    // Produce a real archive by generating first, then downloading what the server emits. Feeding
    // the importer a handmade file would prove less: this is the export path's own output.
    await generate(page);

    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: /Export 837/ }).click();
    const archive = await (await download).path();

    await page.getByRole('tab', { name: /837 → Model/ }).click();
    await page.getByLabel(/837 file/).setInputFiles(archive!);
    await expect(page.getByRole('table')).toBeVisible({ timeout: 30_000 });

    await page.screenshot({ path: `${OUT}/bd_import.jpg`, quality: 88, type: 'jpeg' });

    // The verdict is per row and on demand, so it has to be asked for before it can be shown.
    await page.getByRole('button', { name: /^Verify$/ }).first().click();
    await expect(page.getByText(/Text identical|Text differs/).first()).toBeVisible({ timeout: 30_000 });

    // The claim table carries nine governed columns and scrolls sideways rather than truncating a
    // value - which is the right behaviour and puts the verdict off-screen. Scroll to it, or the
    // screenshot shows everything except the thing it is captioned for.
    await page.evaluate(() => {
      const scroller = document.querySelector('div.overflow-x-auto');
      if (scroller) scroller.scrollLeft = scroller.scrollWidth;
    });
    await page.waitForTimeout(300);

    await page.screenshot({ path: `${OUT}/bd_verdict.jpg`, quality: 88, type: 'jpeg' });
  });

  /**
   * A matching pair of downloads for the portfolio: the same claims as CSV and as 837.
   *
   * They match because both come from one batch in one session, taken through the real buttons. The
   * 837 export sends whatever the store holds, so the app is restarted before this runs - otherwise
   * the archive would carry every claim any earlier request had left behind and the two files would
   * describe different things while looking like a pair.
   */
  test('a matching pair of exports', async ({ page }) => {
    await page.goto('/');
    await generate(page);

    const csv = page.waitForEvent('download');
    await page.getByRole('button', { name: /Export CSV/ }).click();
    await (await csv).saveAs(`${SAMPLES}/bidirectional837-sample.csv`);

    const archive = page.waitForEvent('download');
    await page.getByRole('button', { name: /Export 837/ }).click();
    await (await archive).saveAs(`${SAMPLES}/bidirectional837-sample-837.zip`);
  });
});
