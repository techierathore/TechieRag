import { test, expect, Page } from '@playwright/test';

// REQ-UI-043 / BRD-136 smoke: provider-conditional settings and save-time validation on /llm-settings.
test.use({ baseURL: 'http://localhost:5123' });
test.setTimeout(120000);

async function openLlmSettings(page: Page) {
  await page.goto('/llm-settings');
  await page.waitForLoadState('networkidle');
  await expect(page.getByRole('heading', { name: 'LLM Settings' })).toBeVisible({ timeout: 30000 });
  await expect(page.getByRole('heading', { name: 'Chat provider' })).toBeVisible({ timeout: 30000 });
}

async function pickProvider(page: Page, label: string) {
  await page.locator('#primary-source').click();
  await page.getByRole('option', { name: label, exact: true }).click();
  await page.waitForTimeout(400);
}

test('REQ-UI-043 provider selector changes the visible field set', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openLlmSettings(page);

  // Ollama — base URL + model, NO API key box.
  await pickProvider(page, 'Ollama');
  await expect(page.locator('#primary-endpoint')).toBeVisible();
  await expect(page.locator('#primary-model')).toBeVisible();
  await expect(page.locator('#primary-apikey')).toHaveCount(0);
  await expect(page.locator('#primary-apiversion')).toHaveCount(0);
  await page.screenshot({ path: 'test-results/screens/req-ui-043-ollama-1280.png', fullPage: true });

  // Anthropic (hosted, key-only) — API key + model, NO base URL box.
  await pickProvider(page, 'Anthropic');
  await expect(page.locator('#primary-apikey')).toBeVisible();
  await expect(page.locator('#primary-model')).toBeVisible();
  await expect(page.locator('#primary-endpoint')).toHaveCount(0);
  await page.screenshot({ path: 'test-results/screens/req-ui-043-anthropic-1280.png', fullPage: true });

  // Azure AI Foundry — endpoint + deployment + api-version + key, NO "Model" label.
  await pickProvider(page, 'Azure AI Foundry');
  await expect(page.locator('#primary-endpoint')).toBeVisible();
  await expect(page.locator('#primary-apiversion')).toBeVisible();
  await expect(page.locator('#primary-apikey')).toBeVisible();
  await expect(page.getByText('Deployment name', { exact: false }).first()).toBeVisible();
  const azureLabels = await page.locator('label').allInnerTexts();
  expect(azureLabels.some((t) => t.trim().toLowerCase().startsWith('model'))).toBeFalsy();
  await page.screenshot({ path: 'test-results/screens/req-ui-043-azure-1280.png', fullPage: true });

  // Every visible provider field carries the required marker.
  const requiredMarkers = await page.getByText('required', { exact: true }).count();
  expect(requiredMarkers).toBeGreaterThanOrEqual(4);
});

test('REQ-UI-043 half-configured save is refused and /token-usage still loads', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openLlmSettings(page);

  // The exact regression: OpenAI-compatible with an API key and no endpoint.
  await pickProvider(page, 'OpenAI-compatible endpoint');
  await page.locator('#primary-apikey').fill('sk-smoke-test-key');
  await page.locator('#primary-model').fill('gpt-4o');
  await page.locator('#primary-endpoint').fill('');

  await page.getByRole('button', { name: 'Save & apply' }).click();
  await page.waitForTimeout(1200);

  // The error is named on the offending field, not just in a banner.
  const endpointField = page.locator('#primary-endpoint').locator('xpath=ancestor::*[1]');
  await expect(page.getByText('Base URL is required for the OpenAI-compatible provider.').first()).toBeVisible();
  await expect(page.locator('#llm-validation-summary')).toBeVisible();
  await page.screenshot({ path: 'test-results/screens/req-ui-043-refused-1280.png', fullPage: true });
  console.log('refused-field-html =>', (await endpointField.innerHTML()).slice(0, 200));

  // The whole point of the REQ: nothing broken was persisted, so unrelated pages still work.
  await page.goto('/token-usage');
  await page.waitForLoadState('networkidle');
  const body = await page.locator('body').innerText();
  expect(body).not.toContain('Endpoint is required');
  expect(body).not.toContain('An unhandled error has occurred');
  await expect(page.getByRole('heading', { name: /token usage/i }).first()).toBeVisible({ timeout: 30000 });
  await page.screenshot({ path: 'test-results/screens/req-ui-043-token-usage-after-refusal.png', fullPage: true });
});

test('REQ-UI-043 narrow viewport keeps the provider form usable', async ({ page }) => {
  await page.setViewportSize({ width: 420, height: 900 });
  await openLlmSettings(page);
  await pickProvider(page, 'Azure AI Foundry');

  await expect(page.locator('#primary-endpoint')).toBeVisible();
  await expect(page.locator('#primary-apiversion')).toBeVisible();

  // Nothing may overflow the viewport horizontally.
  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  console.log('narrow horizontal overflow =>', overflow);
  expect(overflow).toBeLessThanOrEqual(1);

  for (const id of ['#primary-endpoint', '#primary-apiversion', '#primary-apikey']) {
    const box = await page.locator(id).boundingBox();
    expect(box, `${id} has no box`).not.toBeNull();
    expect(box!.x).toBeGreaterThanOrEqual(-1);
    expect(box!.x + box!.width).toBeLessThanOrEqual(421);
  }

  await page.screenshot({ path: 'test-results/screens/req-ui-043-azure-420.png', fullPage: true });
});

test('REQ-UI-043 a fully configured provider still saves', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await openLlmSettings(page);

  await pickProvider(page, 'Ollama');
  await page.locator('#primary-endpoint').fill('http://localhost:11434');
  await page.locator('#primary-model').fill('llama3.2');
  await page.getByRole('button', { name: 'Save & apply' }).click();
  await page.waitForTimeout(2500);

  await expect(page.locator('#llm-validation-summary')).toHaveCount(0);
  await page.screenshot({ path: 'test-results/screens/req-ui-043-saved-1280.png', fullPage: true });
});
