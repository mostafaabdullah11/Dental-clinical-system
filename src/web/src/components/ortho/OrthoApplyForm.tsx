import { useState } from 'react';
import { api } from '../../api/client';
import { installmentStatusLabels, isPositive, type OrthoCaseView } from '../../api/task4';
import { isDecimalText, toTwoDecimals } from '../../lib/decimal';
import { ErrorBanner } from '../Form';

/**
 * FR-ORT-006 (Q-ORT-2): the user chooses which installment(s) a collection is applied to; there is no automatic oldest-first.
 * The server caps each application by the collection's available amount and the installment's remaining amount.
 */
export function OrthoApplyForm({ view, onApplied }: { view: OrthoCaseView; onApplied: (next: OrthoCaseView) => void }) {
  const available = view.allocations.filter((a) => isPositive(a.available));
  const open = view.installments.filter((i) => i.isActive && isPositive(i.remaining));
  const [allocationId, setAllocationId] = useState(available[0]?.allocationId ?? '');
  const [amounts, setAmounts] = useState<Record<string, string>>({});
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const lines = Object.entries(amounts).filter(([, v]) => v.trim() !== '');
  const invalid = lines.some(([, v]) => !isDecimalText(v));

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      const next = await api.post<OrthoCaseView>(`/api/sales/orthodontic-cases/${view.treatmentCaseId}/installment-payments`, {
        applications: lines.map(([installmentId, v]) => ({ allocationId, installmentId, amount: toTwoDecimals(v) })),
      });
      setAmounts({});
      setConfirming(false);
      onApplied(next);
    } catch (e) {
      setError(e);
      setConfirming(false);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="card">
      <h2>تطبيق دفعة على الأقساط</h2>
      <ErrorBanner error={error} />
      {available.length === 0 || open.length === 0 ? (
        <p className="hint">
          {available.length === 0 ? 'لا يوجد مبلغ متاح للتطبيق. سجّل الدفعة أولاً من الحساب المالي للمريض.' : 'لا توجد أقساط مفتوحة.'}
        </p>
      ) : (
        <>
          <label className="field">
            <span>الدفعة</span>
            <select value={allocationId} onChange={(e) => setAllocationId(e.target.value)}>
              {available.map((a) => (
                <option key={a.allocationId} value={a.allocationId}>
                  {a.allocatedAt.slice(0, 10)} — المتاح {a.available} ج.م
                </option>
              ))}
            </select>
          </label>
          <table>
            <thead>
              <tr>
                <th>القسط</th>
                <th>الاستحقاق</th>
                <th>المتبقي</th>
                <th>الحالة</th>
                <th>المبلغ المطبق</th>
              </tr>
            </thead>
            <tbody>
              {open.map((i) => (
                <tr key={i.id}>
                  <td>{i.number}</td>
                  <td>{i.dueDate}</td>
                  <td>{i.remaining}</td>
                  <td>{installmentStatusLabels[i.status]}</td>
                  <td>
                    <input
                      aria-label={`المبلغ المطبق على القسط ${i.number}`}
                      value={amounts[i.id] ?? ''}
                      onChange={(e) => setAmounts({ ...amounts, [i.id]: e.target.value })}
                      inputMode="decimal"
                      size={10}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {invalid && <small className="field-error">يرجى إدخال مبالغ صحيحة بحد أقصى رقمين عشريين.</small>}
          {!confirming ? (
            <button type="button" disabled={lines.length === 0 || invalid || !allocationId} onClick={() => setConfirming(true)}>
              تطبيق
            </button>
          ) : (
            <span className="actions">
              <span>تأكيد تطبيق {lines.length} مبلغ على الأقساط المختارة؟</span>
              <button type="button" disabled={busy} onClick={() => void submit()}>
                تأكيد
              </button>
              <button type="button" disabled={busy} onClick={() => setConfirming(false)}>
                رجوع
              </button>
            </span>
          )}
        </>
      )}
    </div>
  );
}
