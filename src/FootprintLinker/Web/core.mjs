export const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
export function visibleRows(rows, search, filter) {
  const query = search.trim().toLocaleLowerCase();
  return rows.filter(r => (filter === 'all' || r.status === filter) && (!query ||
    [r.csv.item, r.csv.description, r.csv.supplier, r.csv.supplierName, r.csv.supplierItemNo, r.csv.footprint, r.componentName, r.file?.relativePath, r.destination?.path].some(v => String(v ?? '').toLocaleLowerCase().includes(query))));
}
export function csvCell(value) { let text = String(value ?? ''); if (/^[\s]*[=+\-@\t\r]/.test(text)) text = "'" + text; return '"' + text.replaceAll('"','""') + '"'; }
export const exampleCsv = '\uFEFFItem;Description;Supplier;Supplier Name;Supplier Item No;footprint\r\nAU-0001;Example component;Supplier code;Example supplier;REF-001;SOT-23\r\n';
