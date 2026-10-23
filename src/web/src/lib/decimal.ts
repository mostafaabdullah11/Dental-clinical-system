// Decimal strings without floating point: validates "123", "123.4", "123.45" and normalizes to two decimals.

const pattern = /^\d{1,10}(\.\d{1,2})?$/;

export function isDecimalText(value: string): boolean {
  return pattern.test(value.trim());
}

export function toTwoDecimals(value: string): string {
  const text = value.trim();
  const [whole = '0', fraction = ''] = text.split('.');
  return `${whole}.${(fraction + '00').slice(0, 2)}`;
}
