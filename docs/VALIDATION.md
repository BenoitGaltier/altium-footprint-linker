# Validation

Validation locale sous Windows, le 29 octobre 2026, avec .NET SDK 10.0.402.

La suite utilise uniquement des identifiants fictifs, un handler HTTP Altium simulé, des bibliothèques CFB synthétiques et des répertoires temporaires isolés. Aucun token personnel ni fichier de bibliothèque métier n’est inclus dans le dépôt ou les fixtures.

## Tests automatisés

`scripts/test.ps1` exécute 27 cas backend et 5 cas frontend :

- CSV : séparateurs, BOM, colonnes réordonnées, guillemets, champs multilignes, rejets d’entrées incohérentes et conservation des zéros initiaux.
- AU_ID : valeur de révision, héritage item, valeur vide, paramètres contradictoires, tous les composants correspondants, doublons CSV et noms de fichiers ambigus.
- PcbLib : lecture native d’un CFB, extraction d’un modèle depuis une bibliothèque à plusieurs empreintes, conservation de données binaires opaques et de l’original, neutralisation des anciens GUID de workspace sans déplacement des offsets.
- Connexion : validation des URL Altium, préfixe Bearer, non-exposition du token, accès local authentifié et rejet d’origines étrangères.
- Stockage : protection DPAPI, remplacement atomique avec backup, verrou exclusif, lecture du fichier chiffré après fermeture/réouverture, effacement.
- Import simulé : un seul upload et une seule création pour deux composants ayant le même AU_ID, ajout en fin de liste et lecture de vérification, import déjà effectué ignoré, schéma de nommage hérité, fichier modifié bloqué avant upload, AU_ID absent.
- Reprise : création distante incertaine non répétée après erreur et redémarrage de l’hôte applicatif ; association incertaine réconciliée par lecture ; reprise manuelle avec un ID d’empreinte vérifié ; inventaire partiel bloquant toutes les écritures.
- Frontend : échappement des données non fiables, filtres, exemple CSV et neutralisation des formules dans le helper d’export CSV.
- Version 1.1.0 : champ de correspondance configurable jusqu’à l’import et restauré après redémarrage, destinations distinctes par composant, contrôle d’un changement de destination avant upload, priorité au dossier de l’empreinte par défaut, classement de repli et blocage des destinations ambiguës.
- Deux étapes : l’analyse n’effectue aucune écriture et affiche aussi les composants trouvés dont le fichier manque ; l’ajout ne modifie que les lignes explicitement sélectionnées.
- Arborescence locale : lecture d’une bibliothèque dans un sous-dossier, chemin relatif conservé, aucun départage des noms identiques par chemin.
- Sélecteur système : contrat HTTP testé avec un sélecteur injecté pour les réponses de sélection et d’annulation. Le dialogue Windows utilise `IFileDialog` en mode `FOS_PICKFOLDERS` sur un thread STA. Le choix interactif dans ce dialogue requiert un essai manuel dans une session Windows graphique ; il n’est pas automatisé dans la suite.
- Compatibilité : chargement d’un fichier chiffré 1.0.0, `AU_ID` par défaut, ancienne destination globale ignorée, changement de champ invalidant l’analyse précédente.

`scripts/smoke.ps1` vérifie le véritable exécutable autonome : cinq ressources embarquées, rejet de l’accès anonyme, état local isolé, rejet d’une origine étrangère et arrêt propre, soit 9 contrôles HTTP.

Les workflows GitHub répètent les tests sur Windows. Le workflow de release vérifie la version du tag, construit le ZIP avec runtime inclus, exécute le test du binaire et publie uniquement si ces étapes réussissent.

## Vérification dans le navigateur

Le parcours a été exécuté dans le navigateur intégré sur le workspace fictif explicitement marqué **SIMULATION** : affichage de deux correspondances pour un AU_ID commun, sélection des deux lignes, confirmation, import simulé puis affichage de deux associations vérifiées sans échec. La mise en page a été inspectée dans une fenêtre étroite et la fermeture depuis **Quitter** vérifiée. Aucune écriture sur un workspace réel n’a été effectuée.

Pour la version 1.1.0, le parcours a été vérifié avec une bibliothèque dans `local-libraries/Transistors/source.PcbLib` : chargement du CSV, sélection du répertoire via le sélecteur simulé, analyse, affichage des destinations déduites, sélection d’un seul des deux composants et ajout vérifié sur cette sélection. Le champ configurable et ses libellés ont été modifiés dans l’interface. Les deux boutons d’action restent visibles ; aucune ligne n’est présélectionnée.

## Périmètre à qualifier avec des données réelles

Les contrats GraphQL de création et d’ajout ont été vérifiés contre la documentation officielle Altium et son schéma au 29 octobre 2026. Cela ne remplace pas un essai dans le workspace concerné.

Restent à qualifier : droits du token, règles et schémas de nommage/révision du dossier, éventuelles politiques serveur, acceptation et rendu des fichiers réels dans Altium Designer, modèles externes, ainsi que les délais de visibilité propres au workspace. La fixture CFB vérifie l’intégrité du conteneur et de données opaques ; elle ne représente pas une validation géométrique/électrique d’une empreinte réelle.

Les modifications existantes simultanées ne peuvent pas être entièrement exclues par l’API : les contrôles avant/après écriture détectent plusieurs changements, mais l’ensemble n’est pas une transaction atomique. Les mutations ne sont pas automatiquement réessayées. La déduplication des créations repose sur le journal local, pas sur une recherche globale par nom.

## Aperçu développeur

`dotnet run --project tests/FootprintLinker.Tests -c Release -- --preview` lance d’abord les tests puis sert un exemple avec workspace marqué **SIMULATION**. Toutes les requêtes Altium restent interceptées par le simulateur ; ce mode appartient au projet de test et n’est pas inclus dans l’exécutable livré. Fermez l’aperçu avec **Quitter**.
