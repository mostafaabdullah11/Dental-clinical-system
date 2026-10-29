import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import { appointmentStatusLabels, type AppointmentStatus } from '../../api/task3';
import { paymentMethodLabels, type ManagerDashboard } from '../../api/task4';
import { useAuth } from '../../auth/AuthContext';
import { EmptyState, Icon, LoadError, Loading, PageHeader, Section, StatCard, type IconName } from '../../components/ui';
import { BarList, ColumnChart } from '../../components/ui/charts';
import { ratioValue } from '../../lib/charts';
import { clinicToday, formatDate, lastDays } from '../../lib/time';

interface DoctorRow {
  doctorId: string;
  doctorName: string;
  isActive: boolean;
  charges: number;
  billed: string;
  collected: string;
}

interface SalesRow {
  salesEmployeeId: string | null;
  name: string;
  isActive: boolean;
  leadSourcedCollected: string;
  commissionAccrued: string;
}

interface SalesPerformance {
  employees: SalesRow[];
  walkInCollected: string;
}

/** "1250.00" → "1250" for chart labels; amounts elsewhere keep the exact decimal text. */
function compactMoney(text: string): string {
  return text.endsWith('.00') ? text.slice(0, -3) : text;
}

function sum(items: { count: number }[]): number {
  return items.reduce((total, item) => total + item.count, 0);
}

/**
 * FR-REPORT-001: the Manager's live dashboard for one clinic-local date (default today). Everything here is read from the
 * existing dashboard and report endpoints; there is no Daily Closing and no sealed daily state.
 */
export function ManagerHome() {
  const { me } = useAuth();
  const zone = me?.clinicTimezone;
  const today = clinicToday(zone);
  const [date, setDate] = useState(today);
  const days = useMemo(() => lastDays(date, 7), [date]);
  const [series, setSeries] = useState<ManagerDashboard[] | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    let cancelled = false;
    setSeries(null);
    setError(null);
    Promise.all(days.map((d) => api.get<ManagerDashboard>(`/api/manager/dashboard?date=${d}`))).then(
      (rows) => !cancelled && setSeries(rows),
      (e: unknown) => !cancelled && setError(e),
    );
    return () => {
      cancelled = true;
    };
  }, [days]);

  const d = series?.[series.length - 1];
  const monthStart = today.slice(0, 8) + '01';

  return (
    <>
      <PageHeader
        title="الرئيسية"
        lead={`لوحة المدير — ${formatDate(date)}`}
        actions={
          <>
            <label className="field checkbox">
              <span>التاريخ</span>
              <input type="date" value={date} max={today} onChange={(e) => e.target.value && setDate(e.target.value)} />
            </label>
            {date !== today && (
              <button type="button" className="secondary" onClick={() => setDate(today)}>
                اليوم
              </button>
            )}
          </>
        }
      />
      {error ? <LoadError error={error} /> : null}
      {!d && !error && <Loading />}
      {d && series && (
        <>
          <QuickLinks />

          <div className="grid cols-4">
            <StatCard label="مواعيد اليوم" value={sum(d.appointmentsByStatus)} icon="calendar" to="/appointments" hint="كل الحالات" />
            <StatCard label="حضور مسجل" value={d.arrivalsRecorded} icon="queue" hint="مرضى وصلوا للعيادة" />
            <StatCard label="زيارات منتهية" value={d.visitsFinalized} icon="check" tone="success" />
            <StatCard label="عملاء محتملون جدد" value={d.newLeadsToday} icon="phone" to="/leads" />
          </div>

          <div className="grid cols-4">
            <StatCard label="المستحق اليوم" value={d.billedToday} unit="ج.م" icon="wallet" to="/finance" />
            <StatCard label="المحصل اليوم" value={d.collectedToday} unit="ج.م" icon="money" tone="success" to="/finance" />
            <StatCard label="مصروفات اليوم" value={d.expensesToday} unit="ج.م" icon="money" to="/expenses" />
            <StatCard label="إجمالي المتبقي على المرضى" value={d.outstandingTotal} unit="ج.م" icon="wallet" tone="warning" to="/finance" />
          </div>

          <div className="grid cols-4">
            <StatCard
              label="متابعات مفتوحة"
              value={d.openFollowUps}
              icon="clipboard"
              to="/follow-ups"
              tone={d.overdueFollowUps > 0 ? 'warning' : undefined}
              hint={`متأخرة: ${d.overdueFollowUps}`}
            />
            <StatCard
              label="بدون مسؤول"
              value={d.unassignedLeads + d.unassignedFollowUps}
              icon="alert"
              tone={d.unassignedLeads + d.unassignedFollowUps > 0 ? 'danger' : undefined}
              hint={`عملاء ${d.unassignedLeads} — متابعات ${d.unassignedFollowUps}`}
            />
            <StatCard
              label="أقساط تقويم متأخرة"
              value={d.overdueOrthoInstallments}
              icon="alert"
              tone={d.overdueOrthoInstallments > 0 ? 'danger' : undefined}
              to="/reports"
            />
            <StatCard label="طلبات تقييم بانتظار الإرسال" value={d.feedbackRequestsToSend} icon="star" to="/feedback" />
          </div>

          <div className="grid cols-2">
            <Section title="المواعيد حسب الحالة" sub="الحالات السبع المعتمدة" pad>
              {d.appointmentsByStatus.length === 0 ? (
                <EmptyState title="لا توجد مواعيد في هذا اليوم" icon="calendar" />
              ) : (
                <BarList
                  label="المواعيد حسب الحالة"
                  items={d.appointmentsByStatus.map((s) => ({ label: appointmentStatusLabels[s.key as AppointmentStatus] ?? s.key, value: s.count }))}
                />
              )}
            </Section>
            <Section title="المقبوضات حسب وسيلة الدفع" sub="لهذا اليوم" pad>
              {d.cashByMethodToday.length === 0 ? (
                <EmptyState title="لا توجد مقبوضات في هذا اليوم" icon="money" />
              ) : (
                <BarList
                  label="المقبوضات حسب وسيلة الدفع"
                  items={d.cashByMethodToday.map((c) => ({
                    label: paymentMethodLabels[c.key] ?? c.label,
                    value: ratioValue(c.amount),
                    text: `${c.amount} ج.م`,
                  }))}
                />
              )}
            </Section>
          </div>

          <div className="grid cols-2">
            <Section title="المحصل — آخر 7 أيام" sub="بالجنيه المصري" pad>
              <ColumnChart
                label="المحصل في آخر سبعة أيام"
                points={series.map((s, i) => ({ label: (days[i] ?? '').slice(5), value: ratioValue(s.collectedToday), text: compactMoney(s.collectedToday) }))}
              />
            </Section>
            <Section title="المواعيد — آخر 7 أيام" sub="كل الحالات" pad>
              <ColumnChart label="المواعيد في آخر سبعة أيام" points={series.map((s, i) => ({ label: (days[i] ?? '').slice(5), value: sum(s.appointmentsByStatus) }))} />
            </Section>
          </div>

          <div className="grid cols-2">
            <StatCard label="الأرصدة الدائنة للمرضى" value={d.patientCreditTotal} unit="ج.م" icon="wallet" hint="ليست إيراداً حتى تُطبَّق" />
            <StatCard label="مستحقات أطباء معلقة" value={d.pendingDoctorEntitlement} unit="ج.م" icon="wallet" hint="بند توضيحي وليس خصماً" />
          </div>

          <PerformanceTables from={monthStart} to={today} />
        </>
      )}
    </>
  );
}

function QuickLinks() {
  const links: { to: string; label: string; icon: IconName }[] = [
    { to: '/patients', label: 'المرضى', icon: 'users' },
    { to: '/appointments', label: 'المواعيد', icon: 'calendar' },
    { to: '/finance', label: 'المالية', icon: 'wallet' },
    { to: '/reports', label: 'التقارير', icon: 'chart' },
    { to: '/payroll', label: 'الرواتب', icon: 'money' },
    { to: '/attendance', label: 'الحضور', icon: 'clock' },
  ];
  return (
    <nav className="quick-links" aria-label="اختصارات">
      {links.map((l) => (
        <Link key={l.to} to={l.to}>
          <Icon name={l.icon} size={18} />
          {l.label}
        </Link>
      ))}
    </nav>
  );
}

/** Month-to-date Doctor and Sales performance: the existing Manager reports, shown as a summary. */
function PerformanceTables({ from, to }: { from: string; to: string }) {
  const [doctors, setDoctors] = useState<DoctorRow[] | null>(null);
  const [sales, setSales] = useState<SalesPerformance | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    let cancelled = false;
    const query = `from=${from}&to=${to}`;
    Promise.all([
      api.get<DoctorRow[]>(`/api/manager/reports/doctor-performance?${query}`),
      api.get<SalesPerformance>(`/api/manager/reports/sales-performance?${query}`),
    ]).then(
      ([d, s]) => {
        if (!cancelled) {
          setDoctors(d);
          setSales(s);
        }
      },
      (e: unknown) => !cancelled && setError(e),
    );
    return () => {
      cancelled = true;
    };
  }, [from, to]);

  if (error) {
    return <LoadError error={error} />;
  }
  if (!doctors || !sales) {
    return <Loading />;
  }

  return (
    <div className="grid cols-2">
      <Section title="أداء الأطباء" sub="من بداية الشهر" actions={<Link to="/reports">التقرير الكامل</Link>}>
        {doctors.length === 0 ? (
          <EmptyState title="لا توجد بيانات" icon="chart" />
        ) : (
          <table>
            <thead>
              <tr>
                <th>الطبيب</th>
                <th className="num">البنود</th>
                <th className="num">المستحق</th>
                <th className="num">المحصل</th>
              </tr>
            </thead>
            <tbody>
              {doctors.map((r) => (
                <tr key={r.doctorId}>
                  <td>
                    {r.doctorName} {!r.isActive && <span className="badge off">غير نشط</span>}
                  </td>
                  <td className="num">{r.charges}</td>
                  <td className="num">{r.billed}</td>
                  <td className="num">{r.collected}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Section>
      <Section title="أداء المبيعات / الاستقبال" sub="من بداية الشهر" actions={<Link to="/reports">التقرير الكامل</Link>}>
        {sales.employees.length === 0 ? (
          <EmptyState title="لا توجد بيانات" icon="chart" />
        ) : (
          <table>
            <thead>
              <tr>
                <th>الموظف</th>
                <th className="num">محصل من عملاء محتملين</th>
                <th className="num">العمولة</th>
              </tr>
            </thead>
            <tbody>
              {sales.employees.map((r) => (
                <tr key={r.salesEmployeeId ?? r.name}>
                  <td>
                    {r.name} {!r.isActive && <span className="badge off">غير نشط</span>}
                  </td>
                  <td className="num">{r.leadSourcedCollected}</td>
                  <td className="num">{r.commissionAccrued}</td>
                </tr>
              ))}
              <tr>
                <td>مباشر (Walk-In) — بدون عمولة</td>
                <td className="num">{sales.walkInCollected}</td>
                <td className="num">0.00</td>
              </tr>
            </tbody>
          </table>
        )}
      </Section>
    </div>
  );
}
