import { useEffect, useState, type FormEvent } from 'react';
import { api } from '../api/client';
import type { ManagerSettings } from '../api/types';
import { ErrorBanner, Field } from '../components/Form';

export function SettingsPage() {
  const [settings, setSettings] = useState<ManagerSettings | null>(null);
  const [etag, setEtag] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [saved, setSaved] = useState(false);

  async function load() {
    try {
      const result = await api.getVersioned<ManagerSettings>('/api/manager/settings');
      setSettings(result.data);
      setEtag(result.etag);
    } catch (e) {
      setError(e);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!settings) {
      return;
    }
    setError(null);
    setSaved(false);
    try {
      const result = await api.putVersioned<ManagerSettings>('/api/manager/settings', settings, etag);
      setSettings(result.data);
      setEtag(result.etag);
      setSaved(true);
    } catch (e) {
      setError(e);
    }
  }

  if (!settings) {
    return <ErrorBanner error={error} />;
  }

  return (
    <section>
      <h1>إعدادات العيادة</h1>
      <form className="card narrow" onSubmit={(e) => void submit(e)}>
        <ErrorBanner error={error} />
        {saved && <div className="ok">تم حفظ الإعدادات.</div>}
        <Field label="المنطقة الزمنية للعيادة (IANA)" field="clinicTimezone" error={error}>
          <input value={settings.clinicTimezone} onChange={(e) => setSettings({ ...settings, clinicTimezone: e.target.value })} required />
        </Field>
        <label className="field checkbox">
          <input
            type="checkbox"
            checked={settings.doctorDobVisible}
            onChange={(e) => setSettings({ ...settings, doctorDobVisible: e.target.checked })}
          />
          <span>إظهار تاريخ الميلاد للطبيب</span>
        </label>
        <Field label="اسم العيادة" field="clinicName" error={error}>
          <input value={settings.clinicName} onChange={(e) => setSettings({ ...settings, clinicName: e.target.value })} />
        </Field>
        <Field label="بيانات التواصل الخاصة بالعيادة" field="clinicContact" error={error}>
          <input value={settings.clinicContact} onChange={(e) => setSettings({ ...settings, clinicContact: e.target.value })} />
        </Field>
        <button type="submit">حفظ</button>
      </form>
    </section>
  );
}
