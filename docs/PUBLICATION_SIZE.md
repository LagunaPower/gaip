# Taille et validation des publications — 19 septembre 2026

Mesures locales avec SDK .NET 10.0.401, runtime 10.0.12 et Avalonia 12.1.0. Aucune montée de version ni NativeAOT. Les tailles sont en octets et en Mio (1 Mio = 1 048 576 octets).

## Mesures avant / après

| Publication | Exécutable avant | Dossier avant | Exécutable après | Dossier après |
|---|---:|---:|---:|---:|
| Release Windows x64 | 50 441 098 (48,10 Mio) | 50 441 098 | 49 471 523 (47,18 Mio) | 49 471 523 (47,18 Mio) |
| Release Linux x64 | 50 014 134 (47,70 Mio) | 50 576 293 | 49 568 881 (47,27 Mio) | 50 131 040 (47,81 Mio) |
| Expérimentale Windows avec trimming, comparée à la nouvelle Release | 49 471 523 (47,18 Mio) | 49 471 523 | 22 819 214 (21,76 Mio) | 22 819 214 (21,76 Mio) |

Le trimming économise **26 652 309 octets, soit 53,87 %**, par rapport à la nouvelle Release Windows. La sélection des dépendances réduit aussi les deux Release non trimées. Le dossier Linux contient 562 159 octets d'icônes et de fichiers facultatifs pour le lanceur.

La compression était déjà active au début de cette intervention. La mesure précédente consignée dans `artifacts/compression-comparison.json` était de 104 985 678 → 50 441 098 octets sous Windows, et 100 076 671 → 50 014 134 sous Linux, avant la sélection des packages par RID.

Le rapport fourni par l'utilisateur (105,91 Mo, dont seulement environ 0,22 Mo de code G@IP) décrit surtout le coût de .NET et du moteur graphique en publication autonome. Le vieux dossier à fichiers séparés `artifacts/win-x64` présent sur cette machine mesure 111 318 947 octets (106,16 Mio), avec un lanceur de 226 816 octets ; ce lanceur dépend de ses DLL voisines. Cette mesure locale n'est donc pas assimilée à la taille d'un single-file, ni à une reproduction exacte du rapport initial. Le dossier Development existant contient également ses dépendances de diagnostic ; son profil n'a pas été modifié.

## Contenu contrôlé

- Manifestes des bundles lus : 201 entrées dans Release Windows, 55 dans ExperimentalTrimmed Windows. Aucun PDB, DesignerSupport, Diagnostics, DiaSymReader, createdump, mscordaccore ou mscordbi dans ces bundles.
- X11, Wayland, FreeDesktop, NWayland et Tmds.DBus.Protocol absents de Windows, présents dans Linux. `Avalonia.Desktop` n'est plus utilisé par les publications x64 ; les références NuGet ciblent les backends du RID.
- Metal est livré par le package principal Avalonia et directement référencé par Skia. Il reste présent : 15 360 octets avant trimming, 13 824 après, avant compression. Il n'a pas été supprimé manuellement.
- Les composants natifs de diagnostic sont écartés par le SDK via `DropFromSingleFile=true` dans le runtime pack ; aucun filtre artisanal n'a été ajouté pour eux.
- Les seules exclusions propres à G@IP concernent les références de conception inutiles et les symboles, avant bundling via MSBuild. Aucune DLL n'a été supprimée après publication.
- Les profils `Default*.pubxml` trouvés appartiennent au SDK, pas au projet : aucun n'a été retiré.

## Trimming et vérifications

`ExperimentalTrimmed.pubxml` importe Release et active `PublishTrimmed=true`, `TrimMode=full`, `TrimmerSingleWarn=false`, avec une sortie distincte. Release conserve `PublishTrimmed=false`, la compression, l'extraction des bibliothèques natives et `PublishReadyToRun=false`. Aucun NativeAOT et aucun avertissement masqué.

Les appels System.Text.Json dépendant de la réflexion ont été remplacés par des contextes générés pour la base, la configuration, les verrous, l'historique et le cache. Les tests comparent le JSON généré au format précédent. **Publication expérimentale finale : zéro avertissement de trimming, zéro erreur.** Journal : `artifacts/trimming-win-x64.log`.

- Compilation et tests : **105 réussis, zéro échec**. Les tests Avalonia Headless couvrent création site/VLAN/IP, thèmes, recherche, /22, virtualisation, prochaine libre, historique/CSV et classement des sites. Ils exécutent les assemblies de test non trimées.
- Release Windows : exécutable copié seul, fenêtre native détectée, fermeture normale, code 0, stderr vide. Rapport : `artifacts/smoke/windows-7258cc122f2a482abf07fd170ca7b5fe/result.json`.
- Expérimentale Windows : mêmes contrôles réussis, code 0, stderr vide. Rapport : `artifacts/smoke/windows-215b8430f3cc4144b4f93844aac0f60a/result.json`. L'utilisateur signale que cette version semble fonctionner. Le contrôle interactif automatisé a été interrompu avec Échap ; il ne démontre pas tous les parcours du binaire trimé.
- Release Linux : exécutable copié seul, données/configuration isolées, fenêtre native et initialisation de la base confirmées sous Ubuntu 24.04/WSLg, fermeture normale, code 0, stderr vide. `XDG_SESSION_TYPE` absent : X11/XWayland retenu malgré `WAYLAND_DISPLAY=wayland-0`. Rapport : `artifacts/smoke/linux-optimized/result.json`.

## Compatibilité et limites

La cible reste **Windows 10 1809 x64 build 17763**. Aucun ajout d'API Windows plus récente, aucun changement de framework/runtime/package, manifeste Windows 10 conservé. Les deux exécutables Windows ont un en-tête PE AMD64 (8664), versions OS et sous-système 6.0 inchangées. Ces contrôles ne remplacent pas une exécution sur la version minimale : la machine de test tourne sous Windows 11 build 26200, et aucun poste 17763 n'est disponible ici.

Le trimming reste expérimental. Une validation complète des parcours du binaire trimé, de Windows 10 1809 et d'une vraie session GNOME/KDE Wayland reste nécessaire avant de lui donner le statut de Release par défaut.

Mesures brutes : `artifacts/size-before-optimization.json`, `artifacts/size-after-optimization.json`. Manifestes : `artifacts/bundle-Release-win-x64.json`, `artifacts/bundle-Release-linux-x64.json`, `artifacts/bundle-ExperimentalTrimmed-win-x64.json`. Empreinte SHA-256 inchangée de Development.pubxml : `2A5BA5C886501ABE3F91B4DAE335A0B45ABE7027A5930C4CB9A7922CF98A72C7`.
