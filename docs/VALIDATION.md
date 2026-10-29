# Validation

Validation locale sous Windows, le 29 octobre 2026, avec .NET SDK 10.0.402.

La suite utilise uniquement des identifiants fictifs, un handler HTTP Altium simulé, des bibliothèques CFB synthétiques et des répertoires temporaires isolés. Aucun token personnel ni fichier de bibliothèque métier n’est inclus dans le dépôt ou les fixtures.

## Tests automatisés

`scripts/test.ps1` exécute 17 cas backend et 4 cas frontend :

- CSV : séparateurs, BOM, colonnes réordonnées, guillemets, champs multilignes, rejets d’entrées incohérentes et conservation des zéros initiaux.
- AU_ID : valeur de révision, héritage item, valeur vide, paramètres contradictoires, tous les composants correspondants, doublons CSV et noms de fichiers ambigus.
- PcbLib : lecture native d’un CFB, extraction d’un modèle depuis une bibliothèque à plusieurs empreintes, conservation de données binaires opaques et de l’original, neutralisation des anciens GUID de workspace sans déplacement des offsets.
- Connexion : validation des URL Altium, préfixe Bearer, non-exposition du token, accès local authentifié et rejet d’origines étrangères.
- Stockage : protection DPAPI, remplacement atomique avec backup, verrou exclusif, lecture du fichier chiffré après fermeture/réouverture, effacement.
- Import simulé : un seul upload et une seule création pour deux composants ayant le même AU_ID, ajout en fin de liste et lecture de vérification, import déjà effectué ignoré, schéma de nommage hérité, fichier modifié bloqué avant upload, AU_ID absent.
- Reprise : création distante incertaine non répétée après erreur et redémarrage de l’hôte applicatif ; association incertaine réconciliée par lecture ; reprise manuelle avec un ID d’empreinte vérifié ; inventaire partiel bloquant toutes les écritures.
- Frontend : échappement des données non fiables, filtres, exemple CSV et neutralisation des formules dans le helper d’export CSV.

`scripts/smoke.ps1` vérifie le véritable exécutable autonome : cinq ressources embarquées, rejet de l’accès anonyme, état local isolé, rejet d’une origine étrangère et arrêt propre, soit 9 contrôles HTTP.

Les workflows GitHub répètent les tests sur Windows. Le workflow de release vérifie la version du tag, construit le ZIP avec runtime inclus, exécute le test du binaire et publie uniquement si ces étapes réussissent.

## Vérification dans le navigateur

Le parcours a été exécuté dans le navigateur intégré sur le workspace fictif explicitement marqué **SIMULATION** : affichage de deux correspondances pour un AU_ID commun, sélection des deux lignes, confirmation, import simulé puis affichage de deux associations vérifiées sans échec. La mise en page a été inspectée dans une fenêtre étroite et la fermeture depuis **Quitter** vérifiée. Aucune écriture sur un workspace réel n’a été effectuée.

## Périmètre à qualifier avec des données réelles

Les contrats GraphQL de création et d’ajout ont été vérifiés contre la documentation officielle Altium et son schéma au 29 octobre 2026. Cela ne remplace pas un essai dans le workspace concerné.

Restent à qualifier : droits du token, règles et schémas de nommage/révision du dossier, éventuelles politiques serveur, acceptation et rendu des fichiers réels dans Altium Designer, modèles externes, ainsi que les délais de visibilité propres au workspace. La fixture CFB vérifie l’intégrité du conteneur et de données opaques ; elle ne représente pas une validation géométrique/électrique d’une empreinte réelle.

Les modifications existantes simultanées ne peuvent pas être entièrement exclues par l’API : les contrôles avant/après écriture détectent plusieurs changements, mais l’ensemble n’est pas une transaction atomique. Les mutations ne sont pas automatiquement réessayées. La déduplication des créations repose sur le journal local, pas sur une recherche globale par nom.

## Aperçu développeur

`dotnet run --project tests/FootprintLinker.Tests -c Release -- --preview` lance d’abord les tests puis sert un exemple avec workspace marqué **SIMULATION**. Toutes les requêtes Altium restent interceptées par le simulateur ; ce mode appartient au projet de test et n’est pas inclus dans l’exécutable livré. Fermez l’aperçu avec **Quitter**.
