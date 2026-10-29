import { Link } from 'react-router-dom';
import { appointmentStatusLabels, type AppointmentStatus } from '../../api/task3';
import type { DashboardAppointment, SalesDashboard } from '../../api/task4';
import { useAuth } from '../../auth/AuthContext';
import { appointmentTone, Badge, EmptyState, LoadError, Loading, PageHeader, Section, ShowMoreList, StatCard } from '../../components/ui';
import { formatClinicDateTime, formatClinicTime, formatDate } from '../../lib/time';
import { useLoad } from '../../lib/useLoad';

/**
 * FR-REPORT-009: exactly the eight Sales dashboard items, in the frozen order. Patient arrival is shown only as `arrivedAt`
 * inside Today's Appointments — never as a status and never as a separate widget. No attendance, overtime, Doctor finance
 * or Manager report data.
 */
export function SalesHome() {
  const { me } = useAuth();
  const zone = me?.clinicTimezone;
  const { data: d, error } = useLoad<SalesDashboard>('/api/sales/dashboard');

  const appointments = (rows: DashboardAppointment[]) =>
    rows.length === 0 ? (
      <EmptyState title="لا توجد مواعيد" icon="calendar" />
    ) : (
      <table>
        <thead>
          <tr>
            <th>الوقت</th>
            <th>المريض</th>
            <th>الطبيب</th>
            <th>الحالة</th>
            <th>الحضور</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((a) => (
            <tr key={a.id}>
              <td className="time-cell">{formatClinicTime(a.startAt, zone)}</td>
              <td>
                <Link to={`/patients/${a.patient.id}`}>{a.patient.fullName}</Link>
              </td>
              <td>{a.doctorName}</td>
              <td>
                <Badge tone={appointmentTone(a.status)}>{appointmentStatusLabels[a.status as AppointmentStatus] ?? a.status}</Badge>
              </td>
              <td>{a.arrivedAt ? <Badge tone="success">وصل {formatClinicTime(a.arrivedAt, zone)}</Badge> : <span className="muted">—</span>}</td>
            </tr>
          ))}
        </tbody>
      </table>
    );

  return (
    <>
      <PageHeader title="الرئيسية" lead={d ? `لوحة المبيعات / الاستقبال — ${formatDate(d.today)}` : 'لوحة المبيعات / الاستقبال'} />
      {error ? <LoadError error={error} /> : null}
      {!d && !error && <Loading />}
      {d && (
        <>
          <div className="grid cols-4">
            <StatCard label="1. مواعيد اليوم" value={d.todaysAppointments.length} icon="calendar" to="#today" />
            <StatCard label="2. عملاء محتملون جدد" value={d.newLeads.length} icon="phone" to="#leads" />
            <StatCard label="3. متابعات معلقة" value={d.pendingFollowUps.length} icon="clipboard" to="#followups" />
            <StatCard label="4. تحويلات جديدة" value={d.newReferrals.length} icon="refer" to="#referrals" />
          </div>
          <div className="grid cols-4">
            <StatCard label="5. مواعيد الغد" value={d.tomorrowsAppointments.length} icon="calendar" to="#tomorrow" />
            <StatCard label="6. أقساط تقويم مستحقة غداً" value={d.tomorrowsOrthoInstallments.length} icon="money" to="#ortho" />
            <StatCard label="7. أرصدة متبقية" value={d.outstandingBalances.length} icon="wallet" tone="warning" to="#balances" />
            <StatCard label="8. طلبات تقييم بانتظار الإرسال" value={d.feedbackRequestsToSend.length} icon="star" to="#feedback" />
          </div>

          <div id="today">
            <Section title="1. مواعيد اليوم" sub="يظهر وقت الحضور المسجل بجانب كل موعد">
              {appointments(d.todaysAppointments)}
            </Section>
          </div>

          <div className="grid cols-2">
            <div id="leads">
              <Section title="2. عملاء محتملون جدد">
                {d.newLeads.length === 0 ? (
                  <EmptyState title="لا يوجد عملاء جدد" icon="phone" />
                ) : (
                  <ShowMoreList
                    items={d.newLeads}
                    keyOf={(l) => l.id}
                    render={(l) => (
                      <>
                        <Link to={`/leads/${l.id}`}>{l.name}</Link>
                        <span className="muted">{formatClinicDateTime(l.createdAt, zone)}</span>
                      </>
                    )}
                  />
                )}
              </Section>
            </div>
            <div id="followups">
              <Section title="3. متابعات معلقة">
                {d.pendingFollowUps.length === 0 ? (
                  <EmptyState title="لا توجد متابعات معلقة" icon="clipboard" />
                ) : (
                  <ShowMoreList
                    items={d.pendingFollowUps}
                    keyOf={(f) => f.id}
                    render={(f) => (
                      <>
                        <Link to={`/follow-ups/${f.id}`}>{f.patient?.fullName ?? f.leadName}</Link>
                        <span className="muted">{f.dueDate}</span>
                      </>
                    )}
                  />
                )}
              </Section>
            </div>
          </div>

          <div className="grid cols-2">
            <div id="referrals">
              <Section title="4. تحويلات جديدة">
                {d.newReferrals.length === 0 ? (
                  <EmptyState title="لا توجد تحويلات جديدة" icon="refer" />
                ) : (
                  <ShowMoreList
                    items={d.newReferrals}
                    keyOf={(r) => r.id}
                    render={(r) => (
                      <>
                        <Link to={`/patients/${r.patient.id}`}>{r.patient.fullName}</Link>
                        <Badge tone="info">{r.referralType}</Badge>
                      </>
                    )}
                  />
                )}
              </Section>
            </div>
            <div id="feedback">
              <Section title="8. طلبات تقييم بانتظار الإرسال">
                {d.feedbackRequestsToSend.length === 0 ? (
                  <EmptyState title="لا توجد طلبات بانتظار الإرسال" icon="star" />
                ) : (
                  <ShowMoreList
                    items={d.feedbackRequestsToSend}
                    keyOf={(f) => f.id}
                    render={(f) => (
                      <>
                        <span>{f.patient.fullName}</span>
                        <span className="muted">{f.doctorName}</span>
                      </>
                    )}
                  />
                )}
              </Section>
            </div>
          </div>

          <div id="tomorrow">
            <Section title="5. مواعيد الغد">{appointments(d.tomorrowsAppointments)}</Section>
          </div>

          <div className="grid cols-2">
            <div id="ortho">
              <Section title="6. أقساط تقويم مستحقة غداً">
                {d.tomorrowsOrthoInstallments.length === 0 ? (
                  <EmptyState title="لا توجد أقساط مستحقة غداً" icon="money" />
                ) : (
                  <ShowMoreList
                    items={d.tomorrowsOrthoInstallments}
                    keyOf={(i) => i.installmentId}
                    render={(i) => (
                      <>
                        <Link to={`/ortho/${i.caseId}?patientId=${i.patient.id}`}>{i.patient.fullName}</Link>
                        <span className="muted">
                          القسط {i.installmentNumber} — المتبقي {i.remaining} ج.م
                        </span>
                      </>
                    )}
                  />
                )}
              </Section>
            </div>
            <div id="balances">
              <Section title="7. أرصدة متبقية">
                {d.outstandingBalances.length === 0 ? (
                  <EmptyState title="لا توجد أرصدة متبقية" icon="wallet" />
                ) : (
                  <ShowMoreList
                    items={d.outstandingBalances}
                    keyOf={(o) => o.patient.id}
                    render={(o) => (
                      <>
                        <Link to={`/patients/${o.patient.id}`}>{o.patient.fullName}</Link>
                        <strong>{o.outstanding} ج.م</strong>
                      </>
                    )}
                  />
                )}
              </Section>
            </div>
          </div>
        </>
      )}
    </>
  );
}
