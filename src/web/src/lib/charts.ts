// Pure helpers for the dashboard charts. Money stays a decimal string everywhere it is shown or compared; the conversion to a
// number here is only for choosing a bar length (a ratio of two amounts), never for a displayed or stored amount.

export interface ChartPoint {
  label: string;
  value: number;
  /** The exact text shown for the value (money keeps its decimal string). */
  text?: string;
}

/** A rounded-up axis maximum so that the tallest bar never touches the top (1, 2, 5 × 10^n steps). */
export function niceMax(max: number): number {
  if (!Number.isFinite(max) || max <= 0) {
    return 1;
  }
  const exponent = Math.floor(Math.log10(max));
  const base = 10 ** exponent;
  const fraction = max / base;
  const step = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
  return step * base;
}

/** Bar heights (0…height) for the values; an all-zero series gives zero-height bars. */
export function barHeights(values: number[], height: number): number[] {
  const top = niceMax(Math.max(0, ...values));
  return values.map((v) => (v <= 0 ? 0 : Math.max(2, Math.round((v / top) * height))));
}

/** Percentage widths (0…100) of horizontal bars relative to the largest value. */
export function barWidths(values: number[]): number[] {
  const max = Math.max(0, ...values);
  return values.map((v) => (max <= 0 || v <= 0 ? 0 : Math.max(2, Math.round((v / max) * 100))));
}

/** Decimal text → number, for bar length only. Invalid or negative text counts as 0. */
export function ratioValue(text: string | null | undefined): number {
  const value = Number(text);
  return Number.isFinite(value) && value > 0 ? value : 0;
}
