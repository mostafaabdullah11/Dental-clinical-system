import { roleLabels } from '../api/types';
import { useAuth } from '../auth/AuthContext';

export function HomePage() {
  const { me } = useAuth();
  if (!me) {
    return null;
  }

  return (
    <section>
      <h1>مرحباً، {me.displayName}</h1>
      <p>الدور: {roleLabels[me.role]}</p>
      {me.role === 'MANAGER' ? (
        <p>استخدم القائمة لإدارة المستخدمين والموظفين وإعدادات العيادة.</p>
      ) : (
        <p>ستتوفر وظائفك في هذه الشاشة مع المراحل القادمة من النظام.</p>
      )}
    </section>
  );
}
