import assert from "node:assert/strict";
import { access, readFile, readdir } from "node:fs/promises";
import test from "node:test";

const templateRoot = new URL("../", import.meta.url);
const previewRoot = new URL("../app/_sites-preview/", import.meta.url);

async function render() {
  const workerUrl = new URL("../dist/server/index.js", import.meta.url);
  workerUrl.searchParams.set("test", `${process.pid}-${Date.now()}`);
  const { default: worker } = await import(workerUrl.href);

  return worker.fetch(
    new Request("http://localhost/", {
      headers: { accept: "text/html" },
    }),
    {
      ASSETS: {
        fetch: async () => new Response("Not found", { status: 404 }),
      },
    },
    {
      waitUntil() {},
      passThroughOnException() {},
    },
  );
}

test("server-renders the Portuguese FiveMCleaner landing page", async () => {
  const response = await render();
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type") ?? "", /^text\/html\b/i);

  const html = await response.text();
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

test("removes starter artifacts and keeps the Sites build cross-platform", async () => {
  const [page, layout, packageJson, files] = await Promise.all([
    readFile(new URL("../app/page.tsx", import.meta.url), "utf8"),
    readFile(new URL("../app/layout.tsx", import.meta.url), "utf8"),
    readFile(new URL("../package.json", import.meta.url), "utf8"),
    readdir(previewRoot).catch((error) => {
      if (error && error.code === "ENOENT") {
        return [];
      }

      throw error;
    }),
  ]);

  assert.deepEqual(files, []);
  assert.match(page, /^"use client";/);
  assert.match(page, /setLanguage\("pt"\)/);
  assert.match(page, /setLanguage\("en"\)/);
  assert.match(
    page,
    /https:\/\/github\.com\/marquezinii\/FiveMCleaner\/releases\/latest/,
  );
  assert.match(layout, /title: "FiveMCleaner/);
  assert.doesNotMatch(layout, /codex-preview|Starter Project|_sites-preview/);
  assert.match(packageJson, /"cross-env":/);
  assert.doesNotMatch(packageJson, /react-loading-skeleton/);

  await access(new URL("../public/icon.png", import.meta.url));
  await assert.rejects(access(new URL("public/_sites-preview", templateRoot)));
});
