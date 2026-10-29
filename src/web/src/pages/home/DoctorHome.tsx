import { Link, useNavigate } from 'react-router-dom';
import { appointmentKindLabels, appointmentStatusLabels, type AppointmentStatus, type DoctorQueueItem, type DoctorSchedule } from '../../api/task3';
import type { DoctorOwnWork } from '../../api/task4';
import { useAuth } from '../../auth/AuthContext';
import { appointmentTone, Badge, EmptyState, LoadError, Loading, PageHeader, Section, StatCard } from '../../components/ui';
import { formatAge } from '../../lib/patients';
import { clinicToday, formatClinicTime, formatDate, isoToClinicLocal } from '../../lib/time';
import { useLoad } from '../../lib/useLoad';

/**
 * The Doctor's start page. There is no Doctor dashboard in V1 (no dashboard permission or route): this page only arranges what the
 * Doctor already reads through the existing own-queue, own-schedule and own-work endpoints. Patient contact data, CRM data, Original
 * Sales and any other Doctor's finance are not available to those endpoints and never appear here.
 */
export function DoctorHome() {
  const { me, can } = useAuth();
  const navigate = useNavigate();
  const zone = me?.clinicTimezone;
  const today = clinicToday(zone);
  const queue = useLoad<DoctorQueueItem[]>('/api/doctor/queue');
  const schedule = useLoad<DoctorSchedule>(`/api/doctor/schedule?weekStart=${today}`);

  const appointments = schedule.data?.appointments ?? [];
  const dayOf = (iso: string) => isoToClinicLocal(iso, zone).slice(0, 10);
  const todays = appointments.filter((a) => dayOf(a.startAt) === today).sort((a, b) => a.startAt.localeCompare(b.startAt));
  const upcoming = appointments
    .filter((a) => dayOf(a.startAt) > today && (a.status === 'Scheduled' || a.status === 'Confirmed'))
    .sort((a, b) => a.startAt.localeCompare(b.startAt))
    .slice(0, 8);
  const count = (status: AppointmentStatus) => todays.filter((a) => a.status === status).length;
  const loading = (!queue.data && !queue.error) || (!schedule.data && !schedule.error);

  return (
    <>
      <PageHeader
        title="الرئيسية"
        lead={`${me?.displayName ?? ''} — ${formatDate(today)}`}
        actions={
          <>
            <button type="button" onClick={() => navigate('/queue')}>
              قائمة الانتظار
            </button>
            <button type="button" className="secondary" onClick={() => navigate('/my-schedule')}>
              جدولي
            </button>
          </>
        }
      />
      {queue.error ? <LoadError error={queue.error} /> : null}
      {schedule.error ? <LoadError error={schedule.error} /> : null}
      {loading && <Loading />}

      {!loading && (
        <>
          <div className="grid cols-4">
            <StatCard label="في الانتظار الآن" value={queue.data?.length ?? 0} icon="queue" to="/queue" tone={(queue.data?.length ?? 0) > 0 ? 'warning' : undefined} hint="وصلوا ولم تبدأ زيارتهم" />
            <StatCard label="قيد العلاج" value={count('InTreatment')} icon="tooth" hint="زيارات بدأت اليوم" />
            <StatCard label="مكتملة اليوم" value={count('Completed')} icon="check" tone="success" />
            <StatCard label="مواعيدي اليوم" value={todays.length} icon="calendar" to="/my-schedule" />
          </div>

          <div className="grid cols-2">
            <Section title="قائمة الانتظار" sub="المرضى الذين سجّل الاستقبال حضورهم" actions={<Link to="/queue">فتح القائمة</Link>}>
              {(queue.data?.length ?? 0) === 0 ? (
                <EmptyState title="لا يوجد مرضى في الانتظار" hint="سيظهر المريض هنا بعد تسجيل حضوره." icon="queue" />
              ) : (
                <table>
                  <thead>
                    <tr>
                      <th>حضر</th>
                      <th>المريض</th>
                      <th>السبب</th>
                    </tr>
                  </thead>
                  <tbody>
                    {queue.data?.slice(0, 6).map((q) => (
                      <tr key={q.appointmentId}>
                        <td className="time-cell">{formatClinicTime(q.arrivedAt, zone)}</td>
                        <td>
                          <Link to={`/patients/${q.patient.id}`}>{q.patient.fullName}</Link>
                          <div className="hint">{formatAge(q.patient.ageDisplay)}</div>
                        </td>
                        <td>{q.reason}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </Section>

            <Section title="المواعيد القادمة" sub="الأيام التالية من الأسبوع" actions={<Link to="/my-schedule">الجدول</Link>}>
              {upcoming.length === 0 ? (
                <EmptyState title="لا توجد مواعيد قادمة هذا الأسبوع" icon="calendar" />
              ) : (
                <table>
                  <thead>
                    <tr>
                      <th>اليوم</th>
                      <th>الوقت</th>
                      <th>المريض</th>
                    </tr>
                  </thead>
                  <tbody>
                    {upcoming.map((a) => (
                      <tr key={a.id}>
                        <td>{formatDate(dayOf(a.startAt))}</td>
                        <td className="time-cell">{formatClinicTime(a.startAt, zone)}</td>
                        <td>
                          <Link to={`/patients/${a.patient.id}`}>{a.patient.fullName}</Link>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </Section>
          </div>

          <Section title="جدول اليوم" sub="كل مواعيدك اليوم بحالاتها">
            {todays.length === 0 ? (
              <EmptyState title="لا توجد مواعيد اليوم" icon="calendar" />
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>الوقت</th>
                    <th>المريض</th>
                    <th>السبب</th>
                    <th>النوع</th>
                    <th>الحالة</th>
                    <th>الحضور</th>
                  </tr>
                </thead>
                <tbody>
                  {todays.map((a) => (
                    <tr key={a.id}>
                      <td className="time-cell">{formatClinicTime(a.startAt, zone)}</td>
                      <td>
                        <Link to={`/patients/${a.patient.id}`}>
                          {a.patient.patientCode} — {a.patient.fullName}
                        </Link>
                      </td>
                      <td>{a.reason}</td>
                      <td>{appointmentKindLabels[a.kind]}</td>
                      <td>
                        <Badge tone={appointmentTone(a.status)}>{appointmentStatusLabels[a.status]}</Badge>
                      </td>
                      <td>{a.arrivedAt ? <Badge tone="success">وصل {formatClinicTime(a.arrivedAt, zone)}</Badge> : <span className="muted">—</span>}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </Section>

          {can('Finance.OwnWork.Read') && <RecentOwnWork />}
        </>
      )}
    </>
  );
}

/** The latest of the Doctor's own work items (Finance.OwnWork.Read): own price, collection and remaining only. */
function RecentOwnWork() {
  const { me } = useAuth();
  const { data, error } = useLoad<DoctorOwnWork[]>('/api/doctor/me/work-items');
  const latest = [...(data ?? [])].sort((a, b) => b.chargedAt.localeCompare(a.chargedAt)).slice(0, 6);
  return (
    <Section title="آخر أعمالي" sub="ملخص من أعمالي المالية" actions={<Link to="/my-work">كل أعمالي</Link>}>
      {error ? <LoadError error={error} /> : null}
      {!data && !error && <Loading />}
      {data && latest.length === 0 && <EmptyState title="لا توجد أعمال مسجلة بعد" icon="wallet" />}
      {latest.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>التاريخ</th>
              <th>المريض</th>
              <th>الإجراء</th>
              <th className="num">السعر بعد الخصم</th>
              <th className="num">المحصل</th>
              <th className="num">المتبقي</th>
            </tr>
          </thead>
          <tbody>
            {latest.map((w) => (
              <tr key={w.chargeId}>
                <td>{isoToClinicLocal(w.chargedAt, me?.clinicTimezone).slice(0, 10)}</td>
                <td>{w.patient.fullName}</td>
                <td>{w.procedureName}</td>
                <td className="num">{w.finalPrice}</td>
                <td className="num">{w.collected}</td>
                <td className="num">{w.remaining}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Section>
  );
}
