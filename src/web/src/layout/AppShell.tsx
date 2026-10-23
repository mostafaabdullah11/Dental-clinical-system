import { NavLink, Outlet } from 'react-router-dom';
import { roleLabels } from '../api/types';
import { useAuth } from '../auth/AuthContext';

export function AppShell() {
  const { me, can, logout } = useAuth();

  return (
    <div className="shell">
      <header className="topbar">
        <strong>نظام إدارة العيادة</strong>
        <span className="who">
          {me?.displayName} — {me ? roleLabels[me.role] : ''}
        </span>
        <button type="button" className="link" onClick={() => void logout()}>
          تسجيل الخروج
        </button>
      </header>
      <div className="body">
        <nav className="sidebar" aria-label="القائمة الرئيسية">
          <NavLink to="/" end>الرئيسية</NavLink>
          {(can('Patients.Search.Clinical') || can('Patients.Search.Contact')) && <NavLink to="/patients">المرضى</NavLink>}
          {(can('Crm.Leads.ViewOwn') || can('Crm.Leads.ViewAll')) && (
            <NavLink to="/leads">{can('Crm.Leads.ViewAll') ? 'كل العملاء المحتملين' : 'عملائي المحتملون'}</NavLink>
          )}
          {can('Crm.FollowUps.Manage') && <NavLink to="/follow-ups">المتابعات</NavLink>}
          {can('Identity.Users.Manage') && <NavLink to="/users">المستخدمون</NavLink>}
          {can('Staff.Manage') && <NavLink to="/doctors">الأطباء</NavLink>}
          {can('Staff.Manage') && <NavLink to="/sales">المبيعات / الاستقبال</NavLink>}
          {can('Staff.Manage') && <NavLink to="/assistants">المساعدون</NavLink>}
          {can('Configuration.Manage') && <NavLink to="/settings">الإعدادات</NavLink>}
          <NavLink to="/change-password">تغيير كلمة المرور</NavLink>
        </nav>
        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
