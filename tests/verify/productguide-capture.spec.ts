import { test } from '@playwright/test';

// One-off capture spec for the ProductGuide: full-page desktop screenshots of every
// TechieDesk screen, saved under docs/screenshots/TechieRag/{slug}.png (rebranded app).
const SCREENS: { slug: string; route: string; title: string }[] = [
  { slug: 'home', route: '/', title: 'TechieDesk' },
  { slug: 'settings', route: '/settings', title: 'TechieRag Settings' },
  { slug: 'llm-settings', route: '/llm-settings', title: 'LLM Settings' },
  { slug: 'ingestion', route: '/ingestion', title: 'Document Ingestion' },
  { slug: 'text-ingestion', route: '/text-ingestion', title: 'Text Ingestion' },
  { slug: 'chat', route: '/chat', title: 'RAG Chat' },
  { slug: 'llm-playground', route: '/llm-playground', title: 'LLM Playground' },
  { slug: 'tool-demo', route: '/tool-demo', title: 'Tool Calling Demo' },
  { slug: 'token-usage', route: '/token-usage', title: 'Token Usage' },
  { slug: 'qdrant-admin', route: '/qdrant-admin', title: 'Qdrant Admin' },
];

for (const s of SCREENS) {
  test(`capture ${s.slug}`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(s.route, { waitUntil: 'networkidle' });
    // Blazor Server: let the SignalR circuit hydrate + data render.
    await page.waitForTimeout(1500);
    await page.screenshot({ path: `docs/screenshots/TechieRag/${s.slug}.png`, fullPage: true });
  });
}
