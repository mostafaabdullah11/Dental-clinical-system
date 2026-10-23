import { useState, type FormEvent } from 'react';
import { api } from '../api/client';
import { ErrorBanner, Field } from '../components/Form';

export function ChangePasswordPage() {
  const [currentPassword, setCurrent] = useState('');
  const [newPassword, setNew] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [done, setDone] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setDone(false);
    try {
      await api.post('/api/auth/change-password', { currentPassword, newPassword });
      setDone(true);
      setCurrent('');
      setNew('');
    } catch (e) {
      setError(e);
    }
  }

  return (
    <section>
      <h1>تغيير كلمة المرور</h1>
      <form className="card narrow" onSubmit={(e) => void submit(e)}>
        <ErrorBanner error={error} />
        {done && <div className="ok">تم تغيير كلمة المرور.</div>}
        <Field label="كلمة المرور الحالية" field="currentPassword" error={error}>
          <input type="password" value={currentPassword} onChange={(e) => setCurrent(e.target.value)} autoComplete="current-password" required />
        </Field>
        <Field label="كلمة المرور الجديدة (12 حرفاً على الأقل)" field="newPassword" error={error}>
          <input type="password" value={newPassword} onChange={(e) => setNew(e.target.value)} autoComplete="new-password" required />
        </Field>
        <button type="submit">حفظ</button>
      </form>
    </section>
  );
}
