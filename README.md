# Footprint Linker

Application Windows portable pour ajouter des **empreintes complémentaires** à des composants Altium 365, à partir d’un CSV et d’un répertoire de fichiers `.PcbLib`.

Elle reprend l’architecture de [Component Lens](https://github.com/BenoitGaltier/altium-component-lens) : exécutable autonome, interface locale dans le navigateur, URL de workspace et token API sauvegardés avec le chiffrement Windows.

## Utilisation

Téléchargez le ZIP de la [dernière version](https://github.com/BenoitGaltier/altium-footprint-linker/releases/latest), extrayez-le puis lancez **FootprintLinker.exe**. Windows 10/11 x64 et un navigateur moderne suffisent : aucun SDK .NET, Node.js ou Altium Designer à installer. Gardez la console ouverte pendant l’utilisation.

1. Connectez votre workspace avec son URL et un token API autorisé à lire les composants, importer des empreintes et modifier les associations.
2. Choisissez un CSV contenant les six colonnes ci-dessous. Leur ordre peut varier ; les colonnes supplémentaires sont acceptées et ignorées.
3. Configurez le **champ Altium correspondant à Item** (`AU_ID` par défaut). Cliquez sur **Parcourir…** pour sélectionner le répertoire racine des `.PcbLib` avec le sélecteur de dossier Windows. Le chemin peut également être saisi.
4. Cliquez sur **1. Analyser les composants**. Le tableau liste tous les composants trouvés, les empreintes locales et leurs chemins relatifs, ainsi que les dossiers de destination déduits.
5. Cochez les associations voulues puis cliquez sur **2. Ajouter aux composants sélectionnés**. Aucune ligne n’est présélectionnée. Le dialogue récapitule le workspace et le nombre d’associations.
6. Exportez le rapport JSON pour conserver les correspondances, les résultats et le journal.

Chaque empreinte locale reconnue possède un bouton **Voir 2D / 3D**. L’aperçu se calcule sur la machine, directement depuis le `.PcbLib`, sans lancer Altium et sans envoyer de fichier à un service de visualisation.

## Visualisation 2D et 3D

- **Vue 2D** : dessin vectoriel SVG des primitives de l’empreinte sélectionnée. Molette pour zoomer, glisser pour déplacer, **Recentrer** pour retrouver la vue initiale et **Exporter SVG** pour enregistrer le dessin.
- **Vue 3D** : pastilles, traits, perçages et modèles STEP embarqués, convertis localement en maillages. Glisser pour tourner, clic droit pour déplacer, molette pour zoomer. Le navigateur doit disposer de WebGL 2. Les corps extrudés définis par un contour et leurs hauteurs sont également affichés.
- La sélection utilise le nom interne demandé dans le CSV, y compris dans une bibliothèque à plusieurs empreintes. Le hash du fichier est revérifié avant le rendu, comme avant l’import.
- Les modèles externes absents, corps non pris en charge ou échecs de conversion sont signalés. Si aucun corps 3D exploitable n’existe, la vue 3D affiche les primitives disponibles de l’empreinte avec un avertissement. Aucun boîtier n’est inventé à partir du nom du composant.
- **Exporter GLB** est disponible pour la scène 3D convertie. Il est désactivé lorsqu’elle contient des corps extrudés ajoutés par le visualiseur, qui ne sont pas inclus dans ce fichier GLB.

La vue est un outil de contrôle visuel, pas une qualification des dimensions de fabrication. Les perçages sont représentés avec une épaisseur de support conventionnelle ; les textures externes et tous les types de corps ne sont pas pris en charge. Le rendu utilise AltiumSharp/OriginalCircuit et Three.js, embarqués dans l’exécutable ; il ne dépend pas d’Altium Designer.

```csv
Item;Description;Supplier;Supplier Name;Supplier Item No;footprint
00042;Example component;SUP;Example supplier;REF-001;SOT-23
```

Un [exemple CSV](examples/footprints.csv) est fourni, également téléchargeable depuis l’application. Les séparateurs virgule, point-virgule, tabulation et `|`, les champs entre guillemets, les retours à la ligne dans les champs, UTF-8 avec/sans BOM et Windows-1252 sont pris en charge. Les limites sont de 10 Mo et 50 000 lignes. Les guillemets ou nombres de colonnes incohérents sont refusés.

## Correspondances et import

- `Item` est comparé au **champ Altium configurable** (`AU_ID` par défaut), après suppression des espaces extérieurs. La comparaison des valeurs conserve la casse, la ponctuation et les zéros initiaux : `00042` reste différent de `42`. Le nom du champ est reconnu sans distinction de casse. Le réglage est sauvegardé avec les données chiffrées ; tout changement invalide l’analyse précédente.
- Les paramètres de révision priment sur les paramètres hérités de l’item, y compris lorsqu’une valeur de révision est vide. Plusieurs valeurs contradictoires ne sont pas utilisées comme identifiant.
- L’inventaire lit toutes les pages de la bibliothèque accessible au token, avec **la dernière révision** de chaque composant. Une même valeur peut correspondre à plusieurs composants : tous apparaissent, avec une ligne par association. Les composants trouvés restent affichés même si leur fichier d’empreinte manque ou si leur destination est ambiguë.
- Chaque paire `Item / footprint` répétée dans le CSV est signalée. Plusieurs empreintes différentes pour le même Item sont autorisées.
- Le répertoire choisi et **tous ses sous-dossiers** sont parcourus récursivement, sans suivre les jonctions/liens symboliques. L’application lit les **noms internes** des `.PcbLib` ; le nom du fichier peut être différent. La comparaison du nom d’empreinte ignore la casse et accepte le suffixe facultatif `.PcbLib` dans le CSV. La recherche récursive ne se sert pas des chemins locaux pour départager les homonymes : si le nom existe dans plusieurs fichiers, la ligne est bloquée. Le chemin relatif retenu est affiché dans le tableau.
- Une bibliothèque contenant plusieurs empreintes est réduite à l’empreinte sélectionnée dans une copie temporaire. Les primitives et modèles embarqués sont copiés comme données binaires ; les anciennes liaisons de workspace `ITEMGUID`, `REVISIONGUID`, `VAULTGUID` et `FOLDERGUID` sont neutralisées sans changer la taille des blocs. Les fichiers sources restent inchangés. Les modèles 3D embarqués sont conservés, mais les dépendances externes absentes du `.PcbLib` ne sont pas rassemblées.
- La **destination Altium est déduite par composant** : dossier de l’empreinte par défaut, sinon dossier commun aux autres empreintes existantes. Si aucun dossier d’empreinte n’est disponible, le classement du composant est comparé aux dossiers `PCB_COMPONENT_LIBRARY` par le plus long suffixe d’arborescence commun. Il faut un résultat unique ; les ambiguïtés ou absences de correspondance bloquent la ligne. Le tableau indique le chemin retenu et l’origine de la déduction. Ce classement Altium sert uniquement à la destination distante, pas au départage des fichiers locaux.
- Le schéma de nommage du dossier déduit, y compris hérité d’un parent, est utilisé lorsqu’il existe ; sinon le nom interne est proposé comme nom Altium. L’empreinte est importée une seule fois par combinaison workspace/dossier/empreinte interne/hash SHA-256. Deux composants ayant des destinations différentes produisent deux imports distincts. La destination et le champ configuré sont revérifiés avant l’écriture.
- Les associations déjà présentes sont ignorées. L’ajout utilise `desAddFootprintToComponent` sans `addAtIndex` : il se fait **en fin de liste**. Les associations et l’empreinte par défaut existantes sont vérifiées après l’écriture.
- `Description`, `Supplier`, `Supplier Name` et `Supplier Item No` servent au contrôle et restent dans le rapport ; ils ne modifient aucun paramètre du composant. L’opération ne crée pas de nouvelle révision du composant.

Une analyse incomplète, un répertoire partiellement illisible, un fichier ambigu ou un changement de fichier/composant est signalé. Les avertissements de couverture bloquent l’import. Le total annoncé par Altium ne limite pas la pagination ; un compteur inférieur au nombre retourné n’est accepté qu’après une deuxième lecture indépendante identique, comme dans Component Lens.

## Reprise après interruption

Chaque création et association est journalisée **avant** l’écriture distante. Les créations et associations ne sont jamais répétées automatiquement après une erreur réseau. Les lectures et les erreurs temporaires de lecture peuvent être réessayées.

Une création dont le résultat est incertain bloque la réimportation du même fichier. Dans **Données locales et journal de reprise**, renseignez la clé de l’opération et l’ID de l’empreinte après vérification directe dans Altium. L’application contrôle le nom/commentaire et le dossier de l’ID fourni. Si vous avez vérifié l’absence de création, saisissez `ABSENT` pour autoriser une nouvelle tentative. Pour une association incertaine, la présence de l’association est relue avant toute action.

**Arrêter** annule l’analyse ou arrête l’import entre deux lignes. Une écriture déjà engagée va jusqu’à sa vérification. Une coupure du processus ou du réseau peut laisser une empreinte créée mais non associée ; son ID reste dans le journal si la réponse a été reçue. Les opérations ne sont pas transactionnelles et il n’y a pas de suppression automatique ou de rollback distant.

## URL, tokens et données locales

Les mêmes formes d’URL que Component Lens sont acceptées : `workspace.365.altium.com`, son URL HTTPS ou son endpoint `/api/graphql`, sur les domaines `.altium.com` et `.altium365.com`. Un préfixe `Bearer` collé avec le token est accepté.

La connexion, le répertoire, le champ de correspondance et le journal sont enregistrés dans **`%LOCALAPPDATA%\FootprintLinker\state.dat`**, chiffré par Windows DPAPI pour l’utilisateur courant. Les écritures sont atomiques avec sauvegarde chiffrée `.bak` et verrou exclusif. Un fichier corrompu est conservé et signalé. Le token sauvegardé peut être réutilisé en laissant son champ vide, uniquement pour son URL d’origine. Un redémarrage restaure les réglages sans appeler Altium ; cliquez sur **Connecter** pour vérifier la connexion et charger les dossiers. La sauvegarde 1.0.0 reste compatible : les connexions et le journal sont conservés, le champ devient `AU_ID` et l’ancienne destination globale est ignorée.

Les tokens ne sont ni renvoyés par l’API locale, ni inclus dans les rapports, URLs, logs ou stockage navigateur. Le serveur écoute seulement sur `127.0.0.1`, sur un port aléatoire, avec une clé de session par processus, contrôle Host/Origin et Content Security Policy. Aucun CDN ni télémétrie. Les redirections HTTP sont désactivées pour les requêtes authentifiées.

**Effacer la connexion et le journal** supprime aussi la sauvegarde locale ; les données Altium restent en place. Conservez/exportez le journal avant de l’effacer : la déduplication des imports repose sur ce journal. Les empreintes déjà créées par un autre outil ne sont pas réutilisées sur la seule base d’un nom. Les résultats détaillés d’analyse/import restent en mémoire jusqu’à la fermeture et peuvent être exportés ; le journal persiste entre les lancements.

## Développement

Prérequis de développement : .NET SDK 10, Node.js 22+ et Git. Les dépendances de rendu sont figées dans les sous-modules Git et `package-lock.json` ; elles sont embarquées dans le ZIP portable. La lecture/extraction pour l’import utilise Windows Structured Storage. Les licences sont regroupées dans [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

```powershell
git -c core.longpaths=true clone --recurse-submodules https://github.com/BenoitGaltier/altium-footprint-linker.git
cd altium-footprint-linker
npm ci
npm run vendor
./scripts/test.ps1
dotnet run --project src/FootprintLinker
./scripts/publish.ps1
./scripts/smoke.ps1
```

Dans une copie existante : `git -c core.longpaths=true submodule update --init --recursive`, puis `npm ci`. Le Source Link fourni par le SDK .NET 10 remplace la référence explicite obsolète du projet amont ; les audits de dépendances restent actifs.

L’export local peut aussi fonctionner sans token de workspace :

```powershell
./FootprintLinker.exe --render-preview C:\Libraries\package.PcbLib SOT-23 C:\Previews\SOT23
```

Cette commande produit `preview.svg`, `preview.glb` et les métadonnées dans `preview.json`.

Options : `--headless`, `--port 4178`, `--data-dir PATH`, `--no-persistence`. Ce dernier mode permet l’analyse mais bloque les imports car ils requièrent un journal durable. Les tests utilisent des dossiers isolés et un serveur Altium simulé ; ils ne contactent ni ne modifient un workspace réel.

Le ZIP et son SHA-256 sont produits dans `dist/`. Les tags `v*` déclenchent les tests, la compilation autonome, le test du binaire puis la création d’une release GitHub. Le binaire n’est pas signé ; le runtime embarqué peut extraire des fichiers natifs dans le répertoire temporaire Windows au premier lancement.

Voir [la validation](docs/VALIDATION.md) pour la couverture et les limites. Un essai d’import sur un workspace de test reste nécessaire pour valider les droits, schémas de nommage et fichiers réels propres à votre environnement. Évitez les modifications simultanées de la bibliothèque pendant l’opération : l’API ne fournit pas de transaction atomique entre lecture et ajout.

## Références

- [API Altium 365 et authentification](https://www.altium.com/documentation/altium-developer-center/altium-365/api)
- [Ajout d’une empreinte à un composant](https://docs.developer.altium.com/platform-api-docs/reference/library-management/operations/mutations/des-add-footprint-to-component/)
- [Ordre d’ajout et conservation de l’empreinte par défaut](https://docs.developer.altium.com/platform-api-docs/reference/library-management/types/inputs/des-add-footprint-to-component-input/)
- [Création d’une empreinte](https://docs.developer.altium.com/platform-api-docs/reference/library-management/types/inputs/des-create-footprint-input/)
- [Chiffrement Windows DPAPI](https://learn.microsoft.com/en-us/windows/win32/secauthn/data-protection)

Application indépendante, développée à partir de Component Lens. Ce projet n’est pas un produit Altium.
