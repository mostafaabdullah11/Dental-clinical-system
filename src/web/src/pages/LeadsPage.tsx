import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { leadStatuses, leadStatusLabels, orNull, type Lead, type LeadStatus } from '../api/crm';
import type { PagedResult } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner, Field } from '../components/Form';
import { SalesEmployeePicker } from '../components/SalesEmployeePicker';
import { useLoad } from '../lib/useLoad';

/**
 * Sales: My Leads (FR-CRM-002) — the server scopes to the signed-in employee. Manager: All Leads with the
 * Unassigned filter and reassignment (FR-CRM-003 / FR-CRM-009). There is no separate "unassigned" screen route.
 */
export function LeadsPage() {
  const { me } = useAuth();
  const isManager = me?.role === 'MANAGER';
  const [unassigned, setUnassigned] = useState(false);
  const [openOnly, setOpenOnly] = useState(false);
  const query = `pageSize=100${openOnly ? '&open=true' : ''}`;
  const url = isManager ? `/api/manager/leads?${query}${unassigned ? '&unassigned=true' : ''}` : `/api/sales/leads/my?${query}`;
  const { data, error, reload } = useLoad<PagedResult<Lead>>(url);

  return (
    <section>
      <h1>{isManager ? 'كل العملاء المحتملين' : 'عملائي المحتملون'}</h1>
      <CreateLeadForm isManager={isManager} onCreated={() => void reload()} />
      <div className="card inline">
        {isManager && (
          <label className="checkbox">
            <input type="checkbox" checked={unassigned} onChange={(e) => setUnassigned(e.target.checked)} /> غير المسندين فقط
          </label>
        )}
        <label className="checkbox">
          <input type="checkbox" checked={openOnly} onChange={(e) => setOpenOnly(e.target.checked)} /> المفتوحون فقط
        </label>
      </div>
      <ErrorBanner error={error} />
      <p className="hint">العدد: {data?.totalCount ?? 0}</p>
      <table>
        <thead>
          <tr>
            <th>الاسم</th>
            <th>الهاتف</th>
            <th>الخدمة المطلوبة</th>
            <th>الحالة</th>
            <th>موعد المتابعة التالي</th>
            {isManager && <th>المسؤول الحالي</th>}
            {isManager && <th>موظف المبيعات الأصلي</th>}
            <th></th>
          </tr>
        </thead>
        <tbody>
          {data?.items.map((lead) => (
            <tr key={lead.id}>
              <td>{lead.name}</td>
              <td>{lead.phone ?? '—'}</td>
              <td>{lead.interestedService ?? '—'}</td>
              <td>
                {leadStatusLabels[lead.status]} {lead.isOpen ? '' : '(مغلق)'}
              </td>
              <td>{lead.nextFollowUpDate ?? '—'}</td>
              {isManager && <td>{lead.responsibleSalesEmployee?.name ?? 'غير مسند'}</td>}
              {isManager && <td>{lead.originalSalesEmployee.name}</td>}
              <td className="actions">
                <Link to={`/leads/${lead.id}`}>فتح</Link>
                {isManager && <ReassignLead lead={lead} onDone={() => void reload()} />}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

function ReassignLead({ lead, onDone }: { lead: Lead; onDone: () => void }) {
  const [target, setTarget] = useState('');
  const [error, setError] = useState<unknown>(null);

  async function reassign() {
    setError(null);
    try {
      await api.post(`/api/manager/leads/${lead.id}/reassign`, { salesEmployeeId: target }, lead.version);
      setTarget('');
      onDone();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <>
      <SalesEmployeePicker value={target} onChange={setTarget} excludeId={lead.responsibleSalesEmployee?.id} />
      <button type="button" disabled={!target} onClick={() => void reassign()}>
        {lead.responsibleSalesEmployee ? 'إعادة الإسناد' : 'إسناد'}
      </button>
      <ErrorBanner error={error} />
    </>
  );
}

const emptyLead = { name: '', phone: '', source: '', interestedService: '', notes: '', nextFollowUpDate: '', status: 'New' as LeadStatus };

function CreateLeadForm({ isManager, onCreated }: { isManager: boolean; onCreated: () => void }) {
  const [form, setForm] = useState(emptyLead);
  const [owner, setOwner] = useState('');
  const [error, setError] = useState<unknown>(null);
  const set = (key: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [key]: value }));

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await api.post('/api/sales/leads', {
        name: form.name,
        phone: orNull(form.phone),
        source: orNull(form.source),
        interestedService: orNull(form.interestedService),
        notes: orNull(form.notes),
        nextFollowUpDate: orNull(form.nextFollowUpDate),
        status: form.status,
        // Only the Manager names the owner; a Sales employee always owns what they create.
        salesEmployeeId: isManager ? owner || null : null,
      });
      setForm(emptyLead);
      setOwner('');
      onCreated();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card" onSubmit={(e) => void submit(e)}>
      <h2>إضافة عميل محتمل</h2>
      <ErrorBanner error={error} />
      <LeadFields form={form} set={set} error={error} />
      {isManager && (
        <Field label="موظف المبيعات المسؤول (يصبح أيضاً موظف المبيعات الأصلي)" field="salesEmployeeId" error={error}>
          <SalesEmployeePicker value={owner} onChange={setOwner} />
        </Field>
      )}
      <button type="submit">إضافة</button>
    </form>
  );
}

export function LeadFields({ form, set, error }: {
  form: typeof emptyLead;
  set: (key: keyof typeof emptyLead) => (value: string) => void;
  error: unknown;
}) {
  return (
    <>
      <Field label="الاسم" field="name" error={error}>
        <input value={form.name} onChange={(e) => set('name')(e.target.value)} required />
      </Field>
      <Field label="الهاتف" field="phone" error={error}>
        <input value={form.phone} onChange={(e) => set('phone')(e.target.value)} inputMode="tel" autoComplete="off" />
      </Field>
      <Field label="المصدر" field="source" error={error}>
        <input value={form.source} onChange={(e) => set('source')(e.target.value)} />
      </Field>
      <Field label="الخدمة المطلوبة" field="interestedService" error={error}>
        <input value={form.interestedService} onChange={(e) => set('interestedService')(e.target.value)} />
      </Field>
      <Field label="ملاحظات" field="notes" error={error}>
        <textarea value={form.notes} onChange={(e) => set('notes')(e.target.value)} />
      </Field>
      <Field label="موعد المتابعة التالي" field="nextFollowUpDate" error={error}>
        <input type="date" value={form.nextFollowUpDate} onChange={(e) => set('nextFollowUpDate')(e.target.value)} />
      </Field>
      <Field label="الحالة" field="status" error={error}>
        <select value={form.status} onChange={(e) => set('status')(e.target.value)}>
          {leadStatuses.map((s) => (
            <option key={s} value={s}>
              {leadStatusLabels[s]}
            </option>
          ))}
        </select>
      </Field>
    </>
  );
}
