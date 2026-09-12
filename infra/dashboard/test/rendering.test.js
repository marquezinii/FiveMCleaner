import { test } from 'node:test';
import assert from 'node:assert/strict';
import { findChartHit, formatChartTooltip } from '../assets/rendering.js';

test('findChartHit recognizes bar, point, and donut interaction regions', () => {
  const regions = [
    { kind: 'rect', index: 0, x: 10, y: 10, width: 40, height: 20 },
    { kind: 'circle', index: 1, x: 100, y: 100, radius: 10 },
    { kind: 'arc', index: 2, x: 200, y: 200, innerRadius: 20, outerRadius: 50, start: 0, end: Math.PI / 2 },
  ];

  assert.equal(findChartHit(regions, 25, 15)?.index, 0);
  assert.equal(findChartHit(regions, 105, 105)?.index, 1);
  assert.equal(findChartHit(regions, 230, 220)?.index, 2);
  assert.equal(findChartHit(regions, 0, 0), null);
});

test('formatChartTooltip preserves the full label and adds exact value and share', () => {
  assert.deepEqual(
    formatChartTooltip({ label: 'Intel(R) Core(TM) i5-12400F', value: 25 }, 100, { valueLabel: 'eventos' }),
    ['Intel(R) Core(TM) i5-12400F', '25 eventos', '25% do total exibido'],
  );
});
