import { apiGetFile } from './client';

/**
 * CSV export (L5) — one authenticated blob fetch for every « Exporter » in the product.
 *
 * <p><b>Why one function and not one per resource.</b> Eight lists export, and the <i>route</i> and the
 * <i>filters</i> are the caller's business; everything else is the same every time.</p>
 *
 * <p>⚠️ <b>The filename comes from the server</b>, read out of `Content-Disposition` by `client.ts`. The server
 * already dates it with the clinic's own day (`patients-2026-08-03.csv`), and re-deriving a name here would be a
 * second authority on it — including a second chance to use the browser's UTC date, which for the first hour of
 * every Tunisian day would name the file after yesterday. The `export.csv` fallback is this module's, not
 * `client.ts`'s: only a CSV export knows what an unnamed download of its own should be called.</p>
 */
export async function fetchExportCsv(
  path: string,
  params: Record<string, string | number | boolean | undefined | null> = {},
): Promise<{ blob: Blob; filename: string }> {
  // `''` is dropped alongside null/undefined — an empty filter is not a filter, and `buildUrl` only skips the
  // latter two.
  const query = Object.fromEntries(
    Object.entries(params).filter(([, value]) => value !== undefined && value !== null && value !== ''),
  );

  const { blob, filename } = await apiGetFile(path, query);
  return { blob, filename: filename ?? 'export.csv' };
}
