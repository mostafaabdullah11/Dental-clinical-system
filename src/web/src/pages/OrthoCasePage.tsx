import { useCallback, useEffect, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import { orthoAreaLabels, type OrthoCaseView } from '../api/task4';
import { useAuth } from '../auth/AuthContext';
import { ErrorBanner } from '../components/Form';
import { InstallmentTable, OrthoCollections } from '../components/ortho/OrthoTables';
import { OrthoApplyForm } from '../components/ortho/OrthoApplyForm';
import { OrthoScheduleForm } from '../components/ortho/OrthoScheduleForm';
import { OrthoTermsForm } from '../components/ortho/OrthoTermsForm';

/**
 * FR-ORT-005..021. Each audience reads its own API contract: the Doctor route returns the schedule without allocations; the
 * Sales route (also used by the Manager) adds the collections available for manual application. Actions are rendered only for
 * the keys that allow them, and the server enforces every one of them again.
 */
export function OrthoCasePage() {
  const { id = '' } = useParams();
  const [search] = useSearchParams();
  const patientId = search.get('patientId');
  const { me, can } = useAuth();
  const isDoctor = me?.role === 'DOCTOR';
  const [view, setView] = useState<OrthoCaseView | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      setView(await api.get<OrthoCaseView>(isDoctor ? `/api/doctor/orthodontic-cases/${id}` : `/api/sales/orthodontic-cases/${id}`));
    } catch (e) {
      setError(e);
    }
  }, [id, isDoctor]);

  useEffect(() => {
    void load();
  }, [load]);

  const saved = (next: OrthoCaseView, message: string) => {
    setView(next);
    setNotice(message);
  };

  if (!view) {
    return error ? <ErrorBanner error={error} /> : <p className="centered">جارٍ التحميل…</p>;
  }

  const established = view.chargeEstablished;
  return (
    <section>
      <h1>
        حالة تقويم — {orthoAreaLabels[view.area] ?? view.area}
        {view.applianceType ? ` (${view.applianceType})` : ''}
      </h1>
      {patientId && <Link to={`/patients/${patientId}`}>العودة إلى ملف المريض</Link>}
      <ErrorBanner error={error} />
      {notice && (
        <p className="ok" role="status">
          {notice}
        </p>
      )}
      <OrthoSummary view={view} />
      <div className="card">
        <h2>جدول الأقساط</h2>
        {established ? <InstallmentTable installments={view.installments} /> : <p className="hint">تُنشأ الأقساط عند إنهاء الطبيب للزيارة وتثبيت المستحق.</p>}
        {view.history.length > 0 && (
          <details>
            <summary>أقساط ملغاة ({view.history.length})</summary>
            <InstallmentTable installments={view.history} />
          </details>
        )}
      </div>
      {!isDoctor && established && <OrthoCollections allocations={view.allocations} />}
      {!isDoctor && established && can('Orthodontics.Installments.ApplyPayment') && (
        <OrthoApplyForm key={`apply-${view.version}`} view={view} onApplied={(next) => saved(next, 'تم تطبيق الدفعة على الأقساط.')} />
      )}
      {established && can('Orthodontics.Terms.Edit') && <OrthoTermsForm key={`terms-${view.version}`} view={view} onSaved={(next) => saved(next, 'تم حفظ شروط التقويم.')} />}
      {established && can('Orthodontics.Installments.Edit') && (
        <OrthoScheduleForm key={`schedule-${view.version}`} view={view} onSaved={(next) => saved(next, 'تم حفظ تعديل الجدول.')} />
      )}
      {isDoctor && <p className="hint">عرض فقط: تعديل الشروط والجدول من صلاحيات المدير، وتطبيق الدفعات من صلاحيات الاستقبال.</p>}
    </section>
  );
}

function OrthoSummary({ view }: { view: OrthoCaseView }) {
  return (
    <div className="card narrow">
      <h2>البيانات المالية للحالة</h2>
      <p>
        الإجمالي: {view.grossTotal} — الخصم: {view.discount} — الإجمالي الفعلي: <strong>{view.effectiveOrthoTotal}</strong> ج.م
      </p>
      <p>
        المقدم: {view.terms.depositAmount} (المدفوع منه {view.depositPaid}) — عدد الشهور: {view.terms.numberOfMonths} — مراجعة الشروط رقم{' '}
        {view.terms.revisionNumber}
      </p>
      <p>
        المحصل: {view.collected} — المتبقي: <strong>{view.outstanding}</strong> ج.م
      </p>
      {view.requiresScheduleAdjustment && (
        <p className="error" role="alert">
          الجدول يحتاج إلى تعديل يدوي من المدير — فرق المبلغ: {view.scheduleDifference} ج.م — فرق عدد الأقساط: {view.monthPlanDifference}
        </p>
      )}
    </div>
  );
}
