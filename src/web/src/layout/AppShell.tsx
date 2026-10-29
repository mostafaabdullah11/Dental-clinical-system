import { useEffect, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import type { UnreadCount } from '../api/types';
import { useLoad } from '../lib/useLoad';
import { roleLabels } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { Icon, type IconName } from '../components/ui/Icon';

interface NavItem {
  to: string;
  label: string;
  icon: IconName;
  show: boolean;
  end?: boolean;
  notifications?: boolean;
}

/** Page titles for the top bar (the pages themselves keep their own headings). First matching prefix wins. */
const titles: [string, string][] = [
  ['/patients/new', 'تسجيل مريض جديد'],
  ['/patients', 'المرضى'],
  ['/queue', 'قائمة الانتظار'],
  ['/my-schedule', 'جدولي'],
  ['/appointments', 'المواعيد'],
  ['/availability', 'إتاحة الأطباء'],
  ['/referrals', 'تحويلات الأطباء'],
  ['/feedback-results', 'نتائج التقييمات'],
  ['/feedback', 'طلبات التقييم'],
  ['/notifications', 'الإشعارات'],
  ['/leads', 'العملاء المحتملون'],
  ['/follow-ups', 'المتابعات'],
  ['/users', 'المستخدمون'],
  ['/doctors', 'الأطباء'],
  ['/sales', 'المبيعات / الاستقبال'],
  ['/assistants', 'المساعدون'],
  ['/my-work', 'أعمالي المالية'],
  ['/my-payroll', 'راتبي'],
  ['/outstanding', 'الأرصدة المتبقية'],
  ['/expenses/new', 'تسجيل مصروف'],
  ['/expenses', 'المصروفات'],
  ['/finance', 'المالية'],
  ['/payroll', 'الرواتب'],
  ['/attendance', 'الحضور والوقت الإضافي'],
  ['/reports', 'التقارير'],
  ['/settings', 'الإعدادات'],
  ['/change-password', 'تغيير كلمة المرور'],
  ['/visits', 'الزيارة'],
  ['/ortho', 'ملف التقويم'],
];

function pageTitle(pathname: string): string {
  if (pathname === '/') {
    return 'الرئيسية';
  }
  return titles.find(([prefix]) => pathname === prefix || pathname.startsWith(prefix + '/'))?.[1] ?? '';
}

export function AppShell() {
  const { me, can, logout } = useAuth();
  const location = useLocation();
  const [open, setOpen] = useState(false);

  useEffect(() => {
    setOpen(false);
  }, [location.pathname]);

  const groups: { heading: string; items: NavItem[] }[] = [
    {
      heading: '',
      items: [{ to: '/', label: 'الرئيسية', icon: 'home', show: true, end: true }],
    },
    {
      heading: 'العيادة',
      items: [
        { to: '/patients', label: 'المرضى', icon: 'users', show: can('Patients.Search.Clinical') || can('Patients.Search.Contact') },
        { to: '/queue', label: 'قائمة الانتظار', icon: 'queue', show: can('Scheduling.Queue.ReadOwn') },
        { to: '/my-schedule', label: 'جدولي', icon: 'calendar', show: can('Scheduling.Availability.ViewOwn') && me?.role === 'DOCTOR' },
        { to: '/appointments', label: 'المواعيد', icon: 'calendar', show: can('Scheduling.Appointments.View.All') },
        { to: '/availability', label: 'إتاحة الأطباء', icon: 'clock', show: can('Scheduling.Availability.Publish') },
        { to: '/referrals', label: 'تحويلات الأطباء', icon: 'refer', show: can('Crm.Referrals.Read.Sales') },
      ],
    },
    {
      heading: 'العملاء والتقييمات',
      items: [
        {
          to: '/leads',
          label: can('Crm.Leads.ViewAll') ? 'كل العملاء المحتملين' : 'عملائي المحتملون',
          icon: 'phone',
          show: can('Crm.Leads.ViewOwn') || can('Crm.Leads.ViewAll'),
        },
        { to: '/follow-ups', label: 'المتابعات', icon: 'clipboard', show: can('Crm.FollowUps.Manage') },
        { to: '/feedback', label: 'طلبات التقييم', icon: 'star', show: can('Feedback.Queue.Manage') },
        { to: '/feedback-results', label: 'نتائج التقييمات', icon: 'chart', show: can('Feedback.Results.Read') },
        { to: '/notifications', label: 'الإشعارات', icon: 'bell', show: can('Notifications.Read.Own'), notifications: true },
      ],
    },
    {
      heading: 'المالية',
      items: [
        { to: '/my-work', label: 'أعمالي المالية', icon: 'wallet', show: can('Finance.OwnWork.Read') },
        { to: '/my-payroll', label: 'راتبي', icon: 'money', show: can('Payroll.Doctor.ReadOwn') || can('Payroll.Sales.ReadOwn') },
        { to: '/outstanding', label: 'الأرصدة المتبقية', icon: 'wallet', show: can('Finance.PatientBalance.Read') && me?.role !== 'MANAGER' },
        { to: '/expenses/new', label: 'تسجيل مصروف', icon: 'money', show: can('Expenses.CreateOperational') && me?.role !== 'MANAGER' },
        { to: '/finance', label: 'المالية', icon: 'wallet', show: can('Finance.Management.Read') },
        { to: '/payroll', label: 'الرواتب', icon: 'money', show: can('Payroll.Doctor.ManageAll') },
        { to: '/expenses', label: 'المصروفات', icon: 'money', show: can('Expenses.ReadAll') },
        { to: '/reports', label: 'التقارير', icon: 'chart', show: can('Reports.Financial') },
      ],
    },
    {
      heading: 'الإدارة',
      items: [
        { to: '/users', label: 'المستخدمون', icon: 'key', show: can('Identity.Users.Manage') },
        { to: '/doctors', label: 'الأطباء', icon: 'tooth', show: can('Staff.Manage') },
        { to: '/sales', label: 'المبيعات / الاستقبال', icon: 'users', show: can('Staff.Manage') },
        { to: '/assistants', label: 'المساعدون', icon: 'user', show: can('Staff.Manage') },
        { to: '/attendance', label: 'الحضور والوقت الإضافي', icon: 'clock', show: can('Staff.Attendance.Manage') },
        { to: '/settings', label: 'الإعدادات', icon: 'settings', show: can('Configuration.Manage') },
      ],
    },
  ];

  return (
    <div className={`shell${open ? ' nav-open' : ''}`}>
      <nav className="sidebar" aria-label="القائمة الرئيسية">
        <div className="brand">
          <span className="brand-mark">
            <Icon name="tooth" size={22} />
          </span>
          <span>
            نظام إدارة العيادة
            <small>طب وجراحة الأسنان</small>
          </span>
        </div>
        {groups.map((group) => {
          const items = group.items.filter((i) => i.show);
          if (items.length === 0) {
            return null;
          }
          return (
            <div className="nav-group" key={group.heading || 'main'}>
              {group.heading && <div className="nav-heading">{group.heading}</div>}
              {items.map((item) =>
                item.notifications ? (
                  <NotificationsLink key={item.to} label={item.label} icon={item.icon} />
                ) : (
                  <NavLink key={item.to} to={item.to} end={item.end}>
                    <Icon name={item.icon} />
                    {item.label}
                  </NavLink>
                ),
              )}
            </div>
          );
        })}
        <div className="nav-group">
          <div className="nav-heading">الحساب</div>
          <NavLink to="/change-password">
            <Icon name="key" />
            تغيير كلمة المرور
          </NavLink>
        </div>
      </nav>
      <button type="button" className="scrim" aria-label="إغلاق القائمة" tabIndex={-1} onClick={() => setOpen(false)} />
      <header className="topbar">
        <button type="button" className="menu-toggle secondary" aria-label="فتح القائمة" aria-expanded={open} onClick={() => setOpen((v) => !v)}>
          <Icon name="menu" />
        </button>
        <span className="page-title">{pageTitle(location.pathname)}</span>
        <div className="who">
          <span className="avatar" aria-hidden="true">
            {me?.displayName.trim().charAt(0)}
          </span>
          <span className="who-text">
            <strong>{me?.displayName}</strong>
            <span>{me ? roleLabels[me.role] : ''}</span>
          </span>
          <button type="button" className="secondary" aria-label="تسجيل الخروج" onClick={() => void logout()}>
            <Icon name="logout" size={18} /> <span className="logout-label">تسجيل الخروج</span>
          </button>
        </div>
      </header>
      <main className="content">
        <Outlet />
      </main>
    </div>
  );
}

/** Inbox link with the unread count (Sales / Manager only; Doctors have no inbox in V1). */
function NotificationsLink({ label, icon }: { label: string; icon: IconName }) {
  const { data } = useLoad<UnreadCount>('/api/me/notifications/unread-count');
  return (
    <NavLink to="/notifications">
      <Icon name={icon} />
      {label}
      {data && data.count > 0 && <span className="nav-count">{data.count}</span>}
    </NavLink>
  );
}
