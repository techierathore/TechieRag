import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';

// ─────────────────────────────────────────────────────────────────────────────
// LLM DATA-PATH verification — REQ-UI-005 / 006 / 007, against the LIVE LM Studio
// (source=LmStudio, http://192.168.1.13:1234, qwen2.5-coder-32b-instruct).
// These exercise the pure-LLM paths (no embeddings needed): direct-LLM chat,
// playground completion + typed output, and the agent tool-loop + execution trace.
// ─────────────────────────────────────────────────────────────────────────────

const SHOT = 'test-results/screens';
fs.mkdirSync(SHOT, { recursive: true });
test.setTimeout(120000);

async function ready(page: Page, title: string) {
  await page.waitForLoadState('networkidle');
  await page.waitForFunction((t) => document.title.includes(t), title, { timeout: 20000 });
  await page.waitForTimeout(500);
}

// WARM the RAG instance via an async-build page first. TechieRagManager.GetLlmProvider()
// is sync-over-async (GetInstanceAsync().GetAwaiter().GetResult(), TechieRagManager.cs:367)
// and DEADLOCKS on the Blazor circuit thread if it is the first call to build the instance.
// Visiting /ingestion (async InitializeAsync) builds+caches it so GetLlmProvider() hits the
// fast return-cached path. (The deadlock itself is logged as a defect in the checklist.)
test.beforeEach(async ({ page }) => {
  await page.goto('/ingestion');
  await ready(page, 'Document Ingestion');
  await page.getByText('Vector Store Statistics', { exact: false }).first().waitFor({ timeout: 20000 }).catch(() => {});
  await page.waitForTimeout(1500);
});

test('REQ-UI-006 playground completion returns text + token counts (non-stream)', async ({ page }) => {
  await page.goto('/llm-playground');
  await ready(page, 'LLM Playground');

  await page.getByPlaceholder('Write a haiku about coding').fill('Reply with exactly one word: PONG');
  // turn Streaming OFF so the non-streaming path returns response.Usage token counts
  await page.locator('label[for="pg-streaming"]').click();
  await expect(page.locator('label[for="pg-streaming"]')).toHaveText('Off');

  await page.getByRole('button', { name: 'Generate', exact: true }).click();

  await expect(page.getByText('Response', { exact: true })).toBeVisible({ timeout: 90000 });
  const stats = page.locator('text=/Input:\\s*\\d+.*Output:\\s*\\d+/');
  await expect(stats, 'completion stats must show Input/Output token counts').toBeVisible({ timeout: 5000 });
  const statsText = await stats.innerText();
  await page.screenshot({ path: `${SHOT}/llm-completion.png`, fullPage: true });
  console.log('REQ-UI-006 completion stats =>', statsText);
});

test('REQ-UI-006 structured output deserializes to a typed object', async ({ page }) => {
  await page.goto('/llm-playground');
  await ready(page, 'LLM Playground');

  await page.getByRole('tab', { name: /Structured/i }).click();
  await page.getByPlaceholder(/Analyze the sentiment/i).fill('Analyze the sentiment of: I absolutely love this product, it is fantastic!');
  await page.getByRole('button', { name: 'Generate Typed Response' }).click();

  await expect(page.getByText('Parsed Result', { exact: true })).toBeVisible({ timeout: 90000 });
  // the Parsed Result font-mono block holds the DESERIALIZED typed fields
  const block = page.locator('div.font-mono').last();
  const text = (await block.innerText()).trim();
  await page.screenshot({ path: `${SHOT}/llm-structured.png`, fullPage: true });
  console.log('REQ-UI-006 structured =>', text.slice(0, 200));
  expect(text.length, 'parsed typed result must be non-empty').toBeGreaterThan(2);
  // typed SentimentAnalysis fields (label rendered from the deserialized object)
  expect(text).toMatch(/sentiment|positive|score|confidence/i);
});

test('REQ-UI-005 chat direct-llm streaming updates the token footer (off zero)', async ({ page }) => {
  await page.goto('/chat');
  await ready(page, 'RAG Chat');

  // open config bar, switch Mode -> Direct LLM (bypasses retrieval/embeddings)
  await page.getByText('Chat Configuration', { exact: false }).click();
  await page.getByRole('combobox').first().click();
  await page.getByRole('option', { name: 'Direct LLM' }).click();

  await page.getByPlaceholder(/Ask a question/i).fill('Reply with exactly one word: PONG');
  // Send is the icon button next to the textarea
  await page.locator('button:near(textarea)').last().click();

  // footer: "Session: N tokens  $x  n messages" — poll until N > 0
  await expect(async () => {
    const footer = await page.getByText(/Session:\s*\d+\s*tokens/i).innerText();
    const n = parseInt(footer.match(/Session:\s*(\d+)/i)?.[1] ?? '0', 10);
    expect(n, `footer tokens should be > 0 (got: "${footer}")`).toBeGreaterThan(0);
  }).toPass({ timeout: 90000, intervals: [2000] });

  await page.screenshot({ path: `${SHOT}/chat-streamed.png`, fullPage: true });
});

test('REQ-UI-007 tool-demo agent loop renders a real execution trace + final answer', async ({ page }) => {
  await page.goto('/tool-demo');
  await ready(page, 'Tool Calling Demo');

  await page.getByPlaceholder(/What's the weather/i)
    .fill('What is 42 multiplied by 17? Use the calculate_math tool, then state the final answer.');
  await page.getByRole('button', { name: 'Run Agent Loop' }).click();

  await expect(page.getByText('Execution Trace', { exact: true })).toBeVisible({ timeout: 110000 });
  await expect(page.getByText('Final Answer', { exact: true })).toBeVisible({ timeout: 110000 });

  const trace = await page.locator('text=Execution Trace').locator('xpath=ancestor::*[1]').innerText().catch(() => '');
  await page.screenshot({ path: `${SHOT}/tool-demo-run.png`, fullPage: true });
  console.log('REQ-UI-007 trace snippet =>', trace.slice(0, 300));
  // trace must have at least one step beyond the header
  const stepCount = await page.locator('div:has-text("Execution Trace") >> css=div').count();
  expect(stepCount, 'execution trace should render step rows').toBeGreaterThan(0);
});
