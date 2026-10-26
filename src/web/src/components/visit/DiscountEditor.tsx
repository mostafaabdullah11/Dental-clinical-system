import { useState } from 'react';
import { ApiError, api } from '../../api/client';
import { isDecimalText, toTwoDecimals } from '../../lib/decimal';
import { ErrorBanner } from '../Form';

/**
 * Fixed-EGP discount (FR-FIN-022, Owner change 1): insert-only history guarded by the expected entry sequence; a
 * percentage is never accepted. Used by the Doctor (own line), Sales and the Manager before the Visit is finalized.
 */
export function DiscountEditor({ url, current, entrySeq, onSaved }: { url: string; current: string; entrySeq: number; onSaved: () => void }) {
  const [value, setValue] = useState(current);
  const [error, setError] = useState<unknown>(null);

  async function save() {
    setError(null);
    if (!isDecimalText(value)) {
      setError(new ApiError(400, 'validation_failed'));
      return;
    }
    try {
      await api.put(url, { discount: toTwoDecimals(value), expectedEntrySeq: entrySeq });
      onSaved();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <div className="inline">
      <label className="field">
        <span>الخصم (ج.م — مبلغ ثابت)</span>
        <input inputMode="decimal" value={value} onChange={(e) => setValue(e.target.value)} />
      </label>
      <button type="button" className="secondary" onClick={() => void save()}>
        حفظ الخصم
      </button>
      <ErrorBanner error={error} />
    </div>
  );
}
