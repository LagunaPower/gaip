# Validation de la V1

Validation mise à jour le 19 septembre 2026, sur Windows x64 et Ubuntu 24.04 sous WSL2/WSLg, avec SDK .NET 10.0.401 et Avalonia 12.1.0.

## Résultats automatisés

- `dotnet build GAIP.sln -c Release --no-restore` : **réussi, 0 erreur, 0 avertissement**.
- `dotnet test GAIP.sln -c Release` : **105 tests réussis, 0 échec, 0 ignoré** après l'adaptation JSON et la sélection des packages par RID.
- Release republiée pour `win-x64` et `linux-x64`, compressée, sans trimming ni PDB. Development, précédemment publié à fichiers séparés, conserve son profil inchangé. Aucun fichier DLL/SO séparé dans Release. DesignerSupport conservé en Development, absent des dépendances Release ; Diagnostics absent des deux.
- ExperimentalTrimmed publiée séparément pour `win-x64` : **0 avertissement de trimming**, 21,76 Mio. Le test natif démarre l'exécutable seul puis le ferme normalement, code 0 et stderr vide. L'utilisateur indique que cette version semble fonctionner ; cela ne constitue pas une validation exhaustive de ses parcours graphiques. Voir [le rapport de taille](PUBLICATION_SIZE.md).
- Nouvel exécutable Release Windows copié seul et réellement démarré : fenêtre native `G@IP — Gestion d’Adresses IP` détectée, fermeture normale, **code de sortie 0**, stderr vide.
- Nouvel exécutable Release Linux copié seul dans un dossier temporaire et réellement démarré sous Ubuntu 24.04/WSLg : fenêtre X11 détectée par PID, base de test initialisée, fermeture normale, **code de sortie 0**, stderr vide. `DISPLAY=:0`, `WAYLAND_DISPLAY=wayland-0`, `XDG_SESSION_TYPE` absent et `GAIP_USE_X11` absent : sélection automatique X11/XWayland confirmée.

## Couverture

| Domaine | Vérifié |
|---|---|
| IPv4 | /0, /12, /22, /23, /24, /30, /31, /32, valeurs limites, première/dernière, réseau/broadcast, syntaxe stricte ; /22 : masque 255.255.252.0, 1022 adresses consécutives, masque absent du JSON |
| Modèle | Passerelle, IP hors réseau, chevauchement inter-sites, unicités, VID, réservations, champs multilignes, casse, absence de cascade, réduction réseau |
| Consultation | Compteurs avec passerelle, prochaine libre, IP enregistrées par défaut, plage complète sans pagination, tri numérique, recherches partielles/exactes, garde des très grands réseaux, recherche globale |
| CSV | Création et mise à jour, UTF-8, séparateur alternatif, guillemets, erreurs multiples, doublons, chevauchements, absence d’import partiel ; export contextuel par GUID même avec VID identique sur un autre site |
| Excel | Classeur `.xlsx` valide, onglet Sites et VLAN, lien hypertexte VLAN → réseau, un onglet par sous-réseau, informations réseau, passerelle/attributions/libres et garde de capacité Excel |
| Historique VLAN | Événements du VLAN et de ses IP, libération, changement de VID, exclusion des événements étrangers et projection des détails, filtrage avant limite de 1000 |
| Stockage | JSON aller-retour, IDs stables et collections obligatoires, rejet JSON invalide, SHA-256 connu, révisions, backups/rétention et audit |
| Échecs I/O | Sauvegarde impossible, remplacement impossible, temporaire incomplet, hash périmé, central inaccessible |
| Concurrence | Huit candidats au verrou, un seul gagnant, heartbeat, force-unlock contre publication, ancien détenteur interdit de publication |
| Cache | Synchronisation entre sessions, hors ligne en lecture seule, redémarrage sur cache, corruption réparée en ligne/refusée hors ligne, autre partage refusé |
| UI Headless | Création site → VLAN/passerelle → IP ; création multi-sites, blocage chevauchement, édition indépendante ; local → partagé → local vide sauvegardé ; thème enregistré |
| Vue VLAN Headless | Logo/icône, actions textuelles contextuelles, masque, bascule 2 → 1022 lignes, virtualisation et défilement jusqu’à la dernière IP, prochaine libre, CSV/historique contextuels ; détails dépliables, logo 96 px, champs sans texte indicatif, conservation du focus et remise à zéro de la recherche par croix |
| Disposition compacte | Colonnes d’accueil adaptatives avec largeur minimale cible de 430 px, maximum local configurable de 1 à 6 (3 par défaut), 6 colonnes possibles sur très grande largeur ; rangées étirées pour occuper la hauteur disponible, cartes d’une même rangée de hauteur égale, actions site centrées et ancrées en bas, masque décimal masqué sur les lignes VLAN de l’accueil. À 1320 × 850, début de liste avant 300 px et hauteur utile supérieure à 500 px ; à 760 × 540, hauteur supérieure à 180 px et bouton d’ajout visible |
| Classement des sites | Ancien JSON sans ordre, persistance et renommage, nouveaux sites à la fin, suppression, IDs périmés/dupliqués refusés sans mutation, conservation par import CSV, verrou et diffusion entre sessions/cache ; gestes réels de glisser-déposer dans les deux sens, Annuler, Enregistrer, réouverture, accueil et lecture seule en partagé |
| Backend | Packages par RID : Win32 pour Windows, X11 et Wayland pour Linux ; mêmes services que la détection standard ; Wayland uniquement avec XDG_SESSION_TYPE=wayland ; override GAIP_USE_X11=1 prioritaire |
| JSON généré | Compatibilité du format existant pour base, configuration et snapshots d'historique ; aller-retour, enums, accents, ordre d'affichage, historique compact |

Captures générées : `artifacts/screenshots/home.png`, `home-narrow.png`, `home-dark.png`, `subnet.png`, `slash22-scroll-end.png`, `slash22-compact-small.png`, `slash22-compact-dark.png`, `site-display-order.png`. Vues /22 compactes et onglet de classement inspectés ; accueil inspecté avec espacement réduit des cartes. Les tests d’interface Headless utilisent des dossiers temporaires isolés, jamais les données utilisateur.

## Validation encore nécessaire sur les environnements cibles

- Windows 10 1809 x64 build 17763 : cible conservée, mais machine de cette version indisponible. Les démarrages Windows ont été contrôlés sur Windows 11 x64 build 26200. Les tests Headless ne prouvent pas à eux seuls tous les parcours du binaire trimé.

- Exécution native sous GNOME/KDE Wayland à valider. Le nouvel artefact est désormais validé réellement sous WSLg/X11, conformément au retour utilisateur signalant l’échec de Wayland forcé dans cet environnement.
- Coupure réelle de réseau et concurrence de postes distincts sur le NAS SMB/NFS destiné à l’exploitation. Les scénarios automatisés utilisent plusieurs sessions sur un filesystem local et simulent la disparition du central.
- Coupure électrique/stockage distant : la durabilité après acquittement dépend du serveur filesystem. Les tests vérifient l’absence d’écriture partielle par l’application, pas les garanties matérielles du NAS.
- Linux ARM64 : commandes prévues, publication et exécution non validées.

## Vérification manuelle conseillée

1. Lancer l’application, créer un site, puis un VLAN avec réseau et passerelle. Ajouter une IP et une réservation sans hostname.
2. Rechercher l’IP/hostname, cocher « Afficher les adresses libres » et parcourir un /22 jusqu’à sa dernière IP, puis libérer une adresse. Essayer un chevauchement puis une réduction de réseau invalidant une IP.
3. Importer les CSV d’exemple dans une base de test vide. Exporter les deux formats et ouvrir dans Excel/LibreOffice.
4. Depuis deux postes sur un partage de test, prendre le verrou sur le premier, constater le blocage du second. Forcer depuis le second et vérifier que le premier ne publie plus.
5. Couper l’accès au partage : consultation conservée, publication interdite. Rétablir puis actualiser. Vérifier historique et sauvegardes.
