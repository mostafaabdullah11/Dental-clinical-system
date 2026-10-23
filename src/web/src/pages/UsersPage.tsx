import { useState, type FormEvent } from 'react';
import { api } from '../api/client';
import { roleLabels, type ManagerUser, type Role } from '../api/types';
import { ErrorBanner, Field, StatusBadge } from '../components/Form';
import { useLoad } from '../lib/useLoad';

// Doctor and Sales / Reception accounts are created with their staff record on the Doctors and Sales pages,
// so the role ⇔ staff-record rule always holds. This page creates Manager accounts and manages all accounts.
export function UsersPage() {
  const { data: users, error: loadError, reload } = useLoad<ManagerUser[]>('/api/manager/users');
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);

  async function run(action: () => Promise<unknown>, success: string) {
    setError(null);
    setNotice(null);
    try {
      await action();
      setNotice(success);
      await reload();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <section>
      <h1>المستخدمون</h1>
      <ErrorBanner error={loadError ?? error} />
      {notice && <div className="ok">{notice}</div>}
      <CreateManagerForm onCreated={() => void reload()} />
      <table>
        <thead>
          <tr>
            <th>اسم المستخدم</th>
            <th>الدور</th>
            <th>الموظف المرتبط</th>
            <th>الحالة</th>
            <th>إجراءات</th>
          </tr>
        </thead>
        <tbody>
          {users?.map((user) => (
            <UserRow key={user.id} user={user} run={run} />
          ))}
        </tbody>
      </table>
    </section>
  );
}

function UserRow({ user, run }: { user: ManagerUser; run: (action: () => Promise<unknown>, success: string) => Promise<void> }) {
  const [newPassword, setNewPassword] = useState('');
  const [role, setRole] = useState<Role>(user.role);

  return (
    <tr>
      <td>{user.userName}</td>
      <td>{roleLabels[user.role]}</td>
      <td>{user.staffFullName ?? '—'}</td>
      <td>
        <StatusBadge active={user.isActive} />
      </td>
      <td className="actions">
        <input
          type="password"
          placeholder="كلمة مرور جديدة"
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          autoComplete="new-password"
        />
        <button
          type="button"
          disabled={!newPassword}
          onClick={() =>
            void run(async () => {
              await api.post(`/api/manager/users/${user.id}/password`, { newPassword });
              setNewPassword('');
            }, 'تم تعيين كلمة مرور جديدة وإنهاء جلسات المستخدم.')
          }
        >
          تعيين كلمة مرور جديدة
        </button>
        <select value={role} onChange={(e) => setRole(e.target.value as Role)} aria-label="الدور">
          {(Object.keys(roleLabels) as Role[]).map((r) => (
            <option key={r} value={r}>
              {roleLabels[r]}
            </option>
          ))}
        </select>
        <button
          type="button"
          disabled={role === user.role}
          onClick={() => void run(() => api.put(`/api/manager/users/${user.id}/role`, { role }), 'تم تغيير الدور.')}
        >
          تغيير الدور
        </button>
        <button
          type="button"
          onClick={() => void run(() => api.post(`/api/manager/users/${user.id}/force-sign-out`), 'تم إنهاء جميع جلسات المستخدم.')}
        >
          إنهاء الجلسات
        </button>
      </td>
    </tr>
  );
}

function CreateManagerForm({ onCreated }: { onCreated: () => void }) {
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<unknown>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await api.post('/api/manager/users', { userName, password, role: 'MANAGER' });
      setUserName('');
      setPassword('');
      onCreated();
    } catch (e) {
      setError(e);
    }
  }

  return (
    <form className="card inline" onSubmit={(e) => void submit(e)}>
      <h2>إنشاء حساب مدير</h2>
      <ErrorBanner error={error} />
      <Field label="اسم المستخدم" field="userName" error={error}>
        <input value={userName} onChange={(e) => setUserName(e.target.value)} autoComplete="off" required />
      </Field>
      <Field label="كلمة المرور الأولية" field="password" error={error}>
        <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" required />
      </Field>
      <button type="submit">إنشاء</button>
    </form>
  );
}
