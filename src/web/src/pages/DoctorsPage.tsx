import { useState, type FormEvent } from 'react';
import { api } from '../api/client';
import type { ManagerDoctor } from '../api/types';
import { ErrorBanner, Field, StatusBadge } from '../components/Form';
import { isDecimalText, toTwoDecimals } from '../lib/decimal';
import { useLoad } from '../lib/useLoad';

export function DoctorsPage() {
  const { data: doctors, error: loadError, reload } = useLoad<ManagerDoctor[]>('/api/manager/staff/doctors');
  const [error, setError] = useState<unknown>(null);

  async function run(action: () => Promise<unknown>) {
    setError(null);
    try {
      await action();
      await reload();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <section>
      <h1>الأطباء</h1>
      <ErrorBanner error={loadError ?? error} />
      <CreateDoctorForm onCreated={() => void reload()} />
      <table>
        <thead>
          <tr>
            <th>الاسم الكامل</th>
            <th>اسم المستخدم</th>
            <th>التخصص</th>
            <th>نسبة الطبيب (%)</th>
            <th>الحالة</th>
            <th>إجراءات</th>
          </tr>
        </thead>
        <tbody>
          {doctors?.map((doctor) => (
            <DoctorRow key={doctor.id} doctor={doctor} run={run} />
          ))}
        </tbody>
      </table>
    </section>
  );
}

function DoctorRow({ doctor, run }: { doctor: ManagerDoctor; run: (action: () => Promise<unknown>) => Promise<void> }) {
  const [percentage, setPercentage] = useState(doctor.currentPercentage);

  return (
    <tr>
      <td>{doctor.fullName}</td>
      <td>{doctor.userName}</td>
      <td>{doctor.specialty ?? '—'}</td>
      <td className="actions">
        <input className="short" value={percentage} onChange={(e) => setPercentage(e.target.value)} inputMode="decimal" />
        <button
          type="button"
          disabled={!isDecimalText(percentage) || toTwoDecimals(percentage) === doctor.currentPercentage}
          onClick={() => void run(() => api.put(`/api/manager/doctors/${doctor.id}/percentage`, { currentPercentage: toTwoDecimals(percentage) }))}
        >
          حفظ النسبة
        </button>
      </td>
      <td>
        <StatusBadge active={doctor.isActive} />
      </td>
      <td>
        {doctor.isActive ? (
          <button type="button" onClick={() => void run(() => api.post(`/api/manager/staff/${doctor.id}/deactivate`))}>
            إيقاف
          </button>
        ) : (
          <button type="button" onClick={() => void run(() => api.post(`/api/manager/staff/${doctor.id}/reactivate`))}>
            إعادة تفعيل
          </button>
        )}
      </td>
    </tr>
  );
}

function CreateDoctorForm({ onCreated }: { onCreated: () => void }) {
  const [form, setForm] = useState({ fullName: '', specialty: '', currentPercentage: '', userName: '', initialPassword: '' });
  const [error, setError] = useState<unknown>(null);
  const set = (key: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [key]: value }));

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await api.post('/api/manager/staff/doctors', {
        fullName: form.fullName,
        specialty: form.specialty || null,
        currentPercentage: isDecimalText(form.currentPercentage) ? toTwoDecimals(form.currentPercentage) : null,
        userName: form.userName,
        initialPassword: form.initialPassword,
      });
      setForm({ fullName: '', specialty: '', currentPercentage: '', userName: '', initialPassword: '' });
      onCreated();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card inline" onSubmit={(e) => void submit(e)}>
      <h2>إضافة طبيب (مع حساب دخول)</h2>
      <ErrorBanner error={error} />
      <Field label="الاسم الكامل" field="fullName" error={error}>
        <input value={form.fullName} onChange={(e) => set('fullName')(e.target.value)} required />
      </Field>
      <Field label="التخصص (اختياري)" field="specialty" error={error}>
        <input value={form.specialty} onChange={(e) => set('specialty')(e.target.value)} />
      </Field>
      <Field label="نسبة الطبيب (%)" field="currentPercentage" error={error}>
        <input value={form.currentPercentage} onChange={(e) => set('currentPercentage')(e.target.value)} inputMode="decimal" required />
      </Field>
      <Field label="اسم المستخدم" field="userName" error={error}>
        <input value={form.userName} onChange={(e) => set('userName')(e.target.value)} autoComplete="off" required />
      </Field>
      <Field label="كلمة المرور الأولية" field="password" error={error}>
        <input type="password" value={form.initialPassword} onChange={(e) => set('initialPassword')(e.target.value)} autoComplete="new-password" required />
      </Field>
      <button type="submit">إضافة</button>
    </form>
  );
}
