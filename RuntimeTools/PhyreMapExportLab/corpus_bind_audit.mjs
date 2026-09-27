// corpus_bind_audit.mjs — Jarvis-MAP corpus texture-binding fidelity aggregator.
// Read-only: scans every map_*.material-slot-analysis.json on disk and sums
// per-map + corpus-wide submeshCount / boundSubmeshCount / status-family breakdown.
// No re-export, no ps3data read. Pure aggregation of existing link-dumps.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, basename } from 'node:path';

// Default = the shipped public/maps; override with `node corpus_bind_audit.mjs <maps-root>`
// (used to audit a temp re-export folder without touching the shipped corpus).
const MAPS_ROOT = process.argv[2] ?? join(process.cwd(), '..', 'FFXMapViewerWeb', 'public', 'maps');

// Walk for *.material-slot-analysis.json
function walk(dir, out) {
  for (const ent of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, ent.name);
    if (ent.isDirectory()) walk(p, out);
    else if (ent.isFile() && ent.name.endsWith('.material-slot-analysis.json')) out.push(p);
  }
  return out;
}

// Map a raw status string to a coarse FAMILY for the breakdown.
function family(status) {
  if (status.startsWith('bound_') && status.includes('field172')) return 'bound_base';                 // field172 base-color (the 14.7k bulk)
  if (status.startsWith('bound_') && status.includes('single_texture_import_link')) return 'bound_import_link'; // offset-348 single import link
  if (status.startsWith('bound_') && status.includes('lowest_offset_texture_import_link')) return 'bound_lowest_offset'; // multi-texture base = lowest field offset
  if (status.startsWith('bound_')) return 'bound_other';
  if (status.includes('missing_pparameterbuffer_texture_import_link')) return 'missing_texture_link';
  if (status.includes('missing_pmesh_to_pmaterial_link')) return 'missing_pmesh_material_link';
  if (status.includes('shared_data_id_unmapped')) return 'unmapped';
  if (status.includes('local_object_ref')) return 'local_object_ref';
  if (status.includes('import_path_not_dds')) return 'not_dds';
  if (status.includes('multiple_pparameterbuffer_texture_import_links')) return 'multiple_links';
  if (status.includes('import_unmatched_to_root_3d_texture')) return 'unmatched_import';
  return 'other:' + status;
}

const files = walk(MAPS_ROOT, []);
const corpus = { files: 0, submeshCount: 0, boundSubmeshCount: 0, rowsSeen: 0, boundRows: 0 };
const famTotals = {};       // family -> count (over rows)
const rawTotals = {};       // raw status -> count
const perMap = [];          // { map, submeshCount, boundSubmeshCount, bindPct, fams }

for (const f of files) {
  let j;
  try { j = JSON.parse(readFileSync(f, 'utf8')); }
  catch (e) { console.error('PARSE FAIL', f, e.message); continue; }
  corpus.files++;
  const sc = j.submeshCount ?? 0;
  const bsc = j.boundSubmeshCount ?? 0;
  corpus.submeshCount += sc;
  corpus.boundSubmeshCount += bsc;

  const fams = {};
  let rows = Array.isArray(j.rows) ? j.rows : [];
  let boundRowsHere = 0;
  for (const r of rows) {
    const st = r.status ?? 'NO_STATUS';
    rawTotals[st] = (rawTotals[st] || 0) + 1;
    const fam = family(st);
    famTotals[fam] = (famTotals[fam] || 0) + 1;
    fams[fam] = (fams[fam] || 0) + 1;
    corpus.rowsSeen++;
    if (st.startsWith('bound_')) { corpus.boundRows++; boundRowsHere++; }
  }

  perMap.push({
    map: basename(f).replace(/^map_/, '').replace(/\.material-slot-analysis\.json$/, ''),
    file: f,
    submeshCount: sc,
    boundSubmeshCount: bsc,
    bindPct: sc > 0 ? (bsc / sc) * 100 : null,
    rows: rows.length,
    boundRows: boundRowsHere,
    fams,
  });
}

const pct = (n, d) => d > 0 ? ((n / d) * 100).toFixed(2) + '%' : 'n/a';

console.log('=== CORPUS TEXTURE-BINDING FIDELITY (on-disk material-slot-analysis) ===');
console.log('files scanned          :', corpus.files);
console.log('total submeshCount     :', corpus.submeshCount);
console.log('total boundSubmeshCount:', corpus.boundSubmeshCount,
            '(' + pct(corpus.boundSubmeshCount, corpus.submeshCount) + ' of submeshCount header)');
console.log('total rows seen        :', corpus.rowsSeen);
console.log('rows with bound_ status:', corpus.boundRows,
            '(' + pct(corpus.boundRows, corpus.rowsSeen) + ' of rows)');

console.log('\n=== STATUS FAMILY DISTRIBUTION (over rows) ===');
const famSorted = Object.entries(famTotals).sort((a, b) => b[1] - a[1]);
for (const [fam, cnt] of famSorted) {
  console.log(String(cnt).padStart(7), pct(cnt, corpus.rowsSeen).padStart(8), ' ', fam);
}

const boundFams = famSorted.filter(([f]) => f.startsWith('bound_')).reduce((a, [, c]) => a + c, 0);
const failFams = corpus.rowsSeen - boundFams;
console.log('\n  BOUND families total :', boundFams, '(' + pct(boundFams, corpus.rowsSeen) + ')');
console.log('  FAILURE families tot :', failFams, '(' + pct(failFams, corpus.rowsSeen) + ')');

console.log('\n=== RAW STATUS DISTRIBUTION ===');
for (const [st, cnt] of Object.entries(rawTotals).sort((a, b) => b[1] - a[1])) {
  console.log(String(cnt).padStart(7), pct(cnt, corpus.rowsSeen).padStart(8), ' ', st);
}

// Worst maps by header bind% (require a meaningful submesh count to avoid tiny noise)
const eligible = perMap.filter(m => m.submeshCount >= 10 && m.bindPct !== null);
const worst = [...eligible].sort((a, b) => a.bindPct - b.bindPct).slice(0, 15);
console.log('\n=== TOP 15 WORST MAPS (lowest bind%, submeshCount>=10) ===');
console.log('bind%    bound/total  dominant-failure-family            map');
for (const m of worst) {
  const failFamsHere = Object.entries(m.fams).filter(([f]) => !f.startsWith('bound_')).sort((a, b) => b[1] - a[1]);
  const dom = failFamsHere.length ? `${failFamsHere[0][0]}(${failFamsHere[0][1]})` : '-';
  console.log(
    (m.bindPct.toFixed(1) + '%').padStart(7),
    (m.boundSubmeshCount + '/' + m.submeshCount).padStart(12),
    ' ', dom.padEnd(33), m.map);
}

// Fully-bound exemplars (100% header bind, submeshCount>=10), biggest first
const full = perMap.filter(m => m.bindPct === 100 && m.submeshCount >= 10)
  .sort((a, b) => b.submeshCount - a.submeshCount).slice(0, 12);
console.log('\n=== FULLY-BOUND EXEMPLARS (100% header bind, submeshCount>=10, biggest first) ===');
console.log('count of 100% maps (sc>=10):', perMap.filter(m => m.bindPct === 100 && m.submeshCount >= 10).length);
for (const m of full) {
  console.log((m.boundSubmeshCount + '/' + m.submeshCount).padStart(10), ' ', m.map);
}

// Header vs row reconciliation: does boundSubmeshCount track bound_ rows?
const mismatches = perMap.filter(m => m.boundSubmeshCount !== m.boundRows);
console.log('\n=== HEADER boundSubmeshCount vs bound_ row-count reconciliation ===');
console.log('maps where header.boundSubmeshCount != count(bound_ rows):', mismatches.length, 'of', perMap.length);
for (const m of mismatches.slice(0, 10)) {
  console.log('  ', m.map, 'header=', m.boundSubmeshCount, 'rowsBound=', m.boundRows, 'rows=', m.rows, 'sc=', m.submeshCount);
}

// Quartile-ish band distribution of maps by bind%
const bands = { '100%': 0, '90-99.99%': 0, '75-90%': 0, '50-75%': 0, '<50%': 0 };
for (const m of eligible) {
  if (m.bindPct === 100) bands['100%']++;
  else if (m.bindPct >= 90) bands['90-99.99%']++;
  else if (m.bindPct >= 75) bands['75-90%']++;
  else if (m.bindPct >= 50) bands['50-75%']++;
  else bands['<50%']++;
}
console.log('\n=== MAP BIND% BAND DISTRIBUTION (submeshCount>=10) ===');
console.log('eligible maps:', eligible.length, '| total maps:', perMap.length);
for (const [b, c] of Object.entries(bands)) console.log(b.padStart(10), ':', c);
