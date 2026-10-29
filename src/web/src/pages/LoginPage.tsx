import { useState, type FormEvent } from 'react';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner, Field } from '../components/Form';
import { Icon } from '../components/ui/Icon';

/**
 * Local-development helper only: `import.meta.env.DEV` is false in a production build, so this block and the names below are
 * removed from the shipped bundle. It fills the demo usernames; the demo password is never in the source — a developer may put
 * it in an untracked `.env.development.local` as VITE_DEMO_PASSWORD (see docs/DEVELOPMENT_DEMO.md).
 */
const demoAccounts = [
  { userName: 'manager', label: 'المدير' },
  { userName: 'doctor.demo', label: 'الطبيب' },
  { userName: 'sales.demo', label: 'المبيعات / الاستقبال' },
];

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

  function fillDemo(name: string) {
    setUserName(name);
    setPassword(import.meta.env.VITE_DEMO_PASSWORD ?? '');
    setError(null);
  }

  return (
    <main className="login-page">
      <section className="login-hero" aria-hidden="true">
        <span className="brand-mark">
          <Icon name="tooth" size={28} />
        </span>
        <h1>نظام إدارة العيادة</h1>
        <p>المرضى والمواعيد والزيارات والمالية في مكان واحد، بصلاحيات واضحة لكل دور.</p>
      </section>
      <div className="login-panel">
        <form className="card" onSubmit={(e) => void submit(e)}>
          <h1>تسجيل الدخول</h1>
          <ErrorBanner error={error} />
          <Field label="اسم المستخدم" field="userName" error={error}>
            <input className="ltr" value={userName} onChange={(e) => setUserName(e.target.value)} autoComplete="username" required />
          </Field>
          <Field label="كلمة المرور" field="password" error={error}>
            <input className="ltr" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
          </Field>
          <button type="submit" disabled={busy}>
            {busy ? 'جارٍ الدخول…' : 'دخول'}
          </button>
          <p className="hint">نسيت كلمة المرور؟ يرجى التواصل مع مدير العيادة لتعيين كلمة مرور جديدة.</p>
          {import.meta.env.DEV && (
            <div className="demo-box">
              <strong>بيئة التطوير — حسابات تجريبية</strong>
              <div className="hint">كلمة المرور موثقة في docs/DEVELOPMENT_DEMO.md.</div>
              <div className="actions">
                {demoAccounts.map((a) => (
                  <button key={a.userName} type="button" className="secondary" onClick={() => fillDemo(a.userName)}>
                    {a.label}
                  </button>
                ))}
              </div>
            </div>
          )}
        </form>
      </div>
    </main>
  );
}
