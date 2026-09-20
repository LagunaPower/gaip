# Modèle JSON v1

`gaip-data.json` contient l’unique état métier courant. UTF-8, propriétés camelCase, version explicite. Schéma inconnu ou modèle invalide refusés sans réinitialisation silencieuse.

```json
{
  "schemaVersion": 1,
  "revision": 3,
  "lastModified": "2026-09-18T14:00:00+00:00",
  "lastModifiedBy": "DOMAINE\\utilisateur",
  "lastModifiedFrom": "POSTE01",
  "sites": [{
    "id": "f3fc5c86-6d0b-4ffc-b949-e018bceac6ef",
    "code": "LEVANT",
    "name": "Île du Levant",
    "description": "",
    "vlans": [{
      "id": "93fa0fd9-051d-4c7b-8fb3-a0b1800f5ac2",
      "vid": 120,
      "name": "SERVEURS",
      "description": "Applications",
      "subnet": {
        "cidr": "10.20.120.0/24",
        "gateway": { "address": "10.20.120.1", "comment": "Firewall principal" },
        "addresses": [{ "address": "10.20.120.25", "hostname": "SRV-APP-01", "description": "Serveur applicatif" }]
      }
    }]
  }]
}
```

`subnet` et `gateway` peuvent être null. UUID stables pour sites/VLAN ; IP unique comme clé d’attribution. Pas de statut, réseau calculé ni compteur persisté. La passerelle n’est jamais dans `addresses`.

Chaque site peut porter `displayOrder`, entier positif ou nul choisi via Configuration. Ce rang exprime une préférence utilisateur, pas un calcul réseau. Il est absent des anciennes bases : tri par code dans ce cas. L’enregistrement du classement attribue les rangs 0 à N−1 aux IDs courants ; une liste périmée ou incomplète est refusée. Les sites sans rang suivent les sites classés. Les imports CSV préservent les rangs existants. Les anciens exécutables à lecture JSON stricte ne connaissent pas ce champ : mettre les postes d’un partage à la même version avant d’enregistrer un classement.

Révision initiale 0, +1 par publication. Le SHA-256 compare les octets complets, pas uniquement la révision ou la date.

## Annexes

- `edit.lock` : `id`, `user`, `machine`, `acquiredAt`, `heartbeat`, `startRevision`, `startHash`. Nouvel ID à chaque acquisition. Le heartbeat n’autorise jamais une reprise automatique.
- `history.jsonl` : une ligne compacte par action avec `date`, `user`, `machine`, `revision`, `action`, `objectType`, `target` et `changes[]`. Chaque changement porte `siteId`, `vlanId`, `objectType`, `target`, `field`, `oldValue`, `newValue`. Seules les différences métier sont journalisées ; les snapshots complets de la base ne sont jamais recopiés dans l’historique.
- `backup/gaip-data_<UTC>_rev<révision>_<suffixe>.json` : octets exacts avant publication.
- `cache/gaip-data.json` et `cache/history.jsonl` : copie locale de récupération en mode partagé. `cache/cache.info` : `hash`, `historyHash`, `source` absolue, `checkedAt`.
- `config.json` : `mode` (`Local`/`Shared`), `sharedPath`, `syncSeconds` (5–86400, défaut 60), `backupCount` (1–10000, défaut 30), `csvSeparator` (un caractère, défaut `;`), `theme` (`System`/`Light`/`Dark`).
- `.gaip/io.guard` : garde technique vide, permanente, sans donnée métier. Le dossier `.gaip` est caché sous Windows lorsque possible ; l’ancien `.gaip-io.guard` est supprimé lors de la transition uniquement s’il n’est plus utilisé.

Les CSV imposés ne représentent pas tous les champs JSON. Voir `SPECIFICATIONS.md` pour leurs règles ; ne pas les utiliser comme sauvegarde intégrale.
