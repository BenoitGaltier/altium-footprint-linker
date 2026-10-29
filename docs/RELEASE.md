Footprint Linker 1.1.0 ajoute les évolutions demandées :

- **Parcourir…** ouvre le sélecteur de dossier Windows pour choisir le répertoire racine des empreintes.
- Recherche des `.PcbLib` dans tous ses sous-dossiers, avec chemin relatif affiché. Les noms présents dans plusieurs fichiers restent signalés comme ambigus, sans départage par chemin local.
- **Destination Altium déduite de chaque composant**, en priorité du dossier de son empreinte par défaut, puis des autres empreintes et de son classement. Le tableau présente la destination et son origine ; les cas ambigus sont bloqués.
- Deux actions visibles : **1. Analyser les composants**, puis **2. Ajouter aux composants sélectionnés**. Les composants trouvés restent visibles même si leur empreinte manque. Aucune association n’est présélectionnée.
- **Champ de correspondance configurable**, `AU_ID` par défaut, conservé dans la sauvegarde chiffrée et utilisé pour l’analyse comme pour l’ajout.

Extrayez le ZIP et lancez **FootprintLinker.exe**. Windows 10/11 x64 ; aucun logiciel de développement à installer. Binaire non signé.

Les connexions et le journal 1.0.0 sont conservés. L’ancienne destination globale est ignorée : la destination est maintenant calculée pour chaque composant.

Tests automatiques et import sur simulateur. L’import sur un workspace réel et la sélection interactive dans le dialogue Windows restent à qualifier dans votre environnement.
