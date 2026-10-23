import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import {
  followUpStatuses, followUpStatusLabels, followUpTypeLabels, followUpTypes, orNull,
  type FollowUp, type FollowUpStatus, type FollowUpType,
} from '../api/crm';
import type { PagedResult, PatientListItem } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner, Field } from '../components/Form';
import { SalesEmployeePicker } from '../components/SalesEmployeePicker';
import { useLoad } from '../lib/useLoad';

/**
 * Sales: own Follow-Ups only (FR-CRM-008). Manager: all Follow-Ups with the Unassigned filter and assignment
 * (FR-CRM-009). Open = anything but «تمت المتابعة».
 */
export function FollowUpsPage() {
  const { me } = useAuth();
  const isManager = me?.role === 'MANAGER';
  const [unassigned, setUnassigned] = useState(false);
  const [openOnly, setOpenOnly] = useState(true);
  const query = `pageSize=100${openOnly ? '&open=true' : ''}`;
  const url = isManager ? `/api/manager/follow-ups?${query}${unassigned ? '&unassigned=true' : ''}` : `/api/sales/follow-ups?${query}`;
  const { data, error, reload } = useLoad<PagedResult<FollowUp>>(url);

  return (
    <section>
      <h1>{isManager ? 'كل المتابعات' : 'متابعاتي'}</h1>
      <CreateFollowUpForm onCreated={() => void reload()} />
      <div className="card inline">
        {isManager && (
          <label className="checkbox">
            <input type="checkbox" checked={unassigned} onChange={(e) => setUnassigned(e.target.checked)} /> غير المسندة فقط
          </label>
        )}
        <label className="checkbox">
          <input type="checkbox" checked={openOnly} onChange={(e) => setOpenOnly(e.target.checked)} /> المفتوحة فقط
        </label>
      </div>
      <ErrorBanner error={error} />
      <p className="hint">العدد: {data?.totalCount ?? 0}</p>
      <table>
        <thead>
          <tr>
            <th>النوع</th>
            <th>المريض / العميل المحتمل</th>
            <th>تاريخ الاستحقاق</th>
            <th>الحالة</th>
            {isManager && <th>المسؤول</th>}
            <th></th>
          </tr>
        </thead>
        <tbody>
          {data?.items.map((f) => (
            <tr key={f.id}>
              <td>{followUpTypeLabels[f.followUpType]}</td>
              <td>{f.patient ? `${f.patient.patientCode} — ${f.patient.fullName}` : f.leadId ? <Link to={`/leads/${f.leadId}`}>عميل محتمل</Link> : '—'}</td>
              <td>{f.dueDate}</td>
              <td>{followUpStatusLabels[f.status]}</td>
              {isManager && <td>{f.assignedSalesEmployee?.name ?? 'غير مسندة'}</td>}
              <td className="actions">
                <Link to={`/follow-ups/${f.id}`}>فتح</Link>
                {isManager && <AssignFollowUp followUp={f} onDone={() => void reload()} />}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

function AssignFollowUp({ followUp, onDone }: { followUp: FollowUp; onDone: () => void }) {
  const [target, setTarget] = useState('');
  const [error, setError] = useState<unknown>(null);

  async function assign() {
    setError(null);
    try {
      await api.post(`/api/manager/follow-ups/${followUp.id}/assign`, { salesEmployeeId: target }, followUp.version);
      setTarget('');
      onDone();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <>
      <SalesEmployeePicker value={target} onChange={setTarget} excludeId={followUp.assignedSalesEmployee?.id} />
      <button type="button" disabled={!target} onClick={() => void assign()}>
        {followUp.assignedSalesEmployee ? 'إعادة الإسناد' : 'إسناد'}
      </button>
      <ErrorBanner error={error} />
    </>
  );
}

/** Manual creation only: no follow-up is ever created automatically. A Lead follow-up references the Lead. */
export function CreateFollowUpForm({ fixedLeadId, onCreated }: { fixedLeadId?: string; onCreated: () => void }) {
  const { me } = useAuth();
  const isManager = me?.role === 'MANAGER';
  const [type, setType] = useState<FollowUpType>(fixedLeadId ? 'Lead' : 'GeneralTreatmentFollowUp');
  const [patient, setPatient] = useState<PatientListItem | null>(null);
  const [query, setQuery] = useState('');
  const [matches, setMatches] = useState<PatientListItem[]>([]);
  const [form, setForm] = useState({ dueDate: '', status: 'New' as FollowUpStatus, notes: '' });
  const [owner, setOwner] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [done, setDone] = useState(false);

  async function search() {
    setError(null);
    try {
      const url = isManager ? '/api/manager/patients/search' : '/api/sales/patients/search';
      setMatches((await api.post<PagedResult<PatientListItem>>(url, { query, page: 1, pageSize: 10 })).items);
    } catch (e) {
      setError(e);
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setDone(false);
    try {
      await api.post('/api/sales/follow-ups', {
        followUpType: type,
        patientId: patient?.id ?? null,
        leadId: fixedLeadId ?? null,
        dueDate: orNull(form.dueDate),
        status: form.status,
        notes: orNull(form.notes),
        result: null,
        nextFollowUpDate: null,
        salesEmployeeId: isManager ? owner || null : null,
      });
      setForm({ dueDate: '', status: 'New', notes: '' });
      setPatient(null);
      setDone(true);
      onCreated();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card" onSubmit={(e) => void submit(e)}>
      <h2>{fixedLeadId ? 'إضافة متابعة لهذا العميل المحتمل' : 'إضافة متابعة'}</h2>
      <ErrorBanner error={error} />
      {done && <div className="ok">تمت إضافة المتابعة.</div>}
      <Field label="النوع" field="followUpType" error={error}>
        <select value={type} onChange={(e) => setType(e.target.value as FollowUpType)}>
          {followUpTypes.map((t) => (
            <option key={t} value={t}>
              {followUpTypeLabels[t]}
            </option>
          ))}
        </select>
      </Field>
      {!fixedLeadId && (
        <Field label="المريض" field="patientId" error={error}>
          {patient ? (
            <span>
              {patient.patientCode} — {patient.fullName}{' '}
              <button type="button" className="link" onClick={() => setPatient(null)}>
                تغيير
              </button>
            </span>
          ) : (
            <span className="actions">
              <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="كود المريض أو الاسم أو الهاتف" autoComplete="off" />
              <button type="button" disabled={!query.trim()} onClick={() => void search()}>
                بحث
              </button>
              {matches.map((m) => (
                <button key={m.id} type="button" className="link" onClick={() => { setPatient(m); setMatches([]); }}>
                  {m.patientCode} — {m.fullName}
                </button>
              ))}
            </span>
          )}
        </Field>
      )}
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
      {isManager && (
        <Field label="موظف المبيعات المسؤول" field="salesEmployeeId" error={error}>
          <SalesEmployeePicker value={owner} onChange={setOwner} />
        </Field>
      )}
      <button type="submit">إضافة</button>
    </form>
  );
}
