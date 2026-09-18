# Validation de la V1

Validation effectuée le 18 septembre 2026, sur Windows x64, avec SDK .NET 10.0.401 et Avalonia 12.1.0.

## Résultats automatisés

- `dotnet build GAIP.sln -c Release --no-restore` : **réussi, 0 erreur, 0 avertissement**.
- `dotnet test GAIP.sln -c Release --no-restore` : **75 tests réussis, 0 échec, 0 ignoré**.
- Publications autonomes `win-x64` et `linux-x64` générées sous `artifacts/`.
- Exécutable autonome Windows démarré en fenêtre masquée : fenêtre Win32 `G@IP — Gestion d’Adresses IP` détectée, fermeture normale par message de fenêtre, **code de sortie 0**, stderr vide. La première sonde `CloseMainWindow` ne voyait pas la fenêtre masquée ; la sonde finale utilise son handle Win32.

## Couverture

| Domaine | Vérifié |
|---|---|
| IPv4 | /0, /23, /24, /30, /31, /32, valeurs limites, première/dernière, réseau/broadcast, syntaxe stricte |
| Modèle | Passerelle, IP hors réseau, chevauchement inter-sites, unicités, VID, réservations, champs multilignes, casse, absence de cascade, réduction réseau |
| Consultation | Compteurs avec passerelle, prochaine libre, réseau complet, tri numérique, pagination /0, filtres, recherche globale |
| CSV | Création et mise à jour, UTF-8, séparateur alternatif, guillemets, erreurs multiples, doublons, chevauchements, absence d’import partiel |
| Stockage | JSON aller-retour, IDs stables et collections obligatoires, rejet JSON invalide, SHA-256 connu, révisions, backups/rétention et audit |
| Échecs I/O | Sauvegarde impossible, remplacement impossible, temporaire incomplet, hash périmé, central inaccessible |
| Concurrence | Huit candidats au verrou, un seul gagnant, heartbeat, force-unlock contre publication, ancien détenteur interdit de publication |
| Cache | Synchronisation entre sessions, hors ligne en lecture seule, redémarrage sur cache, corruption réparée en ligne/refusée hors ligne, autre partage refusé |
| UI Headless | Création site → VLAN/passerelle → IP ; création multi-sites, blocage chevauchement, édition indépendante ; local → partagé → local vide sauvegardé ; thème enregistré |

Captures rendues et inspectées : `artifacts/screenshots/home.png`, `home-narrow.png`, `home-dark.png`, `subnet.png`. Les tests d’interface utilisent des dossiers temporaires isolés, jamais les données utilisateur.

## Validation encore nécessaire sur les environnements cibles

- Exécution native Linux sous GNOME/KDE Wayland et repli XWayland ; ici seule la publication Linux est vérifiée.
- Coupure réelle de réseau et concurrence de postes distincts sur le NAS SMB/NFS destiné à l’exploitation. Les scénarios automatisés utilisent plusieurs sessions sur un filesystem local et simulent la disparition du central.
- Coupure électrique/stockage distant : la durabilité après acquittement dépend du serveur filesystem. Les tests vérifient l’absence d’écriture partielle par l’application, pas les garanties matérielles du NAS.
- Linux ARM64 : commandes prévues, publication et exécution non validées.

## Vérification manuelle conseillée

1. Lancer l’application, créer un site, puis un VLAN avec réseau et passerelle. Ajouter une IP et une réservation sans hostname.
2. Rechercher l’IP/hostname, filtrer occupées/libres, libérer l’adresse. Essayer un chevauchement puis une réduction de réseau invalidant une IP.
3. Importer les CSV d’exemple dans une base de test vide. Exporter les deux formats et ouvrir dans Excel/LibreOffice.
4. Depuis deux postes sur un partage de test, prendre le verrou sur le premier, constater le blocage du second. Forcer depuis le second et vérifier que le premier ne publie plus.
5. Couper l’accès au partage : consultation conservée, publication interdite. Rétablir puis actualiser. Vérifier historique et sauvegardes.
