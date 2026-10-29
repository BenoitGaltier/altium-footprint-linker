import {escapeHtml as e, visibleRows, exampleCsv} from './core.mjs';
const $ = id => document.getElementById(id);
let key = new URLSearchParams(location.hash.slice(1)).get('session') || sessionStorage.getItem('linkerSession');
if (key) sessionStorage.setItem('linkerSession', key);
history.replaceState(null, '', location.pathname);
let plan = null, reportPlan = null, latest = null, busy = false, connected = false, selected = new Set(), runningJob = null;
const messages = {
  invalidEndpoint:'URL invalide : utilisez une URL HTTPS de workspace Altium.', invalidToken:'Saisissez un token API valide.',
  savedTokenMismatch:'Le token sauvegardé appartient à une autre URL. Saisissez le token de ce workspace.',
  apiAuth:'Accès refusé ou token expiré. Vérifiez ses droits dans ce workspace.', apiGraphql:'Altium a refusé la requête. Vérifiez les droits et la compatibilité de l’API.',
  apiNetwork:'Connexion à Altium indisponible.', apiTimeout:'Altium n’a pas répondu à temps.', apiFormat:'Réponse Altium inattendue.',
  csvHeaders:'En-têtes requis : Item, Description, Supplier, Supplier Name, Supplier Item No, footprint.',
  csvColumns:'Le nombre de colonnes du CSV est incohérent.', csvQuotes:'Les guillemets du CSV sont mal fermés.', csvEmpty:'Le CSV ne contient aucune ligne de données.', csvTooLarge:'CSV trop volumineux (10 Mo / 50 000 lignes maximum).',
  connectFirst:'Connectez le workspace avant de lancer l’analyse.', selectFolder:'Choisissez un dossier d’empreintes dans le workspace.',
  invalidMatchField:'Saisissez le nom du champ Altium à comparer à Item (128 caractères maximum).',
  pickerPlatform:'Le sélecteur de répertoire nécessite Windows.', pickerBusy:'Un sélecteur de répertoire est déjà ouvert.', pickerFailed:'Le sélecteur Windows n’a pas pu être ouvert. Vous pouvez saisir le chemin du répertoire.',
  destinationNotFound:'Impossible de déduire un dossier d’empreintes pour ce composant.', destinationAmbiguous:'Plusieurs dossiers de destination sont possibles pour ce composant.', destinationChanged:'Le dossier déduit du composant a changé. Relancez l’analyse.',
  defaultFootprint:'Dossier de l’empreinte par défaut', existingFootprints:'Dossier des empreintes existantes', componentFolder:'Classement du composant',
  directoryMissing:'Le répertoire local est introuvable.', directoryUnreadable:'Le répertoire ou un sous-répertoire est inaccessible.', directoryTooLarge:'Le répertoire contient trop de fichiers.',
  emptyItem:'Item vide', emptyFootprint:'Nom d’empreinte vide', duplicateCsvRow:'Paire Item / footprint répétée dans le CSV',
  fileNotFound:'Nom interne introuvable dans les .PcbLib de l’arborescence', ambiguousFile:'Ce nom figure dans plusieurs fichiers de l’arborescence : gardez une source unique', componentNotFound:'Aucun composant ne correspond à Item dans le champ configuré',
  componentChanged:'Le composant a changé depuis l’analyse. Relancez l’analyse.', fileChanged:'Le fichier a changé depuis l’analyse. Relancez l’analyse.',
  planExpired:'Le tableau doit être actualisé. Relancez une analyse.', partialPlan:'L’analyse contient des avertissements. Résolvez-les avant l’import.',
  creationOutcomeUnknown:'Création précédente incertaine. Vérifiez Altium et renseignez le journal de reprise.', linkOutcomeUnknown:'Association précédente incertaine. Vérifiez Altium puis renseignez le journal.',
  verificationFailed:'L’écriture n’a pas pu être vérifiée. Consultez le workspace avant toute reprise.', operationInterrupted:'Opération interrompue : son résultat distant est incertain.',
  apiMutationRejected:'Altium a rejeté la modification. Vérifiez les droits d’écriture, le dossier et ses schémas de nommage.', uploadFailed:'Échec de l’envoi du fichier vers Altium.',
  persistenceRequired:'L’import requiert la sauvegarde chiffrée du journal. Relancez sans --no-persistence.',
  storageInUse:'Une autre instance utilise ce dossier de données.', storageRead:'La sauvegarde chiffrée est illisible. Elle est conservée.', storageCrypto:'Échec du chiffrement Windows. Les données existantes sont conservées.', storageWrite:'Impossible d’enregistrer le journal chiffré.',
  recoveryMismatch:'L’empreinte ne correspond pas au nom ou au dossier attendu.', recoveryInvalid:'La clé du journal n’est pas une opération incertaine.',
  indexing:'Indexation des bibliothèques de toute l’arborescence locale', listing:'Lecture de l’inventaire Altium', verifying:'Vérification de l’inventaire', details:'Lecture du champ de correspondance', matching:'Vérification des composants et déduction des destinations', importing:'Import et vérification des associations', finished:'Traitement terminé', starting:'Démarrage',
  ready:'Prête', blocked:'À vérifier', alreadyLinked:'Déjà associée', imported:'Ajout vérifié', failed:'Échec', unknown:'Résultat incertain', cancelled:'Traitement arrêté',
  unreadParameters:'Certains paramètres n’ont pas pu être lus. L’import est bloqué tant que la lecture n’est pas complète.', reportedCountMismatch:'Le nombre de composants diffère du total annoncé par Altium.', reportedTotalChanged:'Le total Altium a changé pendant la lecture.', componentChangedDuringScan:'Un composant a changé pendant la lecture.',
  analysisFailed:'L’analyse a échoué.', importFailed:'L’import a échoué. Consultez le journal de reprise.', jobRunning:'Un traitement est déjà en cours.'
};
const message = code => messages[code] || code || 'Erreur inattendue';
function notice(text, error = false) { $('notice').textContent = text; $('notice').className = 'notice' + (error ? ' error' : ''); $('notice').hidden = !text; }
async function api(path, method = 'GET', body) {
  const response = await fetch('/api/' + path, {method, headers:{'X-Linker-Session':key || '', ...(body === undefined ? {} : {'Content-Type':'application/json'})}, body:body === undefined ? undefined : JSON.stringify(body)});
  const text = await response.text(); let data;
  try { data = text ? JSON.parse(text) : {}; } catch { throw Error('Réponse locale illisible.'); }
  if (!response.ok) throw Error(response.status === 403 ? 'Ouvrez l’URL complète affichée dans la console de Footprint Linker.' : message(data.error) + (data.detail ? ' (' + data.detail + ')' : ''));
  return data;
}
function controls() {
  $('analyze').disabled = busy || !connected || !$('csv').files.length || !$('directory').value.trim() || !$('matchField').value.trim();
  $('connect').disabled = busy; $('csv').disabled = busy; $('encoding').disabled = busy; $('endpoint').disabled = busy;
  $('name').disabled = busy; $('token').disabled = busy; $('matchField').disabled = busy; $('directory').disabled = busy; $('browse').disabled = busy;
  $('forget').disabled = busy; $('cancel').hidden = !runningJob; $('apply').disabled = busy || !plan || !selected.size || plan.warnings.length > 0;
  $('selectAll').disabled = busy || !plan;
  for (const input of $('rows').querySelectorAll('input[data-id]')) input.disabled = busy || plan?.rows.find(r => r.id === input.dataset.id)?.status !== 'ready';
}
function fieldLabels(field = $('matchField').value.trim() || 'AU_ID') {
  $('matchFieldDisplay').textContent = field; $('matchFieldFlow').textContent = field; $('matchFieldHeader').textContent = 'Item / ' + field;
}
function invalidate() { plan = null; selected.clear(); $('results').hidden = true; controls(); }
function download(name, content, type = 'application/json') {
  const url = URL.createObjectURL(new Blob([content], {type})); const link = document.createElement('a'); link.href = url; link.download = name; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
}
async function refresh() {
  const state = await api('session');
  connected = Boolean(state.connected);
  if (state.connection) {
    $('name').value = state.connection.name; $('endpoint').value = state.connection.endpoint;
    $('connected').textContent = connected ? 'Connecté · ' + state.connection.componentCount : 'Connexion sauvegardée';
    $('savedHint').textContent = 'Token sauvegardé et chiffré. Laissez vide pour le réutiliser avec cette URL.';
  }
  if (state.directory && !$('directory').value) $('directory').value = state.directory;
  $('matchField').value = state.matchField || 'AU_ID'; fieldLabels();
  $('storagePath').textContent = state.dataDirectory || 'Mode sans sauvegarde : l’import est désactivé.';
  $('journal').textContent = JSON.stringify(state.journal, null, 2);
  if (state.storageError) notice(message(state.storageError), true);
  if (state.connection && !connected) notice('Connexion sauvegardée. Cliquez sur « Connecter le workspace » pour vérifier le token.');
  controls(); return state;
}
function render() {
  if (!plan) return;
  const visible = visibleRows(plan.rows, $('search').value, $('filter').value);
  $('rows').innerHTML = visible.map(r => `<tr><td><input type="checkbox" data-id="${e(r.id)}" ${selected.has(r.id) ? 'checked' : ''} ${r.status === 'ready' ? '' : 'disabled'} aria-label="Sélectionner ${e(r.csv.item)} ${e(r.componentName)}"></td><td><strong>${e(r.csv.item || '—')}</strong><small>Ligne ${r.csv.line} · ${e(r.csv.description)}</small><small>${e(r.csv.supplierName)} ${e(r.csv.supplierItemNo)}</small></td><td>${e(r.componentName || '—')}<small>${e(r.revision)} ${r.matches > 1 ? ' · ' + e(plan.matchField) + ' partagé par ' + r.matches + ' composants' : ''}</small><small>Actuelles : ${e(r.existingFootprints.join(', ') || '—')}</small></td><td>${e(r.internalName || r.csv.footprint || '—')}<small>${e(r.file?.relativePath || '')}</small></td><td>${e(r.destination?.path || '—')}<small>${e(r.destinationSource ? message(r.destinationSource) : '')}</small></td><td><span class="status ${e(r.status)}">${e(message(r.status))}</span>${r.reason ? '<small>' + e(message(r.reason)) + '</small>' : ''}</td></tr>`).join('') || '<tr><td colspan="6">Aucune correspondance pour ce filtre.</td></tr>';
  $('total').textContent = new Set(plan.rows.filter(r => r.componentId).map(r => r.componentId)).size;
  for (const [id,status] of [['ready','ready'],['linked','alreadyLinked'],['blocked','blocked']]) $(''+id).textContent = plan.rows.filter(r => r.status === status).length;
  $('selection').textContent = selected.size + ' association(s) sélectionnée(s)';
  $('selectAll').checked = visible.some(r => r.status === 'ready') && visible.filter(r => r.status === 'ready').every(r => selected.has(r.id));
  $('warnings').hidden = !plan.warnings.length; $('warnings').textContent = plan.warnings.map(message).join('\n');
  $('planDate').textContent = 'Analyse du ' + new Date(plan.createdAt).toLocaleString('fr-FR') + ' · ' + plan.diagnostics.distinctComponents + ' composants lus · ' + plan.diagnostics.pages + ' page(s)';
  fieldLabels(plan.matchField); $('results').hidden = false; controls();
}
async function poll(job) {
  runningJob = job.id; busy = true; controls(); $('progressSection').hidden = false;
  while (true) {
    const current = await api('jobs/' + job.id);
    $('phase').textContent = message(current.phase); $('progressText').textContent = current.total ? current.loaded + ' / ' + current.total : '';
    if (current.total) { $('progress').max = current.total; $('progress').value = current.loaded; } else $('progress').removeAttribute('value');
    if (current.status !== 'running') {
      latest = current; $('export').disabled = false;
      if (current.status !== 'completed') throw Error(message(current.error));
      if (current.kind === 'analyze') {
        plan = current.result; reportPlan = plan; selected = new Set();
        $('outcome').hidden = true; render(); notice('Analyse terminée. Vérifiez les correspondances et sélectionnez les lignes à importer.');
      } else {
        const result = current.result; reportPlan = result.plan || plan; $('outcome').hidden = false;
        $('outcomeSummary').textContent = result.results.filter(r => ['imported','alreadyLinked'].includes(r.status)).length + ' association(s) vérifiée(s), ' + result.results.filter(r => ['failed','unknown'].includes(r.status)).length + ' échec(s)' + (result.cancelled ? ' · Traitement arrêté avant la fin.' : '.');
        $('outcomeRows').innerHTML = result.results.map(r => { const source = reportPlan?.rows.find(row => row.id === r.rowId); return `<p><span class="status ${e(r.status)}">${e(message(r.status))}</span> ${e(source?.csv.item)} → ${e(source?.componentName)} · ${e(source?.internalName)} ${r.error ? ' · ' + e(message(r.error)) : ''}${r.footprintId ? '<small> · ID ' + e(r.footprintId) + '</small>' : ''}</p>`; }).join('');
        selected.clear(); plan = null; $('apply').disabled = true; $('results').hidden = true;
        notice('Traitement terminé. Le rapport et le journal de reprise sont disponibles.');
      }
      return;
    }
    await new Promise(resolve => setTimeout(resolve, 800));
  }
}
async function run(task) { try { await task(); } catch (error) { notice(error.message, true); } finally { busy = false; runningJob = null; $('progressSection').hidden = true; controls(); } }
$('connection').addEventListener('submit', event => { event.preventDefault(); run(async () => { busy = true; controls(); notice('Connexion au workspace…'); await api('connect', 'POST', {name:$('name').value, endpoint:$('endpoint').value, token:$('token').value}); plan = null; $('results').hidden = true; await refresh(); notice('Workspace connecté. Choisissez le CSV, le champ de correspondance et le répertoire racine des empreintes.'); }); $('token').value = ''; });
for (const id of ['csv','encoding','directory','endpoint']) $(id).addEventListener('change', invalidate);
$('directory').addEventListener('input', invalidate);
$('matchField').addEventListener('input', () => { invalidate(); fieldLabels(); });
$('matchField').addEventListener('change', () => {
  const field = $('matchField').value.trim();
  api('preferences','POST',{matchField:field}).then(() => { if ($('matchField').value.trim() === field) { $('matchField').value = field; fieldLabels(); } }).catch(error => notice(error.message,true));
});
$('browse').addEventListener('click', () => run(async () => {
  busy = true; controls(); const picked = await api('directory/pick','POST',{directory:$('directory').value});
  if (!picked.cancelled && picked.directory) { $('directory').value = picked.directory; invalidate(); notice('Répertoire sélectionné. L’analyse parcourra tous ses sous-dossiers.'); }
}));
$('csv').addEventListener('change', () => { $('csvName').textContent = $('csv').files[0]?.name || 'Choisir un fichier CSV'; });
$('analyze').addEventListener('click', () => run(async () => {
  const file = $('csv').files[0]; if (!file) return;
  busy = true; controls();
  if (file.size > 10_000_000) throw Error(message('csvTooLarge'));
  const text = new TextDecoder($('encoding').value, {fatal:true}).decode(await file.arrayBuffer());
  plan = null; selected.clear(); $('results').hidden = true;
  busy = true; controls(); await poll(await api('analyze', 'POST', {csv:text, directory:$('directory').value, matchField:$('matchField').value}));
}));
$('rows').addEventListener('change', event => { const id = event.target.dataset.id; if (!id) return; if (event.target.checked) selected.add(id); else selected.delete(id); render(); });
$('selectAll').addEventListener('change', () => { for (const row of visibleRows(plan.rows, $('search').value, $('filter').value).filter(r => r.status === 'ready')) { if ($('selectAll').checked) selected.add(row.id); else selected.delete(row.id); } render(); });
for (const id of ['search','filter']) $(id).addEventListener('input', render);
$('apply').addEventListener('click', () => { $('confirmText').textContent = selected.size + ' association(s) sélectionnée(s) dans ' + new URL(plan.endpoint).host + '.'; $('confirm').showModal(); });
$('dismiss').addEventListener('click', () => $('confirm').close());
$('confirmApply').addEventListener('click', () => { $('confirm').close(); run(async () => { const source = plan; busy = true; controls(); await poll(await api('apply','POST',{planId:source.id,rowIds:[...selected]})); await refresh(); }); });
$('cancel').addEventListener('click', () => { if (runningJob) api('jobs/' + runningJob + '/cancel','POST',{}).catch(error => notice(error.message,true)); });
$('export').addEventListener('click', async () => { try { const state = await api('session'); download('FootprintLinker-report.json', JSON.stringify({exportedAt:new Date().toISOString(), plan:reportPlan, job:latest, journal:state.journal},null,2)); } catch(error) { notice(error.message,true); } });
$('example').addEventListener('click', event => { event.preventDefault(); download('footprints-example.csv',exampleCsv,'text/csv;charset=utf-8'); });
$('forget').addEventListener('click', () => { if (confirm('Effacer le token et tout le journal de reprise ? Les empreintes déjà importées dans Altium restent en place. Sans ce journal, une réimportation peut créer des doublons.')) run(async () => { await api('local-data','DELETE'); location.reload(); }); });
$('recovery').addEventListener('submit', event => { event.preventDefault(); if (confirm('Confirmez-vous avoir vérifié cette opération directement dans Altium ?')) run(async () => { await api('recover','POST',{key:$('recoveryKey').value.trim(),footprintId:$('recoveryId').value.trim()}); await refresh(); notice('Vérification enregistrée. Relancez une analyse.'); }); });
$('quit').addEventListener('click', () => { if (busy && !confirm('Un traitement est en cours. Fermer peut laisser un résultat distant incertain. Quitter ?')) return; api('quit','POST',{}).then(() => { busy = true; controls(); $('cancel').hidden = true; notice('Application arrêtée. Vous pouvez fermer cet onglet.'); }).catch(error => notice(error.message,true)); });
refresh().then(state => { if (state.job && ['running','completed'].includes(state.job.status)) run(() => poll(state.job)); }).catch(error => notice(error.message,true));
