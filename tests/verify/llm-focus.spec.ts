import { test, expect } from '@playwright/test';

test.setTimeout(60000);

test('REQ-UI-006 warm completion against live LM Studio', async ({ page }) => {
  // 1) WARM the RAG instance via async-build page (avoids GetLlmProvider cold-start deadlock)
  await page.goto('/ingestion');
  await page.waitForLoadState('networkidle');
  await page.getByText('Vector Store Statistics', { exact: false }).first().waitFor({ timeout: 20000 });

  // 2) Now the singleton instance is cached — completion should reach LM Studio
  await page.goto('/llm-playground');
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(() => document.title.includes('LLM Playground'), null, { timeout: 15000 });
  await page.waitForTimeout(400);

  await page.getByPlaceholder('Write a haiku about coding').fill('Reply with exactly one word: PONG');
  await page.locator('label[for="pg-streaming"]').click(); // streaming OFF -> non-stream returns usage tokens

  await page.getByRole('button', { name: 'Generate', exact: true }).click();

  // fail fast (30s) — a warm completion is ~1-3s; a hang means deadlock persists
  await expect(page.getByText('Response', { exact: true })).toBeVisible({ timeout: 30000 });
  const resp = (await page.locator('div.whitespace-pre-wrap').first().innerText()).trim();
  const stats = (await page.locator('text=/Input:\\s*\\d+/').innerText().catch(() => '')).trim();
  console.log('FOCUS completion response =>', JSON.stringify(resp.slice(0, 120)));
  console.log('FOCUS completion stats    =>', JSON.stringify(stats));
  await page.screenshot({ path: 'test-results/screens/llm-focus-completion.png', fullPage: true });

  expect(resp.length).toBeGreaterThan(0);
  expect(stats).toMatch(/Input:\s*\d+\s*\|\s*Output:\s*\d+/);
});
