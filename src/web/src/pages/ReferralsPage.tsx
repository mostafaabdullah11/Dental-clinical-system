import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { referralStatusLabels, referralTypeLabels, type ReferralStatus, type SalesReferral } from '../api/task3';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner } from '../components/Form';
import { formatClinicDateTime } from '../lib/time';
import { useLoad } from '../lib/useLoad';

const statuses = Object.keys(referralStatusLabels) as ReferralStatus[];

/**
 * FR-REF-003 / -004 (Sales / Reception and Manager): Doctor referrals with destination, reason and follow-up
 * instructions. The Doctor's notes are not part of this view at all.
 */
export function ReferralsPage() {
  const { me, can } = useAuth();
  const [status, setStatus] = useState<ReferralStatus | ''>('New');
  const { data, error, reload } = useLoad<SalesReferral[]>(`/api/sales/referrals${status ? `?status=${status}` : ''}`);
  const [actionError, setActionError] = useState<unknown>(null);

  async function change(r: SalesReferral, next: ReferralStatus) {
    setActionError(null);
    try {
      await api.patch(`/api/sales/referrals/${r.id}/status`, { status: next }, r.version);
      void reload();
    } catch (e) {
      setActionError(e);
    }
  }

  return (
    <section>
      <h1>تحويلات الأطباء</h1>
      <div className="card inline">
        <label className="field">
          <span>الحالة</span>
          <select value={status} onChange={(e) => setStatus(e.target.value as ReferralStatus | '')}>
            <option value="">الكل</option>
            {statuses.map((s) => (
              <option key={s} value={s}>
                {referralStatusLabels[s]}
              </option>
            ))}
          </select>
        </label>
      </div>
      <ErrorBanner error={error} />
      <ErrorBanner error={actionError} />
      <table>
        <thead>
          <tr>
            <th>التاريخ</th>
            <th>المريض</th>
            <th>الجهة</th>
            <th>السبب</th>
            <th>تعليمات المتابعة</th>
            <th>الطبيب</th>
            <th>الحالة</th>
          </tr>
        </thead>
        <tbody>
          {data?.map((r) => (
            <tr key={r.id}>
              <td>{formatClinicDateTime(r.createdAt, me?.clinicTimezone)}</td>
              <td>
                <Link to={`/patients/${r.patient.id}`}>
                  {r.patient.patientCode} — {r.patient.fullName}
                </Link>
                {r.patient.phone && <div className="hint">{r.patient.phone}</div>}
              </td>
              <td>{referralTypeLabels[r.referralType]}</td>
              <td>{r.reason}</td>
              <td>{r.followUpInstructions ?? '—'}</td>
              <td>{r.referringDoctorName}</td>
              <td>
                {can('Crm.Referrals.UpdateStatus') ? (
                  <select value={r.status} onChange={(e) => void change(r, e.target.value as ReferralStatus)}>
                    {statuses.map((s) => (
                      <option key={s} value={s}>
                        {referralStatusLabels[s]}
                      </option>
                    ))}
                  </select>
                ) : (
                  referralStatusLabels[r.status]
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {data && data.length === 0 && <p className="hint">لا توجد تحويلات.</p>}
    </section>
  );
}
