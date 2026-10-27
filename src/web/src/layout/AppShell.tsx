import { NavLink, Outlet } from 'react-router-dom';
import type { UnreadCount } from '../api/types';
import { useLoad } from '../lib/useLoad';
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
          {can('Scheduling.Queue.ReadOwn') && <NavLink to="/queue">قائمة الانتظار</NavLink>}
          {can('Scheduling.Availability.ViewOwn') && me?.role === 'DOCTOR' && <NavLink to="/my-schedule">جدولي</NavLink>}
          {can('Scheduling.Appointments.View.All') && <NavLink to="/appointments">المواعيد</NavLink>}
          {can('Scheduling.Availability.Publish') && <NavLink to="/availability">إتاحة الأطباء</NavLink>}
          {can('Crm.Referrals.Read.Sales') && <NavLink to="/referrals">تحويلات الأطباء</NavLink>}
          {can('Feedback.Queue.Manage') && <NavLink to="/feedback">طلبات التقييم</NavLink>}
          {can('Feedback.Results.Read') && <NavLink to="/feedback-results">نتائج التقييمات</NavLink>}
          {can('Notifications.Read.Own') && <NotificationsLink />}
          {(can('Crm.Leads.ViewOwn') || can('Crm.Leads.ViewAll')) && (
            <NavLink to="/leads">{can('Crm.Leads.ViewAll') ? 'كل العملاء المحتملين' : 'عملائي المحتملون'}</NavLink>
          )}
          {can('Crm.FollowUps.Manage') && <NavLink to="/follow-ups">المتابعات</NavLink>}
          {can('Identity.Users.Manage') && <NavLink to="/users">المستخدمون</NavLink>}
          {can('Staff.Manage') && <NavLink to="/doctors">الأطباء</NavLink>}
          {can('Staff.Manage') && <NavLink to="/sales">المبيعات / الاستقبال</NavLink>}
          {can('Staff.Manage') && <NavLink to="/assistants">المساعدون</NavLink>}
          {can('Finance.OwnWork.Read') && <NavLink to="/my-work">أعمالي المالية</NavLink>}
          {(can('Payroll.Doctor.ReadOwn') || can('Payroll.Sales.ReadOwn')) && <NavLink to="/my-payroll">راتبي</NavLink>}
          {can('Finance.PatientBalance.Read') && me?.role !== 'MANAGER' && <NavLink to="/outstanding">الأرصدة المتبقية</NavLink>}
          {can('Expenses.CreateOperational') && me?.role !== 'MANAGER' && <NavLink to="/expenses/new">تسجيل مصروف</NavLink>}
          {can('Finance.Management.Read') && <NavLink to="/finance">المالية</NavLink>}
          {can('Payroll.Doctor.ManageAll') && <NavLink to="/payroll">الرواتب</NavLink>}
          {can('Expenses.ReadAll') && <NavLink to="/expenses">المصروفات</NavLink>}
          {can('Staff.Attendance.Manage') && <NavLink to="/attendance">الحضور والوقت الإضافي</NavLink>}
          {can('Reports.Financial') && <NavLink to="/reports">التقارير</NavLink>}
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

/** Inbox link with the unread count (Sales / Manager only; Doctors have no inbox in V1). */
function NotificationsLink() {
  const { data } = useLoad<UnreadCount>('/api/me/notifications/unread-count');
  return (
    <NavLink to="/notifications">
      الإشعارات{data && data.count > 0 ? ` (${data.count})` : ''}
    </NavLink>
  );
}
