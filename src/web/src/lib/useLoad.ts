import { useCallback, useEffect, useState } from 'react';
import { api } from '../api/client';

/** Loads a resource; data lives only in component memory (no browser storage, no persistent cache). */
export function useLoad<T>(url: string) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<unknown>(null);

  const reload = useCallback(async () => {
    try {
      setError(null);
      setData(await api.get<T>(url));
    } catch (e) {
      setError(e);
    }
  }, [url]);

  useEffect(() => {
    void reload();
  }, [reload]);

  return { data, error, reload };
}
