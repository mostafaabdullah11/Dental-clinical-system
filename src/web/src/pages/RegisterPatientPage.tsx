import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { ManagerPatient, SalesPatient } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner, Field } from '../components/Form';

/**
 * Walk-In / Direct registration (FR-PAT-001): name, date of birth OR recorded age, contact fields.
 * There is no Original Sales field and no medical field (the intake is a separate form).
 */
export function RegisterPatientPage() {
  const { me } = useAuth();
  const navigate = useNavigate();
  const [mode, setMode] = useState<'dob' | 'age'>('dob');
  const [form, setForm] = useState({ fullName: '', dateOfBirth: '', age: '', phone: '', whatsapp: '', email: '', address: '', crmNotes: '' });
  const [error, setError] = useState<unknown>(null);
  const set = (key: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [key]: value }));
  const orNull = (value: string) => (value.trim() ? value.trim() : null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    const body = {
      fullName: form.fullName,
      dateOfBirth: mode === 'dob' ? orNull(form.dateOfBirth) : null,
      ageYearsRecorded: mode === 'age' && form.age.trim() ? Number.parseInt(form.age, 10) : null,
      phone: orNull(form.phone),
      whatsapp: orNull(form.whatsapp),
      email: orNull(form.email),
      address: orNull(form.address),
      crmNotes: orNull(form.crmNotes),
    };
    try {
      const url = me?.role === 'MANAGER' ? '/api/manager/patients' : '/api/sales/patients';
      const created = await api.post<SalesPatient | ManagerPatient>(url, body);
      const id = 'patient' in created ? created.patient.id : created.id;
      navigate(`/patients/${id}`);
    } catch (e) {
      setError(e);
    }
  }

  return (
    <section>
      <h1>تسجيل مريض مباشر (Walk-In)</h1>
      <form className="card narrow" onSubmit={(e) => void submit(e)}>
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
          <input value={form.phone} onChange={(e) => set('phone')(e.target.value)} autoComplete="off" inputMode="tel" />
        </Field>
        <Field label="واتساب" field="whatsapp" error={error}>
          <input value={form.whatsapp} onChange={(e) => set('whatsapp')(e.target.value)} autoComplete="off" inputMode="tel" />
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
        <button type="submit">تسجيل</button>
      </form>
    </section>
  );
}
