// Clinic-time helpers (FR-OPS-003). Instants travel as ISO-8601 UTC; every date / time the user sees or types is in the
// clinic timezone from /api/me, never the browser's. Intl only — no date library, no floating point money.

const fallbackZone = 'Africa/Cairo';

function zoneOrDefault(zone: string | null | undefined): string {
  return zone || fallbackZone;
}

/** Offset (ms) of the zone at the given instant: wall-clock time read in the zone minus the UTC time. */
function offsetAt(instant: number, zone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: zone, hourCycle: 'h23', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit',
  }).formatToParts(new Date(instant));
  const get = (type: string) => Number(parts.find((p) => p.type === type)?.value ?? '0');
  const wall = Date.UTC(get('year'), get('month') - 1, get('day'), get('hour'), get('minute'), get('second'));
  return wall - Math.floor(instant / 1000) * 1000;
}

/** "2026-11-02T09:30" (clinic wall time) → ISO instant. Correct across DST changes (two-pass offset). */
export function clinicLocalToIso(local: string, zone: string | null | undefined): string {
  const z = zoneOrDefault(zone);
  const [datePart = '', timePart = '00:00'] = local.split('T');
  const [y = 0, m = 1, d = 1] = datePart.split('-').map(Number);
  const [hh = 0, mm = 0] = timePart.split(':').map(Number);
  const wall = Date.UTC(y, m - 1, d, hh, mm);
  let instant = wall - offsetAt(wall, z);
  instant = wall - offsetAt(instant, z);
  return new Date(instant).toISOString();
}

/** ISO instant → "YYYY-MM-DDTHH:mm" in the clinic zone (for datetime-local inputs). */
export function isoToClinicLocal(iso: string, zone: string | null | undefined): string {
  const z = zoneOrDefault(zone);
  const instant = Date.parse(iso);
  const shifted = new Date(instant + offsetAt(instant, z));
  return shifted.toISOString().slice(0, 16);
}

export function clinicToday(zone: string | null | undefined): string {
  return isoToClinicLocal(new Date().toISOString(), zone).slice(0, 10);
}

export function addDays(date: string, days: number): string {
  const [y = 0, m = 1, d = 1] = date.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

/** Start (ISO) of a clinic-local calendar day; used to build half-open UTC ranges for list filters. */
export function clinicDayStartIso(date: string, zone: string | null | undefined): string {
  return clinicLocalToIso(`${date}T00:00`, zone);
}

export function formatClinicDateTime(iso: string | null | undefined, zone: string | null | undefined): string {
  if (!iso) {
    return '—';
  }
  return new Date(iso).toLocaleString('ar-EG', { timeZone: zoneOrDefault(zone), dateStyle: 'medium', timeStyle: 'short' });
}

export function formatClinicTime(iso: string | null | undefined, zone: string | null | undefined): string {
  if (!iso) {
    return '—';
  }
  return new Date(iso).toLocaleTimeString('ar-EG', { timeZone: zoneOrDefault(zone), hour: '2-digit', minute: '2-digit' });
}

export function formatDate(date: string): string {
  const [y = 0, m = 1, d = 1] = date.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d)).toLocaleDateString('ar-EG', { timeZone: 'UTC', weekday: 'long', day: 'numeric', month: 'long' });
}
