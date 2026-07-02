import { test, expect } from '@playwright/test';

test.setTimeout(120000);

test('REQ-UI-007 agent loop invokes a tool and the trace shows the tool step', async ({ page }) => {
  // warm (instance already built process-wide, but be safe)
  await page.goto('/ingestion');
  await page.waitForLoadState('networkidle');
  await page.getByText('Vector Store Statistics', { exact: false }).first().waitFor({ timeout: 20000 });

  await page.goto('/tool-demo');
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(() => document.title.includes('Tool Calling Demo'), null, { timeout: 15000 });
  await page.waitForTimeout(400);

  // get_weather is un-answerable without the tool -> forces a tool call
  await page.getByPlaceholder(/What's the weather/i)
    .fill('What is the current weather in Tokyo? You must call the get_weather tool to find out, then report it.');
  await page.getByRole('button', { name: 'Run Agent Loop' }).click();

  await expect(page.getByText('Execution Trace', { exact: true })).toBeVisible({ timeout: 100000 });
  await expect(page.getByText('Final Answer', { exact: true })).toBeVisible({ timeout: 100000 });
  await page.waitForTimeout(800);

  const traceText = (await page.locator('body').innerText());
  const traceSection = traceText.slice(traceText.indexOf('Execution Trace'));
  console.log('TOOL trace =>', traceSection.slice(0, 400).replace(/\n+/g, ' | '));
  await page.screenshot({ path: 'test-results/screens/tool-demo-toolcall.png', fullPage: true });

  // the trace must show an actual tool step (tool name / requested / executed), not just a final answer
  expect(traceSection).toMatch(/get_weather|Tool call|Requested tool|Executed tool|calling tool/i);
});
