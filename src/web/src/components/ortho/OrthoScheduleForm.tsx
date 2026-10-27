import { useState } from 'react';
import { api } from '../../api/client';
import { installmentStatusLabels, isPositive, type InstallmentView, type OrthoCaseView } from '../../api/task4';
import { isDecimalText, toTwoDecimals } from '../../lib/decimal';
import { ErrorBanner } from '../Form';

interface RowEdit {
  amount: string;
  dueDate: string;
  retire: boolean;
}

/**
 * FR-ORT-014..018 (Manager only): one atomic batch that revises amounts, moves the due date of an unpaid installment, adds a
 * future installment or retires an unpaid one (never deletes). The batch must end valid: Deposit + Σ active installments =
 * Effective Ortho Total. Paid history is locked here and enforced again by the server (G5).
 */
export function OrthoScheduleForm({ view, onSaved }: { view: OrthoCaseView; onSaved: (next: OrthoCaseView) => void }) {
  const active = view.installments.filter((i) => i.isActive);
  const initial = (i: InstallmentView): RowEdit => ({ amount: i.amount, dueDate: i.dueDate, retire: false });
  const [rows, setRows] = useState<Record<string, RowEdit>>(() => Object.fromEntries(active.map((i) => [i.id, initial(i)])));
  const [adds, setAdds] = useState<{ dueDate: string; amount: string }[]>([]);
  const [reason, setReason] = useState('');
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const changes = active
    .map((i) => {
      const r = rows[i.id] ?? initial(i);
      if (r.retire) {
        return { installmentId: i.id, amount: null, dueDate: null, retire: true };
      }
      const amount = isDecimalText(r.amount) ? toTwoDecimals(r.amount) : r.amount;
      const amountChanged = amount !== i.amount;
      const dateChanged = r.dueDate !== i.dueDate;
      return amountChanged || dateChanged
        ? { installmentId: i.id, amount: amountChanged ? amount : null, dueDate: dateChanged ? r.dueDate : null, retire: null }
        : null;
    })
    .filter((c) => c !== null);
  const validAdds = adds.filter((a) => a.dueDate && a.amount.trim());
  const invalid =
    active.some((i) => !(rows[i.id]?.retire ?? false) && !isDecimalText(rows[i.id]?.amount ?? i.amount)) || validAdds.some((a) => !isDecimalText(a.amount));
  const hasWork = changes.length > 0 || validAdds.length > 0;

  const setRow = (id: string, patch: Partial<RowEdit>) => setRows({ ...rows, [id]: { ...(rows[id] as RowEdit), ...patch } });

  async function submit() {
    setBusy(true);
    setError(null);
    try {
      const next = await api.put<OrthoCaseView>(
        `/api/manager/orthodontic-cases/${view.treatmentCaseId}/installments`,
        { reason: reason.trim(), changes, adds: validAdds.map((a) => ({ dueDate: a.dueDate, amount: toTwoDecimals(a.amount) })) },
        view.version,
      );
      setAdds([]);
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

  return (
    <div className="card">
      <h2>تعديل جدول الأقساط (المدير)</h2>
      <ErrorBanner error={error} />
      <p className="hint">
        الفرق الحالي في الجدول: {view.scheduleDifference} ج.م — فرق عدد الأقساط: {view.monthPlanDifference}. يجب أن ينتهي التعديل والجدول متوازن. الأقساط
        المدفوع منها لا يمكن إلغاؤها أو تغيير موعدها أو تخفيضها عن المدفوع.
      </p>
      <table>
        <thead>
          <tr>
            <th>القسط</th>
            <th>الحالة</th>
            <th>المدفوع</th>
            <th>المبلغ</th>
            <th>تاريخ الاستحقاق</th>
            <th>إلغاء</th>
          </tr>
        </thead>
        <tbody>
          {active.map((i) => {
            const r = rows[i.id] ?? initial(i);
            const paid = isPositive(i.amountAlreadyPaid);
            return (
              <tr key={i.id}>
                <td>{i.number}</td>
                <td>{installmentStatusLabels[i.status]}</td>
                <td>{i.amountAlreadyPaid}</td>
                <td>
                  <input aria-label={`مبلغ القسط ${i.number}`} value={r.amount} disabled={r.retire} onChange={(e) => setRow(i.id, { amount: e.target.value })} inputMode="decimal" size={10} />
                </td>
                <td>
                  <input aria-label={`تاريخ القسط ${i.number}`} type="date" value={r.dueDate} disabled={paid || r.retire} onChange={(e) => setRow(i.id, { dueDate: e.target.value })} />
                </td>
                <td>
                  <input aria-label={`إلغاء القسط ${i.number}`} type="checkbox" checked={r.retire} disabled={paid} onChange={(e) => setRow(i.id, { retire: e.target.checked })} />
                </td>
              </tr>
            );
          })}
          {adds.map((a, index) => (
            <tr key={`add-${index}`}>
              <td>جديد</td>
              <td>—</td>
              <td>—</td>
              <td>
                <input aria-label="مبلغ القسط الجديد" value={a.amount} onChange={(e) => setAdds(adds.map((x, j) => (j === index ? { ...x, amount: e.target.value } : x)))} inputMode="decimal" size={10} />
              </td>
              <td>
                <input aria-label="تاريخ القسط الجديد" type="date" value={a.dueDate} onChange={(e) => setAdds(adds.map((x, j) => (j === index ? { ...x, dueDate: e.target.value } : x)))} />
              </td>
              <td>
                <button type="button" onClick={() => setAdds(adds.filter((_, j) => j !== index))}>
                  حذف السطر
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <button type="button" onClick={() => setAdds([...adds, { dueDate: '', amount: '' }])}>
        إضافة قسط
      </button>
      <label className="field">
        <span>السبب (إلزامي)</span>
        <input value={reason} onChange={(e) => setReason(e.target.value)} />
      </label>
      {invalid && <small className="field-error">يرجى إدخال مبالغ صحيحة بحد أقصى رقمين عشريين.</small>}
      {!confirming ? (
        <button type="button" disabled={!hasWork || invalid || !reason.trim()} onClick={() => setConfirming(true)}>
          حفظ الجدول
        </button>
      ) : (
        <span className="actions">
          <span>
            تأكيد حفظ {changes.length} تعديل و{validAdds.length} إضافة؟
          </span>
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
