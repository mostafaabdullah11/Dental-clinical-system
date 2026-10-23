import { useState, type FormEvent } from 'react';
import { api } from '../api/client';
import type { ManagerAssistant } from '../api/types';
import { ErrorBanner, Field, StatusBadge } from '../components/Form';
import { isDecimalText, toTwoDecimals } from '../lib/decimal';
import { useLoad } from '../lib/useLoad';

// Assistants are staff records only: no account, no login, no role.
export function AssistantsPage() {
  const { data: assistants, error: loadError, reload } = useLoad<ManagerAssistant[]>('/api/manager/staff/assistants');
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
      <h1>المساعدون</h1>
      <p className="hint">المساعد سجل موظف فقط، بدون حساب دخول أو صلاحيات في النظام.</p>
      <ErrorBanner error={loadError ?? error} />
      <CreateAssistantForm onCreated={() => void reload()} />
      <table>
        <thead>
          <tr>
            <th>الاسم الكامل</th>
            <th>الراتب الحالي</th>
            <th>الحالة</th>
            <th>إجراءات</th>
          </tr>
        </thead>
        <tbody>
          {assistants?.map((assistant) => (
            <AssistantRow key={assistant.id} assistant={assistant} run={run} />
          ))}
        </tbody>
      </table>
    </section>
  );
}

function AssistantRow({ assistant, run }: { assistant: ManagerAssistant; run: (action: () => Promise<unknown>) => Promise<void> }) {
  const [fullName, setFullName] = useState(assistant.fullName);
  const url = `/api/manager/staff/assistants/${assistant.id}`;

  return (
    <tr>
      <td className="actions">
        <input value={fullName} onChange={(e) => setFullName(e.target.value)} />
        <button
          type="button"
          disabled={!fullName.trim() || fullName === assistant.fullName}
          onClick={() =>
            void run(async () => {
              // Optimistic concurrency: read the current version, then update with If-Match.
              const current = await api.getVersioned<ManagerAssistant>(url);
              await api.put(url, { fullName }, current.etag);
            })
          }
        >
          حفظ الاسم
        </button>
      </td>
      <td>{assistant.currentSalary}</td>
      <td>
        <StatusBadge active={assistant.isActive} />
      </td>
      <td>
        {assistant.isActive ? (
          <button type="button" onClick={() => void run(() => api.post(`${url}/deactivate`))}>
            إيقاف
          </button>
        ) : (
          <button type="button" onClick={() => void run(() => api.post(`${url}/reactivate`))}>
            إعادة تفعيل
          </button>
        )}
      </td>
    </tr>
  );
}

function CreateAssistantForm({ onCreated }: { onCreated: () => void }) {
  const [fullName, setFullName] = useState('');
  const [currentSalary, setSalary] = useState('');
  const [error, setError] = useState<unknown>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await api.post('/api/manager/staff/assistants', {
        fullName,
        currentSalary: isDecimalText(currentSalary) ? toTwoDecimals(currentSalary) : null,
      });
      setFullName('');
      setSalary('');
      onCreated();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card inline" onSubmit={(e) => void submit(e)}>
      <h2>إضافة مساعد</h2>
      <ErrorBanner error={error} />
      <Field label="الاسم الكامل" field="fullName" error={error}>
        <input value={fullName} onChange={(e) => setFullName(e.target.value)} required />
      </Field>
      <Field label="الراتب الحالي (ج.م)" field="currentSalary" error={error}>
        <input value={currentSalary} onChange={(e) => setSalary(e.target.value)} inputMode="decimal" required />
      </Field>
      <button type="submit">إضافة</button>
    </form>
  );
}
