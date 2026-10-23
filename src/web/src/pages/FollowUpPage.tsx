import { useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { followUpStatuses, followUpStatusLabels, followUpTypeLabels, formatDateTime, orNull, type FollowUp, type FollowUpStatus } from '../api/crm';
import { ErrorBanner, Field } from '../components/Form';
import { useLoad } from '../lib/useLoad';

/** Follow-Up detail: edit (If-Match) and the append-only contact-attempt history (FR-CRM-007). */
export function FollowUpPage() {
  const { id } = useParams();
  const { data: followUp, error, reload } = useLoad<FollowUp>(`/api/sales/follow-ups/${id}`);

  if (!followUp) {
    return <ErrorBanner error={error} />;
  }

  return (
    <section>
      <h1>متابعة: {followUpTypeLabels[followUp.followUpType]}</h1>
      <div className="card">
        {followUp.patient && (
          <p>
            المريض: <Link to={`/patients/${followUp.patient.id}`}>{followUp.patient.patientCode} — {followUp.patient.fullName}</Link>
          </p>
        )}
        {followUp.leadId && (
          <p>
            <Link to={`/leads/${followUp.leadId}`}>فتح العميل المحتمل</Link>
          </p>
        )}
        <p>المسؤول: {followUp.assignedSalesEmployee?.name ?? 'غير مسندة'}</p>
      </div>
      <EditFollowUp key={followUp.version} followUp={followUp} onSaved={() => void reload()} />
      <Attempts followUp={followUp} onAdded={() => void reload()} />
    </section>
  );
}

function EditFollowUp({ followUp, onSaved }: { followUp: FollowUp; onSaved: () => void }) {
  const [form, setForm] = useState({
    dueDate: followUp.dueDate,
    status: followUp.status as FollowUpStatus,
    notes: followUp.notes ?? '',
    result: followUp.result ?? '',
    nextFollowUpDate: followUp.nextFollowUpDate ?? '',
  });
  const [error, setError] = useState<unknown>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await api.put(`/api/sales/follow-ups/${followUp.id}`, {
        dueDate: form.dueDate,
        status: form.status,
        notes: orNull(form.notes),
        result: orNull(form.result),
        nextFollowUpDate: orNull(form.nextFollowUpDate),
      }, followUp.version);
      onSaved();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card" onSubmit={(e) => void submit(e)}>
      <h2>تحديث المتابعة</h2>
      <ErrorBanner error={error} />
      <Field label="تاريخ الاستحقاق" field="dueDate" error={error}>
        <input type="date" value={form.dueDate} onChange={(e) => setForm((f) => ({ ...f, dueDate: e.target.value }))} required />
      </Field>
      <Field label="الحالة" field="status" error={error}>
        <select value={form.status} onChange={(e) => setForm((f) => ({ ...f, status: e.target.value as FollowUpStatus }))}>
          {followUpStatuses.map((s) => (
            <option key={s} value={s}>
              {followUpStatusLabels[s]}
            </option>
          ))}
        </select>
      </Field>
      <Field label="ملاحظات" field="notes" error={error}>
        <textarea value={form.notes} onChange={(e) => setForm((f) => ({ ...f, notes: e.target.value }))} />
      </Field>
      <Field label="النتيجة" field="result" error={error}>
        <textarea value={form.result} onChange={(e) => setForm((f) => ({ ...f, result: e.target.value }))} />
      </Field>
      <Field label="موعد المتابعة التالي" field="nextFollowUpDate" error={error}>
        <input type="date" value={form.nextFollowUpDate} onChange={(e) => setForm((f) => ({ ...f, nextFollowUpDate: e.target.value }))} />
      </Field>
      <button type="submit">حفظ</button>
    </form>
  );
}

function Attempts({ followUp, onAdded }: { followUp: FollowUp; onAdded: () => void }) {
  const [outcome, setOutcome] = useState('');
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<unknown>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await api.post(`/api/sales/follow-ups/${followUp.id}/contact-attempts`, { outcome, notes: orNull(notes) });
      setOutcome('');
      setNotes('');
      onAdded();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <div className="card">
      <h2>محاولات التواصل</h2>
      <p className="hint">تُضاف المحاولات فقط ولا يمكن تعديلها أو حذفها.</p>
      <table>
        <thead>
          <tr>
            <th>الوقت</th>
            <th>النتيجة</th>
            <th>ملاحظات</th>
          </tr>
        </thead>
        <tbody>
          {(followUp.attempts ?? []).map((a) => (
            <tr key={a.id}>
              <td>{formatDateTime(a.attemptedAt)}</td>
              <td>{a.outcome}</td>
              <td>{a.notes ?? '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <form onSubmit={(e) => void submit(e)}>
        <ErrorBanner error={error} />
        <Field label="نتيجة المحاولة" field="outcome" error={error}>
          <input value={outcome} onChange={(e) => setOutcome(e.target.value)} required />
        </Field>
        <Field label="ملاحظات" field="notes" error={error}>
          <textarea value={notes} onChange={(e) => setNotes(e.target.value)} />
        </Field>
        <button type="submit">إضافة محاولة</button>
      </form>
    </div>
  );
}
