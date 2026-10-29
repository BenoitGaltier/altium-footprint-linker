Application Windows portable pour ajouter des empreintes complémentaires à des composants Altium 365.

- Connexion URL/token et sauvegarde chiffrée, dans le même esprit que Component Lens.
- Import CSV à six colonnes et recherche de tous les composants correspondants par `AU_ID`.
- Indexation récursive des noms internes `.PcbLib`, y compris bibliothèques contenant plusieurs empreintes.
- Tableau de contrôle, sélection, import puis ajout en fin de liste avec vérification de l’empreinte par défaut.
- Journal chiffré de reprise et blocage des répétitions d’écritures incertaines.
- Rapport JSON, exemple CSV, exécutable avec runtime inclus et checksum SHA-256.

Extrayez le ZIP et lancez **FootprintLinker.exe**. Aucun logiciel de développement à installer. Windows 10/11 x64 requis. Binaire non signé.

La validation automatique utilise un serveur Altium simulé et des bibliothèques synthétiques. L’import sur un workspace réel reste à qualifier avec vos droits, règles de nommage et fichiers.
