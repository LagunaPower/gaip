# Architecture

```text
GAIP.Desktop → GAIP.Sync → GAIP.Storage → GAIP.Core
GAIP.Tests → ces projets + Avalonia.Headless.XUnit
```

- **Core** : modèles unicast et multicast, IPv4 uint/ulong, validation globale, requêtes, CSV transactionnel en mémoire et export Excel OOXML en flux. Indépendant de l’OS, du filesystem et d’Avalonia. Des attributs System.Text.Json imposent la présence des IDs/collections.
- **Storage** : JSON strict, SHA-256, configuration et chemins OS, repository filesystem, verrous, sauvegardes et historique.
- **Sync** : sessions local/partagé, cache vérifié et associé à sa source, états consultation/édition/hors ligne, publication d’une copie.
- **Desktop** : contrôles Avalonia en C#, fenêtres/formulaires et contrôleur de présentation. Règles dans Core. Sémaphore de session, I/O hors du thread UI. En partagé, les formulaires ne gardent pas le verrou : il est acquis automatiquement seulement pendant la publication.
- **Tests** : xUnit v3, invariants, imports, concurrence/stockage et parcours UI Headless.

## Concurrence

`edit.lock` représente un bail d’écriture. Dans le parcours UI normal, ce bail est très court : actualisation, acquisition juste avant l’enregistrement, publication, puis libération immédiate. `.gaip/io.guard` est la garde technique ouverte avec `FileShare.None` lors d’une publication, acquisition, libération forcée ou heartbeat. Le fichier reste présent dans le sous-dossier technique `.gaip`, masqué sous Windows lorsque possible. Au premier accès, l’ancien `.gaip-io.guard` n’est supprimé qu’après prise exclusive ; s’il est encore utilisé, l’opération est refusée. Les clients partageant un même stockage doivent donc être mis à jour ensemble. Les handles sont relâchés à la fin de l’opération ou du processus. Attente maximale de garde : 5 s.

Acquisition UI : actualisation cache → garde → absence de verrou → hash central attendu → création `CreateNew` → relecture central → publication → libération. Le formulaire reste ouvert si l’acquisition ou la validation échoue. Publication et force-unlock partagent la garde : une écriture déjà engagée peut terminer avant la libération forcée ; aucune avec l’ancien ID ne peut réussir après.

La garde protège aussi les écritures de plusieurs instances locales, sans verrou d’édition ni heartbeat. Les lectures ne prennent pas de verrou d’édition. Ces garanties supposent que le filesystem distant respecte les primitives de partage et de renommage.

## Publication et pannes

Le candidat est une copie. Sous garde : validation, contrôle hash/verrou, sauvegarde exacte vérifiée, sérialisation JSON validée, temporaire flushé dans le dossier cible puis renommé. Le fichier courant n’est jamais tronqué sur place. Relecture et comparaison au hash attendu.

Si la vérification finale échoue, le résultat est annoncé incertain et l’édition partagée est interrompue. Actualiser avant de retenter. Historique/cache sont distincts du JSON : leurs erreurs post-publication sont signalées sans rollback aveugle. Une sauvegarde impossible bloque la publication ; une purge impossible laisse les sauvegardes et produit un avertissement.

Historique : lignes JSON compactes contenant uniquement les différences métier champ par champ. Les IDs de site/VLAN sont portés par chaque changement afin de filtrer l’historique contextuel sans désérialiser deux copies de la base ; l’ajout ou le retrait d’un VLAN dans un flux multicast produit un changement ciblé par `VlanId`, tandis que les groupes/flux multicast restent filtrés par leur identifiant naturel `adresse[:port]`. Aucune rétention automatique. Sauvegardes : horodatage UTC, révision précédente et suffixe unique. Aucun changement utilisateur en attente entre formulaires.

## Cache

Base centrale validée avant copie. Le cache partagé contient `gaip-data.json`, `history.jsonl` et des métadonnées `hash`, `historyHash`, `source`, `checkedAt`. La base et l’historique sont copiés atomiquement et vérifiés ; un ancien cache sans hash d’historique reste utilisable pour la consultation hors ligne mais pas pour une restauration complète. Un historique central invalide n’empêche pas la consultation de la base, mais empêche l’actualisation du cache de récupération et produit un avertissement. Le cache d’un autre partage est toujours refusé.

Restauration : sous la garde filesystem, validation du JSON, du journal et de leurs hashes, refus si `gaip-data.json`, `history.jsonl` ou `edit.lock` existe. Les sauvegardes existantes sont laissées intactes ; la présence d’une sauvegarde standard de révision supérieure au cache bloque la restauration automatique. `history.jsonl` est créé et vérifié avant publication de `gaip-data.json` ; aucune entrée artificielle de restauration n’est ajoutée au journal restauré.

## Linux

Les publications x64 sélectionnent les packages Avalonia par RID : Win32 pour Windows, X11 et Wayland pour Linux, Skia et HarfBuzz dans les deux cas. `Program` configure les mêmes services que les branches de `UsePlatformDetect()` ; les compilations sans RID conservent cette méthode. Linux : ajout de `UseWayland()` uniquement si `XDG_SESSION_TYPE=wayland` et si `GAIP_USE_X11` n’est pas `1`. `WAYLAND_DISPLAY` seul est ignoré (cas WSLg) ; sinon X11/XWayland. Le package Wayland 12.1, expérimental, reste présent dans Linux. Development reste autonome à fichiers séparés ; Release autonome en single-file compressé, sans trimming, ReadyToRun, PDB ni DesignerSupport. Un profil ExperimentalTrimmed Windows x64 séparé active le trimming complet sans NativeAOT. Les sérialisations utilisent des contextes JSON générés ; leur format reste compatible avec les fichiers existants. Voir `PUBLICATION.md`.

## Vue VLAN et identité

`SiteOrdering` porte le tri et la validation du classement utilisateur par UUID. `SiteOrderEditor` garde une copie de la séquence en mémoire jusqu’à Enregistrer ; les publications passent par `DataSession.Save`, comme toute autre modification. La copie CSV conserve `displayOrder` pour éviter de perdre la préférence à l’import. L’ordre physique de la collection métier n’est pas modifié.

`AddressRows` est une collection indexée en lecture seule : les lignes libres sont créées à la demande, sans allocation d’une ligne par IP. La liste Avalonia utilise `VirtualizingStackPanel` dans une zone de hauteur finie, sans ScrollViewer parent. Pas de pagination. Affichage intégral jusqu’au /12 ; au-delà, consultation des IP utilisées et recherche d’une adresse exacte, sans modification des calculs réseau.

Le CSV accepte un identifiant stable de VLAN pour filtrer les deux exports. `VlanHistory` sélectionne directement, dans le journal de différences, les changements portant l’ID stable du VLAN demandé. `MulticastHistory` projette de même les changements d’un groupe et de ses flux à partir de l’adresse multicast. Le filtrage précède la limite de 1 000 événements. Le nouveau format d’historique remplace le format à snapshots complets ; aucune migration de l’ancien `history.jsonl` n’est prévue avant diffusion du logiciel.

Le PNG officiel reste intact dans Assets. Des formats d’icônes dérivés sont produits par un script reproductible. Windows embarque l’ICO dans le PE ; Linux utilise les icônes PNG et un lanceur de bureau installé dans le profil utilisateur. Le nom technique de l’application est `GAIP`.

[Référence Avalonia Linux](https://docs.avaloniaui.net/docs/platform-specific-guides/linux).
