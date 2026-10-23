import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { leadStatusLabels, orNull, type Lead, type LeadConverted, type LeadStatus } from '../api/crm';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner, Field } from '../components/Form';
import { useLoad } from '../lib/useLoad';
import { CreateFollowUpForm } from './FollowUpsPage';
import { LeadFields } from './LeadsPage';

/** Lead detail: edit (If-Match), convert to a patient (FR-CRM-004), add a Lead follow-up. */
export function LeadPage() {
  const { id } = useParams();
  const { me } = useAuth();
  const { data: lead, error: loadError, reload } = useLoad<Lead>(`/api/sales/leads/${id}`);

  if (!lead) {
    return <ErrorBanner error={loadError} />;
  }

  return (
    <section>
      <h1>العميل المحتمل: {lead.name}</h1>
      <div className="card">
        <p>
          الحالة: {leadStatusLabels[lead.status]} {lead.isOpen ? '(مفتوح)' : '(مغلق)'}
        </p>
        <p>موظف المبيعات الأصلي: {lead.originalSalesEmployee.name} (ثابت لا يتغير)</p>
        <p>المسؤول الحالي: {lead.responsibleSalesEmployee?.name ?? 'غير مسند'}</p>
        {lead.convertedPatientId && (
          <p>
            تم تحويله إلى مريض: <Link to={`/patients/${lead.convertedPatientId}`}>فتح ملف المريض</Link>
          </p>
        )}
      </div>
      <EditLead key={lead.version} lead={lead} onSaved={() => void reload()} />
      {!lead.convertedPatientId && <ConvertLead lead={lead} />}
      {me?.role !== 'MANAGER' && <CreateFollowUpForm fixedLeadId={lead.id} onCreated={() => undefined} />}
    </section>
  );
}

function EditLead({ lead, onSaved }: { lead: Lead; onSaved: () => void }) {
  const [form, setForm] = useState({
    name: lead.name,
    phone: lead.phone ?? '',
    source: lead.source ?? '',
    interestedService: lead.interestedService ?? '',
    notes: lead.notes ?? '',
    nextFollowUpDate: lead.nextFollowUpDate ?? '',
    status: lead.status as LeadStatus,
  });
  const [error, setError] = useState<unknown>(null);
  const [saved, setSaved] = useState(false);
  const set = (key: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [key]: value }));

  useEffect(() => setSaved(false), [form]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      // Original Sales, responsibility and conversion are never part of an edit.
      await api.put(`/api/sales/leads/${lead.id}`, {
        name: form.name,
        phone: orNull(form.phone),
        source: orNull(form.source),
        interestedService: orNull(form.interestedService),
        notes: orNull(form.notes),
        nextFollowUpDate: orNull(form.nextFollowUpDate),
        status: form.status,
      }, lead.version);
      setSaved(true);
      onSaved();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card" onSubmit={(e) => void submit(e)}>
      <h2>تعديل البيانات</h2>
      <ErrorBanner error={error} />
      {saved && <div className="ok">تم الحفظ.</div>}
      <LeadFields form={form} set={set} error={error} />
      <button type="submit">حفظ</button>
    </form>
  );
}

/**
 * Creates the Lead-sourced patient. The Original Sales Employee is taken from the Lead by the server and cannot be
 * chosen here. Medical History is a separate intake on the patient page, not part of conversion.
 */
function ConvertLead({ lead }: { lead: Lead }) {
  const navigate = useNavigate();
  const [mode, setMode] = useState<'dob' | 'age'>('dob');
  const [form, setForm] = useState({ fullName: lead.name, dateOfBirth: '', age: '', phone: lead.phone ?? '', whatsapp: '', email: '', address: '', crmNotes: '' });
  const [error, setError] = useState<unknown>(null);
  const set = (key: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [key]: value }));

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const converted = await api.post<LeadConverted>(`/api/sales/leads/${lead.id}/convert`, {
        fullName: form.fullName,
        dateOfBirth: mode === 'dob' ? orNull(form.dateOfBirth) : null,
        ageYearsRecorded: mode === 'age' && form.age.trim() ? Number.parseInt(form.age, 10) : null,
        phone: orNull(form.phone),
        whatsapp: orNull(form.whatsapp),
        email: orNull(form.email),
        address: orNull(form.address),
        crmNotes: orNull(form.crmNotes),
      }, lead.version);
      navigate(`/patients/${converted.patientId}`);
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card" onSubmit={(e) => void submit(e)}>
      <h2>تحويل إلى مريض</h2>
      <p className="hint">
        سيُنشأ ملف مريض مصدره «عميل محتمل»، ويُنسب تلقائياً إلى موظف المبيعات الأصلي ({lead.originalSalesEmployee.name}) بشكل نهائي.
        يُستكمل التاريخ الطبي لاحقاً من صفحة المريض.
      </p>
      <ErrorBanner error={error} />
      <Field label="الاسم الكامل" field="fullName" error={error}>
        <input value={form.fullName} onChange={(e) => set('fullName')(e.target.value)} required />
      </Field>
      <div className="field">
        <span>العمر</span>
        <label className="checkbox">
          <input type="radio" checked={mode === 'dob'} onChange={() => setMode('dob')} /> تاريخ الميلاد
        </label>
        <label className="checkbox">
          <input type="radio" checked={mode === 'age'} onChange={() => setMode('age')} /> العمر فقط (عند عدم معرفة تاريخ الميلاد)
        </label>
      </div>
      {mode === 'dob' ? (
        <Field label="تاريخ الميلاد" field="dateOfBirth" error={error}>
          <input type="date" value={form.dateOfBirth} onChange={(e) => set('dateOfBirth')(e.target.value)} required />
        </Field>
      ) : (
        <Field label="العمر بالسنوات (0–120)" field="ageYearsRecorded" error={error}>
          <input inputMode="numeric" value={form.age} onChange={(e) => set('age')(e.target.value)} required />
        </Field>
      )}
      <Field label="الهاتف" field="phone" error={error}>
        <input value={form.phone} onChange={(e) => set('phone')(e.target.value)} inputMode="tel" autoComplete="off" />
      </Field>
      <Field label="واتساب" field="whatsapp" error={error}>
        <input value={form.whatsapp} onChange={(e) => set('whatsapp')(e.target.value)} inputMode="tel" autoComplete="off" />
      </Field>
      <Field label="البريد الإلكتروني" field="email" error={error}>
        <input value={form.email} onChange={(e) => set('email')(e.target.value)} autoComplete="off" />
      </Field>
      <Field label="العنوان" field="address" error={error}>
        <input value={form.address} onChange={(e) => set('address')(e.target.value)} autoComplete="off" />
      </Field>
      <Field label="ملاحظات التواصل" field="crmNotes" error={error}>
        <input value={form.crmNotes} onChange={(e) => set('crmNotes')(e.target.value)} autoComplete="off" />
      </Field>
      <button type="submit">تحويل وإنشاء ملف المريض</button>
    </form>
  );
}
