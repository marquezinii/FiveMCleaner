// Pure CSV serialization for the dashboard's "export CSV" feature. Columns
// come from the first row's keys, so it stays correct as new stat queries
// add or rename columns without needing this file to change.

export function toCsv(rows) {
  if (!rows || rows.length === 0) {
    return '';
  }

  const columns = Object.keys(rows[0]);
  const lines = [columns.join(',')];
  for (const row of rows) {
    lines.push(columns.map((column) => escapeCsvValue(row[column])).join(','));
  }

  return lines.join('\n');
}

function escapeCsvValue(value) {
  if (value === null || value === undefined) {
    return '';
  }

  const text = String(value);
  return /[",\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}
