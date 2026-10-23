import { useState, type FormEvent } from 'react';
import { api } from '../api/client';
import type { CommissionRevision, ManagerSalesEmployee } from '../api/types';
import { ErrorBanner, Field, StatusBadge } from '../components/Form';
import { isDecimalText, toTwoDecimals } from '../lib/decimal';
import { useLoad } from '../lib/useLoad';

export function SalesPage() {
  const { data: employees, error: loadError, reload } = useLoad<ManagerSalesEmployee[]>('/api/manager/staff/sales-employees');
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
      <h1>المبيعات / الاستقبال</h1>
      <ErrorBanner error={loadError ?? error} />
      <CreateSalesForm onCreated={() => void reload()} />
      <p className="hint">تغيير نسبة العمولة يسري على التحصيلات المستقبلية فقط ولا يعيد حساب أي تحصيل سابق.</p>
      <table>
        <thead>
          <tr>
            <th>الاسم الكامل</th>
            <th>اسم المستخدم</th>
            <th>الراتب الأساسي الشهري</th>
            <th>نسبة العمولة الحالية (%)</th>
            <th>الحالة</th>
            <th>إجراءات</th>
          </tr>
        </thead>
        <tbody>
          {employees?.map((employee) => (
            <SalesRow key={employee.id} employee={employee} run={run} />
          ))}
        </tbody>
      </table>
    </section>
  );
}

function SalesRow({ employee, run }: { employee: ManagerSalesEmployee; run: (action: () => Promise<unknown>) => Promise<void> }) {
  const [salary, setSalary] = useState(employee.baseMonthlySalary);
  const [commission, setCommission] = useState(employee.currentCommissionPercentage);
  const [reason, setReason] = useState('');
  const [history, setHistory] = useState<CommissionRevision[] | null>(null);

  return (
    <>
      <tr>
        <td>{employee.fullName}</td>
        <td>{employee.userName}</td>
        <td className="actions">
          <input className="short" value={salary} onChange={(e) => setSalary(e.target.value)} inputMode="decimal" />
          <button
            type="button"
            disabled={!isDecimalText(salary) || toTwoDecimals(salary) === employee.baseMonthlySalary}
            onClick={() => void run(() => api.put(`/api/manager/sales-employees/${employee.id}/base-salary`, { baseMonthlySalary: toTwoDecimals(salary) }))}
          >
            حفظ الراتب
          </button>
        </td>
        <td className="actions">
          <input className="short" value={commission} onChange={(e) => setCommission(e.target.value)} inputMode="decimal" />
          <input placeholder="السبب (اختياري)" value={reason} onChange={(e) => setReason(e.target.value)} />
          <button
            type="button"
            disabled={!isDecimalText(commission) || toTwoDecimals(commission) === employee.currentCommissionPercentage}
            onClick={() =>
              void run(async () => {
                await api.put(`/api/manager/sales-employees/${employee.id}/commission`, {
                  commissionPercentage: toTwoDecimals(commission),
                  reason: reason || null,
                });
                setReason('');
              })
            }
          >
            حفظ العمولة
          </button>
          <button
            type="button"
            className="link"
            onClick={() => void run(async () => setHistory(await api.get<CommissionRevision[]>(`/api/manager/sales-employees/${employee.id}/commission-history`)))}
          >
            سجل العمولة
          </button>
        </td>
        <td>
          <StatusBadge active={employee.isActive} />
        </td>
        <td>
          {employee.isActive ? (
            <button type="button" onClick={() => void run(() => api.post(`/api/manager/staff/${employee.id}/deactivate`))}>
              إيقاف
            </button>
          ) : (
            <button type="button" onClick={() => void run(() => api.post(`/api/manager/staff/${employee.id}/reactivate`))}>
              إعادة تفعيل
            </button>
          )}
        </td>
      </tr>
      {history && (
        <tr className="detail">
          <td colSpan={6}>
            <ul>
              {history.map((h) => (
                <li key={h.revisionNumber}>
                  المراجعة {h.revisionNumber}: {h.commissionPercentage}% — سارية من {new Date(h.effectiveFrom).toLocaleString('ar-EG')}
                  {h.reason ? ` — ${h.reason}` : ''}
                </li>
              ))}
            </ul>
          </td>
        </tr>
      )}
    </>
  );
}

function CreateSalesForm({ onCreated }: { onCreated: () => void }) {
  const empty = { fullName: '', baseMonthlySalary: '', commissionPercentage: '', userName: '', initialPassword: '' };
  const [form, setForm] = useState(empty);
  const [error, setError] = useState<unknown>(null);
  const set = (key: keyof typeof form) => (value: string) => setForm((f) => ({ ...f, [key]: value }));
  const decimalOrNull = (value: string) => (isDecimalText(value) ? toTwoDecimals(value) : null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await api.post('/api/manager/staff/sales-employees', {
        fullName: form.fullName,
        baseMonthlySalary: decimalOrNull(form.baseMonthlySalary),
        commissionPercentage: decimalOrNull(form.commissionPercentage),
        userName: form.userName,
        initialPassword: form.initialPassword,
      });
      setForm(empty);
      onCreated();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card inline" onSubmit={(e) => void submit(e)}>
      <h2>إضافة موظف مبيعات / استقبال (مع حساب دخول)</h2>
      <ErrorBanner error={error} />
      <Field label="الاسم الكامل" field="fullName" error={error}>
        <input value={form.fullName} onChange={(e) => set('fullName')(e.target.value)} required />
      </Field>
      <Field label="الراتب الأساسي الشهري (ج.م)" field="baseMonthlySalary" error={error}>
        <input value={form.baseMonthlySalary} onChange={(e) => set('baseMonthlySalary')(e.target.value)} inputMode="decimal" required />
      </Field>
      <Field label="نسبة العمولة (%) — مطلوبة" field="commissionPercentage" error={error}>
        <input value={form.commissionPercentage} onChange={(e) => set('commissionPercentage')(e.target.value)} inputMode="decimal" required />
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
