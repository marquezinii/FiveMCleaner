import assert from "node:assert/strict";
import { access, readFile } from "node:fs/promises";
import test from "node:test";

test("keeps the dashboard on the Ralven public brand", async () => {
  const [html, brandStyles, headers, app, readme, diagnostics] = await Promise.all([
    readFile(new URL("../index.html", import.meta.url), "utf8"),
    readFile(new URL("../assets/brand.css", import.meta.url), "utf8"),
    readFile(new URL("../_headers", import.meta.url), "utf8"),
    readFile(new URL("../assets/app.js", import.meta.url), "utf8"),
    readFile(new URL("../README.md", import.meta.url), "utf8"),
    readFile(new URL("../../../scripts/Test-ProductionDiagnostics.ps1", import.meta.url), "utf8"),
  ]);

  assert.match(html, /Ralven/);
  assert.doesNotMatch(html, /Vemryx|FiveMCleaner/i);
  assert.match(html, /assets\/brand\.css/);
  assert.match(brandStyles, /--bg:\s*#0A0A0B/i);
  assert.match(brandStyles, /--surface:\s*#111214/i);
  assert.match(brandStyles, /--text:\s*#FFFFFF/i);
  assert.match(brandStyles, /fonts\/InterVariable\.woff2/);
  assert.match(headers, /connect-src 'self' https:\/\/api\.vemryx\.com/);
  assert.match(app, /DEFAULT_API_BASE = 'https:\/\/api\.vemryx\.com'/);
  assert.match(readme, /https:\/\/dashboard\.vemryx\.com/);
  assert.match(diagnostics, /DashboardUrl = 'https:\/\/dashboard\.vemryx\.com'/);
  assert.match(html, /<option value="Production">Produção<\/option>/);
  await access(new URL("../assets/fonts/InterVariable.woff2", import.meta.url));
});
