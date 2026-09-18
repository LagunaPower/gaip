# Architecture

```text
GAIP.Desktop → GAIP.Sync → GAIP.Storage → GAIP.Core
GAIP.Tests → ces projets + Avalonia.Headless.XUnit
```

- **Core** : modèles, IPv4 uint/ulong, validation globale, requêtes, CSV transactionnel en mémoire. Indépendant de l’OS, du filesystem et d’Avalonia. Des attributs System.Text.Json imposent la présence des IDs/collections.
- **Storage** : JSON strict, SHA-256, configuration et chemins OS, repository filesystem, verrous, sauvegardes et historique.
- **Sync** : sessions local/partagé, cache vérifié et associé à sa source, états consultation/édition/hors ligne, publication d’une copie.
- **Desktop** : contrôles Avalonia en C#, fenêtres/formulaires et contrôleur de présentation. Règles dans Core. Sémaphore de session, I/O hors du thread UI. Heartbeat maintenu pendant les formulaires.
- **Tests** : xUnit v3, invariants, imports, concurrence/stockage et parcours UI Headless.

## Concurrence

`edit.lock` représente une session humaine. `.gaip-io.guard` est une garde technique ouverte avec `FileShare.None` lors d’une publication, acquisition, libération forcée ou heartbeat. Le fichier reste présent : sa suppression pourrait créer deux groupes de clients sur des inodes différents. Les handles sont relâchés à la fin de l’opération ou du processus. Attente maximale de garde : 5 s.

Acquisition : actualisation cache → garde → absence de verrou → hash central attendu → création `CreateNew` → relecture central → autorisation d’édition. Publication et force-unlock partagent la garde : une écriture déjà engagée peut terminer avant la libération forcée ; aucune avec l’ancien ID ne peut réussir après.

La garde protège aussi les écritures de plusieurs instances locales, sans verrou d’édition ni heartbeat. Les lectures ne prennent pas de verrou d’édition. Ces garanties supposent que le filesystem distant respecte les primitives de partage et de renommage.

## Publication et pannes

Le candidat est une copie. Sous garde : validation, contrôle hash/verrou, sauvegarde exacte vérifiée, sérialisation JSON validée, temporaire flushé dans le dossier cible puis renommé. Le fichier courant n’est jamais tronqué sur place. Relecture et comparaison au hash attendu.

Si la vérification finale échoue, le résultat est annoncé incertain et l’édition partagée est interrompue. Actualiser avant de retenter. Historique/cache sont distincts du JSON : leurs erreurs post-publication sont signalées sans rollback aveugle. Une sauvegarde impossible bloque la publication ; une purge impossible laisse les sauvegardes et produit un avertissement.

Historique : lignes JSON avec états avant/après, aucune rétention automatique. Sauvegardes : horodatage UTC, révision précédente et suffixe unique. Aucun changement utilisateur en attente entre formulaires.

## Cache

Base centrale validée avant copie. Métadonnées `hash`, `source`, `checkedAt`. Copie atomique et vérification. Un cache corrompu est réparé en ligne ; hors ligne, il est rejeté au démarrage. La copie mémoire déjà validée peut rester consultable lors d’une panne. Le cache d’un autre partage est toujours refusé.

## Linux

Wayland natif sélectionné si `WAYLAND_DISPLAY` existe, sauf `GAIP_USE_X11=1`. En l’absence de Wayland, détection Avalonia standard. Backend Wayland 12.1 expérimental, repli explicite XWayland. Publication autonome non trimée et multi-fichiers pour les dépendances natives.

[Référence Avalonia Linux](https://docs.avaloniaui.net/docs/platform-specific-guides/linux).
