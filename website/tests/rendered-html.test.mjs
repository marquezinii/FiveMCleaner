import assert from "node:assert/strict";
import { access, readFile } from "node:fs/promises";
import test from "node:test";

const outputRoot = new URL("../out/", import.meta.url);

test("exports the Portuguese FiveMCleaner landing page", async () => {
  const html = await readFile(new URL("index.html", outputRoot), "utf8");

  assert.match(
    html,
    /<title>FiveMCleaner — Otimização transparente para FiveM<\/title>/i,
  );
  assert.match(html, /lang="pt-BR"/i);
  assert.match(html, /Seu FiveM mais fluido\./i);
  assert.match(html, /Download do instalador/i);
  assert.match(html, /Escolha o perfil\. O app cuida do restante\./i);
  assert.match(html, /Sua live continua sendo prioridade\./i);
  assert.match(html, /Sobre o SmartScreen e antivírus/i);
  assert.match(html, /Desenvolvido por Felipe Marquezini/i);
  assert.match(html, /href="\/icon\.png"/i);
  assert.match(html, /<main id="main-content">/i);
  assert.match(html, /class="skip-link"/i);
  assert.doesNotMatch(html, /codex-preview|react-loading-skeleton/i);
});

test("uses native Next static export as the only website build", async () => {
  const [page, header, copy, layout, packageJson, nextConfig] = await Promise.all([
    readFile(new URL("../app/page.tsx", import.meta.url), "utf8"),
    readFile(new URL("../app/components/SiteHeader.tsx", import.meta.url), "utf8"),
    readFile(new URL("../app/content/copy.ts", import.meta.url), "utf8"),
    readFile(new URL("../app/layout.tsx", import.meta.url), "utf8"),
    readFile(new URL("../package.json", import.meta.url), "utf8"),
    readFile(new URL("../next.config.ts", import.meta.url), "utf8"),
  ]);

  assert.match(page, /^"use client";/);
  // The language toggle now lives in SiteHeader.tsx (see app/page.tsx, which
  // renders <SiteHeader> and owns only the `language` state itself).
  assert.match(header, /setLanguage\("pt"\)/);
  assert.match(header, /setLanguage\("en"\)/);
  assert.match(
    copy,
    /https:\/\/github\.com\/marquezinii\/FiveMCleaner\/releases\/latest/,
  );
  assert.match(layout, /title: "FiveMCleaner/);
  assert.match(nextConfig, /output: "export"/);
  assert.match(nextConfig, /basePath/);
  assert.doesNotMatch(packageJson, /vinext|vite|wrangler|cloudflare/i);

  await access(new URL("icon.png", outputRoot));
  await access(new URL("og.png", outputRoot));
  await assert.rejects(
    access(new URL("../public-site/index.html", import.meta.url)),
  );
});

test("keeps the exported download page aligned with the official release channel", async () => {
  const [html, styles] = await Promise.all([
    readFile(new URL("index.html", outputRoot), "utf8"),
    readFile(new URL("../app/globals.css", import.meta.url), "utf8"),
  ]);

  assert.match(
    html,
    /https:\/\/github\.com\/marquezinii\/FiveMCleaner\/releases\/latest\/download\/FiveMCleaner-Setup-latest-win-x64\.exe/i,
  );
  assert.match(html, /GitHub Releases · sem cadastro/i);
  assert.match(html, /Rollback disponível/i);
  assert.match(
    html,
    /property="og:image" content="https:\/\/marquezinii\.github\.io\/FiveMCleaner\/og\.png"/i,
  );
  assert.match(styles, /@media \(prefers-reduced-motion: reduce\)/i);
});
