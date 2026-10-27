import { useState } from 'react';
import { ApiError, api } from '../../api/client';
import type { OrthoCaseView } from '../../api/task4';
import { isDecimalText, toTwoDecimals } from '../../lib/decimal';
import { ErrorBanner, Field } from '../Form';

/**
 * FR-ORT-008..012 (Manager only): the only route that changes an established Gross Total, Deposit or Months. A Gross increase
 * adds one PriceChange installment; a decrease is refused by the server (ortho.total_decrease_not_supported, Q-ORT-3b); a
 * Deposit / Months change never regenerates the schedule (the Manager adjusts it with the schedule batch).
 */
export function OrthoTermsForm({ view, onSaved }: { view: OrthoCaseView; onSaved: (next: OrthoCaseView) => void }) {
  const [gross, setGross] = useState(view.terms.grossTotalPrice);
  const [deposit, setDeposit] = useState(view.terms.depositAmount);
  const [months, setMonths] = useState(String(view.terms.numberOfMonths));
  const [reason, setReason] = useState('');
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const valid = isDecimalText(gross) && isDecimalText(deposit) && /^\d{1,3}$/.test(months.trim()) && reason.trim() !== '';
  const changed =
    toTwoDecimals(gross) !== view.terms.grossTotalPrice || toTwoDecimals(deposit) !== view.terms.depositAmount || months.trim() !== String(view.terms.numberOfMonths);

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      const next = await api.put<OrthoCaseView>(
        `/api/manager/orthodontic-cases/${view.treatmentCaseId}/terms`,
        { grossTotalPrice: toTwoDecimals(gross), depositAmount: toTwoDecimals(deposit), numberOfMonths: Number(months.trim()), reason: reason.trim() },
        view.version,
      );
      setReason('');
      setConfirming(false);
      onSaved(next);
    } catch (e) {
      setError(e);
      setConfirming(false);
    } finally {
      setBusy(false);
    }
  }

  const decrease = error instanceof ApiError && error.code === 'ortho.total_decrease_not_supported';
  return (
    <div className="card">
      <h2>تعديل شروط التقويم (المدير)</h2>
      <ErrorBanner error={error} />
      {decrease && <p className="hint">يمكن زيادة الإجمالي فقط؛ التخفيض غير مدعوم في هذا الإصدار. لم يتم حفظ أي تغيير.</p>}
      <div className="inline">
        <Field label="الإجمالي قبل الخصم (ج.م)" field="grossTotalPrice" error={error}>
          <input value={gross} onChange={(e) => setGross(e.target.value)} inputMode="decimal" />
        </Field>
        <Field label="المقدم (ج.م)" field="depositAmount" error={error}>
          <input value={deposit} onChange={(e) => setDeposit(e.target.value)} inputMode="decimal" />
        </Field>
        <Field label="عدد الشهور" field="numberOfMonths" error={error}>
          <input value={months} onChange={(e) => setMonths(e.target.value)} inputMode="numeric" />
        </Field>
        <Field label="السبب (إلزامي)" field="reason" error={error}>
          <input value={reason} onChange={(e) => setReason(e.target.value)} />
        </Field>
      </div>
      <p className="hint">زيادة الإجمالي تضيف قسطاً واحداً بالفرق. تغيير المقدم أو عدد الشهور لا يعيد توليد الجدول؛ عدّل الجدول بعده.</p>
      {!confirming ? (
        <button type="button" disabled={!valid || !changed} onClick={() => setConfirming(true)}>
          حفظ الشروط
        </button>
      ) : (
        <span className="actions">
          <span>تأكيد حفظ الشروط الجديدة؟ يُسجَّل التعديل في سجل المراجعة.</span>
          <button type="button" disabled={busy} onClick={() => void submit()}>
            تأكيد
          </button>
          <button type="button" disabled={busy} onClick={() => setConfirming(false)}>
            رجوع
          </button>
        </span>
      )}
    </div>
  );
}
