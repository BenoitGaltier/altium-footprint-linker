import test from 'node:test';
import assert from 'node:assert/strict';
import {escapeHtml, visibleRows, csvCell, exampleCsv} from '../src/FootprintLinker/Web/core.mjs';
test('untrusted CSV and component names are escaped for table rendering',()=>assert.equal(escapeHtml('<img src=x onerror="alert(1)"> & \'x\''),'&lt;img src=x onerror=&quot;alert(1)&quot;&gt; &amp; &#39;x&#39;'));
test('filters preserve AU_ID leading zeros and supplier searches',()=>{
 const rows=[{status:'ready',csv:{item:'00042',supplierName:'Acme'},componentName:'C1'},{status:'blocked',csv:{item:'42',footprint:'SOIC'}}];
 assert.equal(visibleRows(rows,'00042','all').length,1);assert.equal(visibleRows(rows,'acme','ready').length,1);assert.equal(visibleRows(rows,'SOIC','ready').length,0);
});
test('CSV exports neutralize formula prefixes and quote embedded separators',()=>{assert.equal(csvCell('=HYPERLINK("x")'),'"\'=HYPERLINK(""x"")"');assert.equal(csvCell('00042'),'"00042"');});
test('example uses the exact six required columns',()=>assert.equal(exampleCsv.split('\r\n')[0],'\uFEFFItem;Description;Supplier;Supplier Name;Supplier Item No;footprint'));
