import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import type { Prescription, VisitDetail } from '../../api/task3';
import { useLoad } from '../../lib/useLoad';
import { ErrorBanner } from '../Form';

interface ItemRow {
  id: string | null;
  medicationName: string;
  dosage: string;
  frequency: string;
  duration: string;
  instructions: string;
}

const emptyRow = (): ItemRow => ({ id: null, medicationName: '', dosage: '', frequency: '', duration: '', instructions: '' });

/** FR-RX-001..004: prescriptions of this Visit; editable while the Visit is open, then only the own Doctor's correction. */
export function PrescriptionPanel({ visit }: { visit: VisitDetail }) {
  const { data, error, reload } = useLoad<Prescription[]>(`/api/doctor/patients/${visit.patient.id}/prescriptions`);
  const mine = (data ?? []).filter((p) => p.visitId === visit.id);
  const [creating, setCreating] = useState(false);

  return (
    <div className="card">
      <h2>الوصفات الطبية</h2>
      <ErrorBanner error={error} />
      {mine.map((p) => (
        <PrescriptionView key={p.id} prescription={p} visit={visit} onChanged={() => void reload()} />
      ))}
      {visit.canEdit && !creating && (
        <button type="button" className="secondary" onClick={() => setCreating(true)}>
          وصفة جديدة
        </button>
      )}
      {creating && (
        <PrescriptionEditor
          onSave={async (body) => {
            await api.post(`/api/doctor/visits/${visit.id}/prescriptions`, body);
            setCreating(false);
            void reload();
          }}
          onCancel={() => setCreating(false)}
        />
      )}
    </div>
  );
}

function PrescriptionView({ prescription: p, visit, onChanged }: { prescription: Prescription; visit: VisitDetail; onChanged: () => void }) {
  const [mode, setMode] = useState<'view' | 'edit' | 'correct'>('view');
  const canEdit = visit.canEdit && p.isOwn;
  const canCorrect = p.visitFinalized && p.isOwn;

  if (mode === 'edit') {
    return (
      <PrescriptionEditor
        initial={p}
        onSave={async (body) => {
          await api.put(`/api/doctor/prescriptions/${p.id}`, body, p.version);
          setMode('view');
          onChanged();
        }}
        onCancel={() => setMode('view')}
      />
    );
  }

  if (mode === 'correct') {
    return (
      <PrescriptionEditor
        initial={p}
        withReason
        onSave={async (body) => {
          await api.post(`/api/doctor/prescriptions/${p.id}/corrections`, body);
          setMode('view');
          onChanged();
        }}
        onCancel={() => setMode('view')}
      />
    );
  }

  return (
    <div className="subsection">
      <ol>
        {p.items.map((i) => (
          <li key={i.id}>
            <strong>{i.medicationName}</strong> {[i.dosage, i.frequency, i.duration].filter(Boolean).join(' — ')}
            {i.instructions && <div className="hint">{i.instructions}</div>}
          </li>
        ))}
      </ol>
      {p.generalNotes && <p className="hint">{p.generalNotes}</p>}
      <div className="actions">
        <Link to={`/prescriptions/${p.id}/print`} target="_blank" rel="noreferrer">
          طباعة
        </Link>
        {canEdit && (
          <button type="button" className="link" onClick={() => setMode('edit')}>
            تعديل
          </button>
        )}
        {canCorrect && (
          <button type="button" className="link" onClick={() => setMode('correct')}>
            تصحيح (بعد إنهاء الزيارة)
          </button>
        )}
      </div>
    </div>
  );
}

function PrescriptionEditor({ initial, withReason = false, onSave, onCancel }: {
  initial?: Prescription;
  withReason?: boolean;
  onSave: (body: object) => Promise<void>;
  onCancel: () => void;
}) {
  const [notes, setNotes] = useState(initial?.generalNotes ?? '');
  const [rows, setRows] = useState<ItemRow[]>(
    initial?.items.map((i) => ({
      id: i.id, medicationName: i.medicationName, dosage: i.dosage ?? '', frequency: i.frequency ?? '', duration: i.duration ?? '', instructions: i.instructions ?? '',
    })) ?? [emptyRow()],
  );
  const [reason, setReason] = useState('');
  const [error, setError] = useState<unknown>(null);
  const set = (i: number, patch: Partial<ItemRow>) => setRows(rows.map((r, j) => (j === i ? { ...r, ...patch } : r)));
  const orNull = (v: string) => (v.trim() ? v.trim() : null);

  async function save() {
    setError(null);
    try {
      await onSave({
        generalNotes: orNull(notes),
        items: rows.filter((r) => r.medicationName.trim()).map((r) => ({
          id: r.id, medicationName: r.medicationName.trim(), dosage: orNull(r.dosage), frequency: orNull(r.frequency), duration: orNull(r.duration),
          instructions: orNull(r.instructions),
        })),
        ...(withReason ? { reason: orNull(reason) } : {}),
      });
    } catch (e) {
      setError(e);
    }
  }

  return (
    <div className="subsection">
      {rows.map((r, i) => (
        <div key={i} className="inline wrap">
          <input value={r.medicationName} onChange={(e) => set(i, { medicationName: e.target.value })} placeholder="اسم الدواء" />
          <input value={r.dosage} onChange={(e) => set(i, { dosage: e.target.value })} placeholder="الجرعة" />
          <input value={r.frequency} onChange={(e) => set(i, { frequency: e.target.value })} placeholder="عدد المرات" />
          <input value={r.duration} onChange={(e) => set(i, { duration: e.target.value })} placeholder="المدة" />
          <input value={r.instructions} onChange={(e) => set(i, { instructions: e.target.value })} placeholder="تعليمات" />
          <button type="button" className="link danger" onClick={() => setRows(rows.filter((_, j) => j !== i))}>
            حذف
          </button>
        </div>
      ))}
      <button type="button" className="secondary" onClick={() => setRows([...rows, emptyRow()])}>
        إضافة دواء
      </button>
      <label className="field">
        <span>ملاحظات عامة</span>
        <textarea value={notes} onChange={(e) => setNotes(e.target.value)} />
      </label>
      {withReason && (
        <label className="field">
          <span>سبب التصحيح</span>
          <input value={reason} onChange={(e) => setReason(e.target.value)} />
        </label>
      )}
      <ErrorBanner error={error} />
      <div className="actions">
        <button type="button" onClick={() => void save()}>
          حفظ
        </button>
        <button type="button" className="link" onClick={onCancel}>
          إلغاء
        </button>
      </div>
    </div>
  );
}
