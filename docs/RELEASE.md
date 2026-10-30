Footprint Linker 1.3.0 ajoute le choix des couches dans la vue 2D :

- Toutes les couches présentes, y compris celles de la face inférieure, sont visibles par défaut avec une couleur distincte et une légende.
- Cases à cocher pour sélectionner plusieurs couches ; boutons **Toutes**, **Aucune** et **Seule** pour isoler une couche.
- Couches Paste et Solder Mask des pastilles, expansions manuelles et tenting ; perçages et position des corps 3D sélectionnables séparément.
- Export SVG conforme à la sélection, sans modifier les fichiers source, les associations Altium ou la vue 3D.
- Les ouvertures dépendant de règles de carte non disponibles sont représentées à la taille nominale et signalées.

La version conserve la visualisation locale des empreintes ajoutée en 1.2.0, sans Altium Designer :

- **Voir 2D / 3D** dans le tableau : aperçu SVG vectoriel, zoom et déplacement en 2D ; rotation, déplacement et zoom en 3D.
- Lecture directe de l’empreinte sélectionnée dans le `.PcbLib`, rendu des modèles STEP embarqués et des corps extrudés disponibles.
- Signalement des modèles externes ou non convertibles ; export SVG et GLB de la scène convertie.
- Moteur et ressources graphiques inclus dans l’exécutable, aucun CDN. Export local en ligne de commande possible sans token de workspace.

La version conserve les évolutions de la version 1.1.0 :

- **Parcourir…** ouvre le sélecteur de dossier Windows pour choisir le répertoire racine des empreintes.
- Recherche des `.PcbLib` dans tous ses sous-dossiers, avec chemin relatif affiché. Les noms présents dans plusieurs fichiers restent signalés comme ambigus, sans départage par chemin local.
- **Destination Altium déduite de chaque composant**, en priorité du dossier de son empreinte par défaut, puis des autres empreintes et de son classement. Le tableau présente la destination et son origine ; les cas ambigus sont bloqués.
- Deux actions visibles : **1. Analyser les composants**, puis **2. Ajouter aux composants sélectionnés**. Les composants trouvés restent visibles même si leur empreinte manque. Aucune association n’est présélectionnée.
- **Champ de correspondance configurable**, `AU_ID` par défaut, conservé dans la sauvegarde chiffrée et utilisé pour l’analyse comme pour l’ajout.

Extrayez le ZIP et lancez **FootprintLinker.exe**. Windows 10/11 x64 ; aucun logiciel de développement à installer. Binaire non signé.

Les connexions et le journal 1.0.0 sont conservés. L’ancienne destination globale est ignorée : la destination est maintenant calculée pour chaque composant.

Les rendus 2D/3D sont testés sur un modèle STEP synthétique de dimensions connues et dans le véritable exécutable autonome sans connexion à un workspace. L’import sur un workspace réel et la sélection interactive dans le dialogue Windows restent à qualifier dans votre environnement. La visualisation ne remplace pas une validation de fabrication.
