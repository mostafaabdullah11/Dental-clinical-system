// Unit tests for the dashboard chart helpers (pure functions, Node's built-in runner).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { barHeights, barWidths, niceMax, ratioValue } from '../src/lib/charts.ts';
import { lastDays } from '../src/lib/time.ts';

test('lastDays returns the requested number of calendar dates ending at the given day, oldest first', () => {
  assert.deepEqual(lastDays('2026-03-02', 4), ['2026-02-27', '2026-02-28', '2026-03-01', '2026-03-02']);
  assert.equal(lastDays('2026-10-28', 7).length, 7);
  assert.equal(lastDays('2026-10-28', 7)[6], '2026-10-28');
});

test('niceMax rounds up to 1, 2, 5 or 10 times a power of ten and never returns zero', () => {
  assert.equal(niceMax(0), 1);
  assert.equal(niceMax(-3), 1);
  assert.equal(niceMax(Number.NaN), 1);
  assert.equal(niceMax(7), 10);
  assert.equal(niceMax(1200), 2000);
  assert.equal(niceMax(4300), 5000);
  assert.equal(niceMax(5001), 10000);
});

test('bar heights are proportional, zero stays zero and a positive value is always visible', () => {
  assert.deepEqual(barHeights([0, 0, 0], 100), [0, 0, 0]);
  const heights = barHeights([0, 1, 100], 100);
  assert.equal(heights[0], 0);
  assert.ok((heights[1] ?? 0) >= 2);
  assert.equal(heights[2], 100);
});

test('bar widths are relative to the largest value', () => {
  assert.deepEqual(barWidths([0, 0]), [0, 0]);
  assert.deepEqual(barWidths([5, 10]), [50, 100]);
  assert.equal(barWidths([1, 1000])[0], 2);
});

test('ratio values come from decimal text and ignore invalid or negative text', () => {
  assert.equal(ratioValue('1250.50'), 1250.5);
  assert.equal(ratioValue('0.00'), 0);
  assert.equal(ratioValue('-5.00'), 0);
  assert.equal(ratioValue('abc'), 0);
  assert.equal(ratioValue(null), 0);
});
