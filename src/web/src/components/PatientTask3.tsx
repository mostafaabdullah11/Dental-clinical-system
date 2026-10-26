import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import {
  appointmentStatusLabels, caseStatusLabels, discountRoleLabels, referralStatusLabels, referralTypeLabels, visitStatusLabels,
  type CaseSummary, type DoctorReferral, type PricingLineSales, type SalesAppointment, type SalesClinicalSummary, type VisitDetail,
  type VisitListItem,
} from '../api/task3';
import { useAuth } from '../auth/AuthContext';
import { formatClinicDateTime } from '../lib/time';
import { useLoad } from '../lib/useLoad';
import { ErrorBanner } from './Form';
import { DiscountEditor } from './visit/DiscountEditor';

/** Doctor patient view (FR-CLN-010): Visit history, treatment cases, referrals with the Doctor's notes, walk-in Visit. */
export function DoctorClinicalSection({ patientId }: { patientId: string }) {
  const { me, can } = useAuth();
  const navigate = useNavigate();
  const visits = useLoad<VisitListItem[]>(`/api/doctor/patients/${patientId}/visits`);
  const cases = useLoad<CaseSummary[]>(`/api/doctor/patients/${patientId}/treatment-cases`);
  const referrals = useLoad<DoctorReferral[]>(`/api/doctor/patients/${patientId}/referrals`);
  const [error, setError] = useState<unknown>(null);
  const zone = me?.clinicTimezone;

  async function startWalkIn() {
    setError(null);
    try {
      const visit = await api.post<VisitDetail>('/api/doctor/visits', { patientId, appointmentId: null });
      navigate(`/visits/${visit.id}`);
    } catch (e) {
      setError(e);
    }
  }

  return (
    <>
      <div className="card">
        <h2>الزيارات</h2>
        {me?.role === 'DOCTOR' && can('Clinical.Visits.Write') && (
          <button type="button" onClick={() => void startWalkIn()}>
            بدء زيارة بدون موعد
          </button>
        )}
        <ErrorBanner error={error ?? visits.error} />
        <table>
          <tbody>
            {visits.data?.map((v) => (
              <tr key={v.id}>
                <td>{formatClinicDateTime(v.startedAt, zone)}</td>
                <td>{v.doctor.name}</td>
                <td>{visitStatusLabels[v.status]}</td>
                <td>{v.treatmentCount} علاج</td>
                <td>
                  <Link to={`/visits/${v.id}`}>فتح</Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="card">
        <h2>الحالات العلاجية</h2>
        <ErrorBanner error={cases.error} />
        <table>
          <tbody>
            {cases.data?.map((c) => (
              <tr key={c.id}>
                <td>{c.procedureName}</td>
                <td>{c.teeth.join('، ') || '—'}</td>
                <td>{caseStatusLabels[c.status]}</td>
                <td>{c.startedBy.name}</td>
                <td>{formatClinicDateTime(c.startedAt, zone)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="card">
        <h2>التحويلات</h2>
        <ErrorBanner error={referrals.error} />
        {referrals.data?.map((r) => (
          <div key={r.id} className="subsection">
            <strong>{referralTypeLabels[r.referralType]}</strong> — {r.reason} — {referralStatusLabels[r.status]}
            {r.followUpInstructions && <div className="hint">تعليمات المتابعة: {r.followUpInstructions}</div>}
            {r.notes && <div className="hint">ملاحظات الطبيب: {r.notes}</div>}
          </div>
        ))}
      </div>
    </>
  );
}

/** Sales / Manager patient view: appointments, the patient-facing pricing lines (with discount) and the clinical summary. */
export function SalesPatientSection({ patientId }: { patientId: string }) {
  const { me, can } = useAuth();
  const zone = me?.clinicTimezone;
  const appointments = useLoad<SalesAppointment[]>(`/api/sales/appointments?patientId=${patientId}`);
  const lines = useLoad<PricingLineSales[]>(`/api/sales/pricing-lines?patientId=${patientId}`);
  const summary = useLoad<SalesClinicalSummary>(`/api/sales/patients/${patientId}/clinical-summary`);

  return (
    <>
      {can('Scheduling.Appointments.View.All') && (
        <div className="card">
          <h2>المواعيد</h2>
          {can('Scheduling.Appointments.Book') && <Link to={`/appointments?patientId=${patientId}`}>حجز موعد جديد</Link>}
          <ErrorBanner error={appointments.error} />
          <table>
            <tbody>
              {appointments.data?.map((a) => (
                <tr key={a.id}>
                  <td>{formatClinicDateTime(a.startAt, zone)}</td>
                  <td>{a.doctor.name}</td>
                  <td>{a.reason}</td>
                  <td>{appointmentStatusLabels[a.status]}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {can('Finance.PricingLines.Read') && (
        <div className="card">
          <h2>الأسعار والخصومات (قبل إنهاء الزيارة)</h2>
          <ErrorBanner error={lines.error} />
          {lines.data?.length === 0 && <p className="hint">لا توجد بنود مسعّرة مفتوحة.</p>}
          {lines.data?.map((l) => (
            <div key={l.lineId} className="subsection">
              <p>
                <strong>{l.kind === 'Consultation' ? 'كشف' : l.procedureName}</strong> — {l.treatingDoctor.name} — السعر: {l.price} — الخصم: {l.discount} —
                الإجمالي: {l.total} ج.م
                {l.lastSetBy && <span className="hint"> (آخر خصم: {discountRoleLabels[l.lastSetBy.role] ?? l.lastSetBy.role})</span>}
              </p>
              {can('Finance.Discount.Set') && (
                <DiscountEditor url={`/api/sales/pricing-lines/${l.lineId}/discount`} current={l.discount} entrySeq={l.entrySeq} onSaved={() => void lines.reload()} />
              )}
            </div>
          ))}
        </div>
      )}
      {can('Clinical.SalesSummary.Read') && summary.data && (
        <div className="card">
          <h2>ملخص العلاج</h2>
          <table>
            <thead>
              <tr>
                <th>الإجراء</th>
                <th>الطبيب</th>
                <th>الحالة</th>
                <th>السعر</th>
                <th>الخصم</th>
                <th>الإجمالي</th>
                <th>المدفوع / المتبقي</th>
              </tr>
            </thead>
            <tbody>
              {summary.data.cases.map((c) => (
                <tr key={c.caseId}>
                  <td>{c.procedureName}</td>
                  <td>{c.treatingDoctor.name}</td>
                  <td>{caseStatusLabels[c.status]}</td>
                  <td>{c.price ?? '—'}</td>
                  <td>{c.discount ?? '—'}</td>
                  <td>{c.total ?? '—'}</td>
                  <td>{c.paid !== null ? `${c.paid} / ${c.remaining ?? '—'}` : 'تتوفر مع وحدة المدفوعات'}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {summary.data.referrals.length > 0 && (
            <>
              <h3>التحويلات</h3>
              {summary.data.referrals.map((r, i) => (
                <p key={i}>
                  {referralTypeLabels[r.referralType]} — {r.reason}
                  {r.followUpInstructions ? ` — ${r.followUpInstructions}` : ''} — {referralStatusLabels[r.status]}
                </p>
              ))}
            </>
          )}
        </div>
      )}
    </>
  );
}
