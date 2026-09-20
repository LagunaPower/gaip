# Règles G@IP

- Limiter le travail au périmètre demandé. Résumer les modifications et demander l'accord avant de coder, sauf accord déjà donné pour le travail en cours.
- Application desktop simple, IPv4 uniquement en V1. Aucun inventaire, Equipment, rack, DHCP, DNS, SNMP, cloud, API ou authentification applicative.
- Aucun SQL. `gaip-data.json` est l'unique fichier métier courant. Ne jamais stocker de champs calculables ou de statut libre/occupée.
- Core indépendant du stockage, de la synchronisation, de l'OS et d'Avalonia. Séparer Core, Storage, Sync, Desktop et Tests.
- Aucune suppression en cascade des sites vers VLAN ni des VLAN vers IP. Une passerelle seule ne bloque pas la suppression d’un VLAN et disparaît avec lui. Adressage unique et sans chevauchement dans toute la base.
- Chaque publication valide tout le modèle, vérifie hash et propriété du verrou, sauvegarde et remplace le JSON atomiquement.
- `history.jsonl` est un journal compact de différences champ par champ : ne jamais y recopier la base complète avant/après. Le cache partagé conserve base + historique validés. Une restauration ne doit jamais écraser `gaip-data.json`, `history.jsonl` ou `edit.lock` ; les sauvegardes sont conservées et une sauvegarde plus récente que le cache interdit la restauration automatique.
- Maintenir les tests ; toute règle réseau importante doit être couverte. Tester compilation et parcours touchés.
- Ne jamais versionner de données utilisateur, caches, sauvegardes, secrets, bin/ ou obj/.
- Documenter les comportements effectivement implémentés et leurs limitations. Aucun ajout fonctionnel non demandé.
