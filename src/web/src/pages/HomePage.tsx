import { Link } from 'react-router-dom';
import { appointmentStatusLabels, type AppointmentStatus } from '../api/task3';
import { paymentMethodLabels, type DashboardAppointment, type ManagerDashboard, type SalesDashboard } from '../api/task4';
import { roleLabels } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner } from '../components/Form';
import { formatClinicDateTime, formatClinicTime } from '../lib/time';
import { useLoad } from '../lib/useLoad';

export function HomePage() {
  const { me, can } = useAuth();
  if (!me) {
    return null;
  }

  return (
    <section>
      <h1>مرحباً، {me.displayName}</h1>
      <p>الدور: {roleLabels[me.role]}</p>
      {can('Reports.Dashboard.Manager') && <ManagerDashboardView />}
      {can('Reports.Dashboard.Sales') && <SalesDashboardView />}
      {me.role === 'DOCTOR' && <p>استخدم القائمة لقائمة الانتظار وجدولك وأعمالك المالية وراتبك.</p>}
    </section>
  );
}

/** FR-REPORT-001: live figures computed on read. No Daily Closing. */
function ManagerDashboardView() {
  const { data: d, error } = useLoad<ManagerDashboard>('/api/manager/dashboard');
  return (
    <div className="card">
      <h2>لوحة اليوم</h2>
      <ErrorBanner error={error} />
      {d && (
        <>
          <p>التاريخ: {d.date}</p>
          <p>
            المواعيد:{' '}
            {d.appointmentsByStatus.map((s) => `${appointmentStatusLabels[s.key as AppointmentStatus] ?? s.key}: ${s.count}`).join(' — ') || 'لا توجد'}
          </p>
          <p>حضور مسجل: {d.arrivalsRecorded} — زيارات منتهية: {d.visitsFinalized}</p>
          <p>المستحقات اليوم: {d.billedToday} — المحصل اليوم: {d.collectedToday} — مصروفات اليوم: {d.expensesToday} ج.م</p>
          <p>المقبوضات: {d.cashByMethodToday.map((c) => `${paymentMethodLabels[c.key] ?? c.label}: ${c.amount}`).join(' — ') || '—'}</p>
          <p>عملاء محتملون جدد اليوم: {d.newLeadsToday} — متابعات مفتوحة: {d.openFollowUps} (متأخرة: {d.overdueFollowUps})</p>
          <p>
            عملاء محتملون بدون مسؤول: {d.unassignedLeads} — متابعات بدون مسؤول: {d.unassignedFollowUps}
          </p>
          <p>إجمالي المتبقي على المرضى: {d.outstandingTotal} — الأرصدة الدائنة: {d.patientCreditTotal} — مستحقات أطباء معلقة: {d.pendingDoctorEntitlement}</p>
          <p>أقساط تقويم متأخرة: {d.overdueOrthoInstallments} — طلبات تقييم بانتظار الإرسال: {d.feedbackRequestsToSend}</p>
        </>
      )}
    </div>
  );
}

/** FR-REPORT-009: exactly the eight Sales items; arrival appears only as arrivedAt inside Today's Appointments. */
function SalesDashboardView() {
  const { me } = useAuth();
  const zone = me?.clinicTimezone;
  const { data: d, error } = useLoad<SalesDashboard>('/api/sales/dashboard');
  const appointments = (rows: DashboardAppointment[]) => (
    <table>
      <tbody>
        {rows.map((a) => (
          <tr key={a.id}>
            <td>{formatClinicTime(a.startAt, zone)}</td>
            <td>
              <Link to={`/patients/${a.patient.id}`}>{a.patient.fullName}</Link>
            </td>
            <td>{a.doctorName}</td>
            <td>{appointmentStatusLabels[a.status as AppointmentStatus] ?? a.status}</td>
            <td>{a.arrivedAt ? `وصل ${formatClinicTime(a.arrivedAt, zone)}` : ''}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );

  return (
    <div className="card">
      <h2>لوحة المبيعات / الاستقبال</h2>
      <ErrorBanner error={error} />
      {d && (
        <>
          <h3>1. مواعيد اليوم ({d.todaysAppointments.length})</h3>
          {appointments(d.todaysAppointments)}
          <h3>2. عملاء محتملون جدد ({d.newLeads.length})</h3>
          {d.newLeads.map((l) => (
            <p key={l.id}>
              <Link to={`/leads/${l.id}`}>{l.name}</Link> — {formatClinicDateTime(l.createdAt, zone)}
            </p>
          ))}
          <h3>3. متابعات معلقة ({d.pendingFollowUps.length})</h3>
          {d.pendingFollowUps.map((f) => (
            <p key={f.id}>
              <Link to={`/follow-ups/${f.id}`}>{f.patient?.fullName ?? f.leadName}</Link> — {f.dueDate}
            </p>
          ))}
          <h3>4. تحويلات جديدة ({d.newReferrals.length})</h3>
          {d.newReferrals.map((r) => (
            <p key={r.id}>
              {r.patient.fullName} — {r.referralType}
            </p>
          ))}
          <h3>5. مواعيد الغد ({d.tomorrowsAppointments.length})</h3>
          {appointments(d.tomorrowsAppointments)}
          <h3>6. أقساط تقويم مستحقة غداً ({d.tomorrowsOrthoInstallments.length})</h3>
          {d.tomorrowsOrthoInstallments.map((i) => (
            <p key={i.installmentId}>
              <Link to={`/ortho/${i.caseId}?patientId=${i.patient.id}`}>{i.patient.fullName}</Link> — القسط {i.installmentNumber} — المتبقي {i.remaining}
            </p>
          ))}
          <h3>7. أرصدة متبقية ({d.outstandingBalances.length})</h3>
          {d.outstandingBalances.map((o) => (
            <p key={o.patient.id}>
              <Link to={`/patients/${o.patient.id}`}>{o.patient.fullName}</Link> — {o.outstanding} ج.م
            </p>
          ))}
          <h3>8. طلبات تقييم بانتظار الإرسال ({d.feedbackRequestsToSend.length})</h3>
          {d.feedbackRequestsToSend.map((f) => (
            <p key={f.id}>
              {f.patient.fullName} — {f.doctorName}
            </p>
          ))}
        </>
      )}
    </div>
  );
}
