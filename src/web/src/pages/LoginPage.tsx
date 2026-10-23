import { useState, type FormEvent } from 'react';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner, Field } from '../components/Form';

export function LoginPage() {
  const { login } = useAuth();
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(userName, password);
    } catch (e) {
      setError(e);
      setPassword('');
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="login">
      <form className="card" onSubmit={(e) => void submit(e)}>
        <h1>تسجيل الدخول</h1>
        <ErrorBanner error={error} />
        <Field label="اسم المستخدم" field="userName" error={error}>
          <input value={userName} onChange={(e) => setUserName(e.target.value)} autoComplete="username" required />
        </Field>
        <Field label="كلمة المرور" field="password" error={error}>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
        </Field>
        <button type="submit" disabled={busy}>
          دخول
        </button>
        <p className="hint">نسيت كلمة المرور؟ يرجى التواصل مع مدير العيادة لتعيين كلمة مرور جديدة.</p>
      </form>
    </main>
  );
}
