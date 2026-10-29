# Footprint Linker

Application Windows portable pour ajouter des **empreintes complémentaires** à des composants Altium 365, à partir d’un CSV et d’un répertoire de fichiers `.PcbLib`.

Elle reprend l’architecture de [Component Lens](https://github.com/BenoitGaltier/altium-component-lens) : exécutable autonome, interface locale dans le navigateur, URL de workspace et token API sauvegardés avec le chiffrement Windows.

## Utilisation

Téléchargez le ZIP de la [dernière version](https://github.com/BenoitGaltier/altium-footprint-linker/releases/latest), extrayez-le puis lancez **FootprintLinker.exe**. Windows 10/11 x64 et un navigateur moderne suffisent : aucun SDK .NET, Node.js ou Altium Designer à installer. Gardez la console ouverte pendant l’utilisation.

1. Connectez votre workspace avec son URL et un token API autorisé à lire les composants, importer des empreintes et modifier les associations.
2. Choisissez un CSV contenant les six colonnes ci-dessous. Leur ordre peut varier ; les colonnes supplémentaires sont acceptées et ignorées.
3. Saisissez le chemin du répertoire local contenant les `.PcbLib`, puis choisissez le dossier d’empreintes de destination dans le workspace.
4. Lancez **Analyser les correspondances** et vérifiez le tableau.
5. Sélectionnez les lignes voulues puis **Importer et ajouter les empreintes**. Le dialogue récapitule le workspace et le nombre d’associations.
6. Exportez le rapport JSON pour conserver les correspondances, les résultats et le journal.

```csv
Item;Description;Supplier;Supplier Name;Supplier Item No;footprint
00042;Example component;SUP;Example supplier;REF-001;SOT-23
```

Un [exemple CSV](examples/footprints.csv) est fourni, également téléchargeable depuis l’application. Les séparateurs virgule, point-virgule, tabulation et `|`, les champs entre guillemets, les retours à la ligne dans les champs, UTF-8 avec/sans BOM et Windows-1252 sont pris en charge. Les limites sont de 10 Mo et 50 000 lignes. Les guillemets ou nombres de colonnes incohérents sont refusés.

## Correspondances et import

- `Item` est comparé au paramètre **AU_ID** des composants, après suppression des espaces extérieurs. La comparaison conserve la casse, la ponctuation et les zéros initiaux : `00042` reste différent de `42`. Le nom du champ `AU_ID` est reconnu sans distinction de casse.
- Les paramètres de révision priment sur les paramètres hérités de l’item, y compris lorsqu’un AU_ID de révision est vide. Plusieurs valeurs contradictoires ne sont pas utilisées comme identifiant.
- L’inventaire lit toutes les pages de la bibliothèque accessible au token, avec **la dernière révision** de chaque composant. Une même valeur AU_ID peut correspondre à plusieurs composants : tous apparaissent, avec une ligne par association. Ces correspondances multiples ne sont pas présélectionnées.
- Chaque paire `Item / footprint` répétée dans le CSV est signalée. Plusieurs empreintes différentes pour le même Item sont autorisées.
- Le répertoire est parcouru récursivement, sans suivre les jonctions/liens symboliques. L’application lit les **noms internes** des `.PcbLib` ; le nom du fichier peut être différent. La comparaison du nom d’empreinte ignore la casse et accepte le suffixe facultatif `.PcbLib` dans le CSV. Si le nom existe dans plusieurs fichiers, la ligne est bloquée.
- Une bibliothèque contenant plusieurs empreintes est réduite à l’empreinte sélectionnée dans une copie temporaire. Les primitives et modèles embarqués sont copiés comme données binaires ; les anciennes liaisons de workspace `ITEMGUID`, `REVISIONGUID`, `VAULTGUID` et `FOLDERGUID` sont neutralisées sans changer la taille des blocs. Les fichiers sources restent inchangés. Les modèles 3D embarqués sont conservés, mais les dépendances externes absentes du `.PcbLib` ne sont pas rassemblées.
- Le dossier Altium de destination est explicitement choisi parmi les dossiers de type `PCB_COMPONENT_LIBRARY`. Son schéma de nommage, y compris hérité d’un dossier parent, est utilisé lorsqu’il existe ; sinon le nom interne est proposé comme nom Altium. L’empreinte est importée une seule fois par combinaison workspace/dossier/empreinte interne/hash SHA-256, puis réutilisée pour les autres composants du lot et lors des reprises avec le même journal.
- Les associations déjà présentes sont ignorées. L’ajout utilise `desAddFootprintToComponent` sans `addAtIndex` : il se fait **en fin de liste**. Les associations et l’empreinte par défaut existantes sont vérifiées après l’écriture.
- `Description`, `Supplier`, `Supplier Name` et `Supplier Item No` servent au contrôle et restent dans le rapport ; ils ne modifient aucun paramètre du composant. L’opération ne crée pas de nouvelle révision du composant.

Une analyse incomplète, un répertoire partiellement illisible, un fichier ambigu ou un changement de fichier/composant est signalé. Les avertissements de couverture bloquent l’import. Le total annoncé par Altium ne limite pas la pagination ; un compteur inférieur au nombre retourné n’est accepté qu’après une deuxième lecture indépendante identique, comme dans Component Lens.

## Reprise après interruption

Chaque création et association est journalisée **avant** l’écriture distante. Les créations et associations ne sont jamais répétées automatiquement après une erreur réseau. Les lectures et les erreurs temporaires de lecture peuvent être réessayées.

Une création dont le résultat est incertain bloque la réimportation du même fichier. Dans **Données locales et journal de reprise**, renseignez la clé de l’opération et l’ID de l’empreinte après vérification directe dans Altium. L’application contrôle le nom/commentaire et le dossier de l’ID fourni. Si vous avez vérifié l’absence de création, saisissez `ABSENT` pour autoriser une nouvelle tentative. Pour une association incertaine, la présence de l’association est relue avant toute action.

**Arrêter** annule l’analyse ou arrête l’import entre deux lignes. Une écriture déjà engagée va jusqu’à sa vérification. Une coupure du processus ou du réseau peut laisser une empreinte créée mais non associée ; son ID reste dans le journal si la réponse a été reçue. Les opérations ne sont pas transactionnelles et il n’y a pas de suppression automatique ou de rollback distant.

## URL, tokens et données locales

Les mêmes formes d’URL que Component Lens sont acceptées : `workspace.365.altium.com`, son URL HTTPS ou son endpoint `/api/graphql`, sur les domaines `.altium.com` et `.altium365.com`. Un préfixe `Bearer` collé avec le token est accepté.

La connexion, le répertoire, le dossier choisi et le journal sont enregistrés dans **`%LOCALAPPDATA%\FootprintLinker\state.dat`**, chiffré par Windows DPAPI pour l’utilisateur courant. Les écritures sont atomiques avec sauvegarde chiffrée `.bak` et verrou exclusif. Un fichier corrompu est conservé et signalé. Le token sauvegardé peut être réutilisé en laissant son champ vide, uniquement pour son URL d’origine. Un redémarrage restaure les réglages sans appeler Altium ; cliquez sur **Connecter** pour vérifier la connexion et charger les dossiers.

Les tokens ne sont ni renvoyés par l’API locale, ni inclus dans les rapports, URLs, logs ou stockage navigateur. Le serveur écoute seulement sur `127.0.0.1`, sur un port aléatoire, avec une clé de session par processus, contrôle Host/Origin et Content Security Policy. Aucun CDN ni télémétrie. Les redirections HTTP sont désactivées pour les requêtes authentifiées.

**Effacer la connexion et le journal** supprime aussi la sauvegarde locale ; les données Altium restent en place. Conservez/exportez le journal avant de l’effacer : la déduplication des imports repose sur ce journal. Les empreintes déjà créées par un autre outil ne sont pas réutilisées sur la seule base d’un nom. Les résultats détaillés d’analyse/import restent en mémoire jusqu’à la fermeture et peuvent être exportés ; le journal persiste entre les lancements.

## Développement

Prérequis : .NET SDK 10 et Node.js 22+ pour les tests du frontend. L’application n’a aucune dépendance tierce. La lecture/extraction native des `.PcbLib` utilise Windows Structured Storage.

```powershell
./scripts/test.ps1
dotnet run --project src/FootprintLinker
./scripts/publish.ps1
./scripts/smoke.ps1
```

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
