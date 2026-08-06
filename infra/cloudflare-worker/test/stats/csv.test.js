import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toCsv } from '../../src/stats/csv.js';

test('toCsv returns an empty string for no rows', () => {
  assert.equal(toCsv([]), '');
  assert.equal(toCsv(null), '');
  assert.equal(toCsv(undefined), '');
});

test('toCsv writes a header row from the first row\'s keys', () => {
  const csv = toCsv([{ day: '2026-01-01', runs: 12 }]);

  assert.equal(csv, 'day,runs\n2026-01-01,12');
});

test('toCsv writes one line per row in order', () => {
  const csv = toCsv([
    { day: '2026-01-01', runs: 12 },
    { day: '2026-01-02', runs: 30 },
  ]);

  assert.equal(csv, 'day,runs\n2026-01-01,12\n2026-01-02,30');
});

test('toCsv renders null and undefined as an empty field', () => {
  const csv = toCsv([{ error_category: null, occurrences: 3 }]);

  assert.equal(csv, 'error_category,occurrences\n,3');
});

test('toCsv quotes and escapes values containing commas, quotes, or newlines', () => {
  const csv = toCsv([{ label: 'a "quoted", value\nwith newline' }]);

  assert.equal(csv, 'label\n"a ""quoted"", value\nwith newline"');
});

test('toCsv neutralizes spreadsheet formula injection from leading = + - @ tab and CR', () => {
  const csv = toCsv([
    { label: '=HYPERLINK("https://evil.example")', runs: 1 },
    { label: '+cmd', runs: 2 },
    { label: '@sum(1,2)', runs: 3 },
    { label: '-2+3', runs: 4 },
    { label: '\t=evil', runs: 5 },
  ]);
  const lines = csv.split('\n').slice(1);

  assert.equal(lines[0], `"'=HYPERLINK(""https://evil.example"")",1`);
  assert.equal(lines[1], `"'+cmd",2`);
  assert.equal(lines[2], `"'@sum(1,2)",3`);
  assert.equal(lines[3], `"'-2+3",4`);
  assert.equal(lines[4], `"'\t=evil",5`);
});

test('toCsv leaves a plain leading dash inside an otherwise ordinary value untouched', () => {
  const csv = toCsv([{ label: 'x86-64' }]);
  assert.equal(csv, 'label\nx86-64');
});
