// Frontend unit tests (DEPLOYMENT_ARCHITECTURE.md §2.2 "frontend" job). Node's built-in runner with native type stripping:
// `npm run test` — no extra dependencies. Covers the pure helpers the UI relies on for money and clinic time.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { isDecimalText, toTwoDecimals } from '../src/lib/decimal.ts';
import { isPositive, moneyCompare } from '../src/api/task4.ts';
import { addDays, clinicLocalToIso, isoToClinicLocal } from '../src/lib/time.ts';

test('decimal text accepts at most two decimals and never uses floating point', () => {
  for (const ok of ['0', '12', '12.5', '12.50', '9999999999.99']) {
    assert.equal(isDecimalText(ok), true, ok);
  }
  for (const bad of ['', '-1', '1.234', '1e3', '١٢', 'abc', '12.']) {
    assert.equal(isDecimalText(bad), false, bad);
  }
  assert.equal(toTwoDecimals('7'), '7.00');
  assert.equal(toTwoDecimals('7.5'), '7.50');
  assert.equal(toTwoDecimals(' 2600.25 '), '2600.25');
});

test('money comparison is exact on decimal strings (no binary rounding)', () => {
  assert.equal(moneyCompare('0.10', '0.1'), 0);
  assert.equal(moneyCompare('18000.00', '17999.99'), 1);
  assert.equal(moneyCompare('-70.00', '0.00'), -1);
  assert.equal(moneyCompare('9999999999.99', '9999999999.98'), 1);
  assert.equal(isPositive('0.00'), false);
  assert.equal(isPositive('0.01'), true);
});

test('clinic-time conversion round-trips in the clinic zone, including across a DST change', () => {
  const zone = 'Africa/Cairo';
  for (const local of ['2026-01-15T09:30', '2026-07-15T09:30', '2026-10-30T23:30']) {
    assert.equal(isoToClinicLocal(clinicLocalToIso(local, zone), zone), local);
  }
  assert.equal(addDays('2026-02-28', 1), '2026-03-01');
  assert.equal(addDays('2026-03-01', -1), '2026-02-28');
});
