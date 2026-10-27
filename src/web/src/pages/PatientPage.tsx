import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { useParams } from 'react-router-dom';
import { api } from '../api/client';
import {
  sourceLabels,
  type DoctorPatient,
  type ManagerDoctor,
  type ManagerPatient,
  type PatientAssignment,
  type SalesPatient,
} from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner, Field } from '../components/Form';
import { MedicalIntakeEditor, MedicalIntakeReadOnly } from '../components/MedicalIntake';
import { DoctorClinicalSection, SalesPatientSection } from '../components/PatientTask3';
import { PatientFinance } from '../components/PatientFinance';
import { PatientTimeline } from '../components/PatientTimeline';
import { formatAge } from '../lib/patients';

export function PatientPage() {
  const { id = '' } = useParams();
  const { can } = useAuth();
  // Audience is chosen by permission: Sales / Manager hold contact keys; Doctors hold only clinical keys.
  return can('Patients.Contact.Read') ? <ContactAudiencePatient id={id} /> : <DoctorPatientView id={id} />;
}

/** Doctor profile (FR-PAT-007): no contact data exists in this DTO; the intake is read-only. */
function DoctorPatientView({ id }: { id: string }) {
  const [patient, setPatient] = useState<DoctorPatient | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    api.get<DoctorPatient>(`/api/doctor/patients/${id}`).then(setPatient, setError);
  }, [id]);

  if (!patient) {
    return <ErrorBanner error={error} />;
  }

  return (
    <section>
      <h1>
        {patient.fullName} — {patient.patientCode}
      </h1>
      <div className="card narrow">
        <p>العمر: {formatAge(patient.ageDisplay)}</p>
        {patient.dateOfBirth && <p>تاريخ الميلاد: {patient.dateOfBirth}</p>}
        <p>الطبيب الحالي: {patient.currentDoctor?.name ?? '—'}</p>
        <p>الشكوى الرئيسية: {patient.chiefComplaint ?? '—'}</p>
        <p className="hint">تعديل الشكوى الرئيسية يتطلب زيارة مفتوحة للطبيب.</p>
      </div>
      <MedicalIntakeReadOnly patientId={id} />
      <DoctorClinicalSection patientId={id} />
      <PatientTimeline url={`/api/doctor/patients/${id}/timeline`} />
    </section>
  );
}

/** Sales / Manager view (FR-PAT-006): contact data, identity / contact edits, intake, and Manager reassignment. */
function ContactAudiencePatient({ id }: { id: string }) {
  const { me, can } = useAuth();
  const isManager = me?.role === 'MANAGER';
  const [patient, setPatient] = useState<SalesPatient | null>(null);
  const [chiefComplaint, setChiefComplaint] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);

  const load = useCallback(async () => {
    try {
      if (isManager) {
        const full = await api.get<ManagerPatient>(`/api/manager/patients/${id}`);
        setPatient(full.patient);
        setChiefComplaint(full.chiefComplaint);
      } else {
        setPatient(await api.get<SalesPatient>(`/api/sales/patients/${id}`));
      }
    } catch (e) {
      setError(e);
    }
  }, [id, isManager]);

  useEffect(() => {
    void load();
  }, [load]);

  if (!patient) {
    return <ErrorBanner error={error} />;
  }

  return (
    <section>
      <h1>
        {patient.fullName} — {patient.patientCode}
      </h1>
      <div className="card narrow">
        <p>العمر: {formatAge(patient.ageDisplay)}</p>
        <p>المصدر: {sourceLabels[patient.patientSource]}</p>
        {patient.originalSalesEmployee && <p>موظف المبيعات الأصلي: {patient.originalSalesEmployee.name}</p>}
        <p>الطبيب الحالي: {patient.currentDoctor?.name ?? 'لم يُعيَّن بعد'}</p>
        <p>تاريخ التسجيل: {new Date(patient.registeredAt).toLocaleString('ar-EG')}</p>
        <p>رصيد المريض الدائن: {patient.patientCreditBalance ?? '—'} ج.م</p>
        {isManager && <p>الشكوى الرئيسية: {chiefComplaint ?? '—'}</p>}
      </div>
      {can('Patients.Identity.Write') && <IdentityForm patient={patient} onSaved={setPatient} />}
      {can('Patients.Contact.Write') && <ContactForm patient={patient} onSaved={setPatient} />}
      {can('Patients.MedicalHistory.EditIntake') && <MedicalIntakeEditor patientId={id} />}
      {can('Patients.Reassign') && <AssignmentPanel patientId={id} onChanged={() => void load()} />}
      <SalesPatientSection patientId={id} />
      <PatientFinance patientId={id} />
      {isManager && <DoctorClinicalSection patientId={id} />}
      <PatientTimeline url={isManager ? `/api/manager/patients/${id}/timeline` : `/api/sales/patients/${id}/timeline`} />
    </section>
  );
}

function IdentityForm({ patient, onSaved }: { patient: SalesPatient; onSaved: (p: SalesPatient) => void }) {
  const [fullName, setFullName] = useState(patient.fullName);
  const [dateOfBirth, setDob] = useState(patient.dateOfBirth ?? '');
  const [age, setAge] = useState(patient.ageYearsRecorded?.toString() ?? '');
  const [error, setError] = useState<unknown>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      onSaved(await api.put<SalesPatient>(`/api/sales/patients/${patient.id}/identity`, {
        fullName,
        dateOfBirth: dateOfBirth || null,
        ageYearsRecorded: dateOfBirth ? null : age ? Number.parseInt(age, 10) : null,
      }, patient.identityVersion));
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card narrow" onSubmit={(e) => void submit(e)}>
      <h2>بيانات الهوية</h2>
      <ErrorBanner error={error} />
      <Field label="الاسم الكامل" field="fullName" error={error}>
        <input value={fullName} onChange={(e) => setFullName(e.target.value)} required />
      </Field>
      <Field label="تاريخ الميلاد" field="dateOfBirth" error={error}>
        <input type="date" value={dateOfBirth} onChange={(e) => setDob(e.target.value)} />
      </Field>
      <Field label="أو العمر المسجل (عند عدم معرفة تاريخ الميلاد)" field="ageYearsRecorded" error={error}>
        <input inputMode="numeric" value={age} onChange={(e) => setAge(e.target.value)} disabled={!!dateOfBirth} />
      </Field>
      <button type="submit">حفظ الهوية</button>
    </form>
  );
}

function ContactForm({ patient, onSaved }: { patient: SalesPatient; onSaved: (p: SalesPatient) => void }) {
  const [form, setForm] = useState({
    phone: patient.phone ?? '',
    whatsapp: patient.whatsapp ?? '',
    email: patient.email ?? '',
    address: patient.address ?? '',
    crmNotes: patient.crmNotes ?? '',
  });
  const [error, setError] = useState<unknown>(null);
  const set = (key: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [key]: value }));
  const orNull = (v: string) => (v.trim() ? v.trim() : null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      onSaved(await api.put<SalesPatient>(`/api/sales/patients/${patient.id}/contact`, {
        phone: orNull(form.phone),
        whatsapp: orNull(form.whatsapp),
        email: orNull(form.email),
        address: orNull(form.address),
        crmNotes: orNull(form.crmNotes),
      }, patient.contactVersion));
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card narrow" onSubmit={(e) => void submit(e)}>
      <h2>بيانات التواصل</h2>
      <ErrorBanner error={error} />
      <Field label="الهاتف" field="phone" error={error}>
        <input value={form.phone} onChange={(e) => set('phone')(e.target.value)} autoComplete="off" />
      </Field>
      <Field label="واتساب" field="whatsapp" error={error}>
        <input value={form.whatsapp} onChange={(e) => set('whatsapp')(e.target.value)} autoComplete="off" />
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
      <button type="submit">حفظ بيانات التواصل</button>
    </form>
  );
}

/** Manager-only reassignment with history (FR-PAT-009). Moves no financial ownership. */
function AssignmentPanel({ patientId, onChanged }: { patientId: string; onChanged: () => void }) {
  const [rows, setRows] = useState<PatientAssignment[]>([]);
  const [etag, setEtag] = useState<string | null>(null);
  const [doctors, setDoctors] = useState<ManagerDoctor[]>([]);
  const [doctorId, setDoctorId] = useState('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState<unknown>(null);

  const load = useCallback(async () => {
    const result = await api.getVersioned<PatientAssignment[]>(`/api/manager/patients/${patientId}/assignments`);
    setRows(result.data);
    setEtag(result.etag);
  }, [patientId]);

  useEffect(() => {
    void load().catch(setError);
    api.get<ManagerDoctor[]>('/api/manager/staff/doctors').then(setDoctors, setError);
  }, [load]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await api.post(`/api/manager/patients/${patientId}/reassign`, { doctorId: doctorId || null, reason }, etag);
      setReason('');
      await load();
      onChanged();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <section className="card">
      <h2>الطبيب المعالج (إعادة التعيين)</h2>
      <p className="hint">إعادة التعيين لا تنقل أي ملكية مالية لعمل سابق.</p>
      <ErrorBanner error={error} />
      <form className="inline" onSubmit={(e) => void submit(e)}>
        <select value={doctorId} onChange={(e) => setDoctorId(e.target.value)} aria-label="الطبيب" required>
          <option value="">اختر الطبيب</option>
          {doctors.map((d) => (
            <option key={d.id} value={d.id}>
              {d.fullName}
            </option>
          ))}
        </select>
        <input placeholder="السبب (مطلوب)" value={reason} onChange={(e) => setReason(e.target.value)} required />
        <button type="submit">إعادة التعيين</button>
      </form>
      <table>
        <thead>
          <tr>
            <th>الطبيب</th>
            <th>الطبيب السابق</th>
            <th>من</th>
            <th>إلى</th>
            <th>السبب</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.id}>
              <td>{r.doctor.name}</td>
              <td>{r.previousDoctor?.name ?? '—'}</td>
              <td>{new Date(r.assignedAt).toLocaleString('ar-EG')}</td>
              <td>{r.endedAt ? new Date(r.endedAt).toLocaleString('ar-EG') : 'حالي'}</td>
              <td>{r.reason}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
