import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const files = Promise.all([
  readFile(new URL('../index.html', import.meta.url), 'utf8'),
  readFile(new URL('../assets/app.js', import.meta.url), 'utf8'),
  readFile(new URL('../assets/api.js', import.meta.url), 'utf8'),
]);

test('command center exposes every investigation domain through landmark navigation', async () => {
  const [html] = await files;

  assert.match(html, /<nav[^>]+aria-label="Navegação principal"/);
  for (const id of ['overview', 'growth', 'reliability', 'compatibility', 'operations']) {
    assert.match(html, new RegExp(`id="${id}"`));
    assert.match(html, new RegExp(`href="#${id}"`));
  }
});

test('filters and incident investigation use native accessible controls', async () => {
  const [html] = await files;

  assert.match(html, /type="date" name="from"/);
  assert.match(html, /type="date" name="to"/);
  assert.match(html, /id="incident-search"[^>]+type="search"/);
  assert.match(html, /<dialog id="detail-dialog"/);
  assert.match(html, /aria-live="polite"/);
});

test('sensitive live-message changes keep confirmation and CSRF protection', async () => {
  const [, app, api] = await files;

  assert.match(app, /confirm\(`Publicar este aviso para todos os aplicativos ativos\?/);
  assert.match(app, /confirm\('Desativar o aviso ao vivo atual\?'\)/);
  assert.match(api, /'X-Ralven-Csrf-Token': csrfToken/);
});

test('partial source failures are surfaced instead of becoming zero-value metrics', async () => {
  const [, app] = await files;

  assert.match(app, /result\?\.error \|\| !hasRows\(result\) \? '—' : value/);
  assert.match(app, /Fonte indisponível; tente atualizar novamente/);
  assert.match(app, /aria-busy/);
});

test('operational health consumes the current telemetry stability signals', async () => {
  const [, app] = await files;

  for (const signal of ['app-initializations-per-day', 'abandoned-optimizations', 'gtav-benchmark-outcomes']) {
    assert.match(app, new RegExp(signal));
  }
});

test('command deck prioritizes real operational signals and keeps telemetry gaps explicit', async () => {
  const [html, app] = await files;

  assert.match(html, /id="command-posture"/);
  assert.match(html, /id="command-priorities"/);
  assert.match(html, /id="coverage-available"/);
  assert.match(html, /Instrumentação ainda pendente/);
  assert.match(html, /ainda não são emitidos pelo aplicativo/);
  assert.match(app, /function renderCommandBrief/);
  assert.match(app, /não substitui uma fonte indisponível por valores nulos/);
});

test('empty charts explain both unavailable sources and the event needed to populate the view', async () => {
  const [, app] = await files;

  assert.match(app, /function renderChartEmpty/);
  assert.match(app, /Fonte indisponível/);
  assert.match(app, /definition\.empty/);
  assert.match(app, /chart-empty-state/);
});
