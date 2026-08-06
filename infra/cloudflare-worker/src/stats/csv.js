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
  // Leading '=', '+', '-', '@' (plus tab and CR, which Excel also treats as
  // a formula start) would be interpreted by spreadsheet software as a
  // formula when the exported CSV is opened -- telemetry fields like CPU/GPU
  // model or error category are device/user-controlled, so a malicious value
  // could run `=HYPERLINK(...)` or `=cmd(...)` on the admin's machine.
  // Neutralizing means quoting the cell AND prefixing a single quote, the
  // spreadsheet convention for "treat as text".
  const dangerousLeading = /^[=+\-@\t\r]/.test(text)
    || text.startsWith('\uFF1D')  // fullwidth equals sign
    || text.startsWith('\uFF0B')  // fullwidth plus sign
    || text.startsWith('\uFF03')   // fullwidth number sign
    || text.startsWith('|');       // pipe (LibreOffice formula)
  const needsQuotes = /[",\n]/.test(text) || dangerousLeading;
  return needsQuotes ? `"${dangerousLeading ? "'" : ''}${text.replaceAll('"', '""')}"` : text;
}
