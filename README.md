# G@IP — Gestion d’Adresses IP

Application desktop C# / .NET 10 / Avalonia 12.1 pour gérer sites, VLAN et plans IPv4. Sans SQL, serveur, inventaire matériel ou compte applicatif. Windows x64 et Linux x64 ; Wayland natif prioritaire, repli XWayland explicite.

## Compiler, tester, lancer

Prérequis : SDK .NET 10, accès NuGet pour la première restauration.

```sh
dotnet restore GAIP.sln
dotnet build GAIP.sln -c Release --no-restore
dotnet test GAIP.sln -c Release --no-restore
dotnet run --project src/GAIP.Desktop
```

Sur le poste de création, un SDK privé a été installé dans `.tools/dotnet/`. Si `dotnet` ne trouve aucun SDK, utiliser `./.tools/dotnet/dotnet.exe` à sa place. Les dépendances téléchargées sont dans `.nuget/packages/` (non versionnées).

```powershell
$env:DOTNET_CLI_HOME="$PWD/.tools/cli"
$env:NUGET_PACKAGES="$PWD/.nuget/packages"
./.tools/dotnet/dotnet.exe run --project src/GAIP.Desktop --no-restore
```

## Interface et parcours

- Accueil : recherche globale, utilisateur système, synchronisation et actualisation. Cartes de sites sur une à trois colonnes, affichant tous leurs VLAN triés sans défilement interne.
- **+ Site** : code unique, nom, description. **+ VLAN** : VID, nom et cases multi-sites ; CIDR et passerelle facultatifs par site. Les VLAN créés sont indépendants.
- Cliquer un VLAN ouvre sa fiche/IP. Calculs réseau, passerelle, statistiques, filtres Toutes/Occupées/Libres et pages de 256 adresses. Cliquer une IP permet son édition ; une ligne libre préremplit l’ajout. **Prochaine IP libre** ignore la passerelle.
- **Modifier le VLAN / réseau**, **Modifier le site**, **Libérer l’adresse** : modifications et suppressions avec validation. Aucun parent non vide n’est supprimé.
- **CSV** : import avec toutes les erreurs détectées avant publication, export VLAN, IP ou les deux. **Historique** : 1 000 dernières actions, détails dépliables.
- **Configuration** : mode, chemin partagé, fréquence, rétention, séparateur, thème et diagnostic/verrou.

La base initiale est vide. Pour une démonstration volontaire, importer `samples/vlans.csv`, puis `samples/addresses.csv`.

## Local et partagé

Local : chaque formulaire publie immédiatement avec sauvegarde et historique. Aucun verrou `edit.lock` ni synchronisation réseau.

Partagé : choisir un dossier filesystem existant accessible en lecture/écriture (`\\NAS\IPAM` ou `/mnt/ipam`). Utiliser une base existante ou autoriser explicitement l’initialisation depuis la base actuelle, seulement si absente. Aucun merge automatique.

**Passer en modification** actualise le cache, prend le verrou et revérifie le hash central. Chaque formulaire publie immédiatement. **Terminer la modification** libère le verrou. Heartbeat toutes les 10 s ; actualisation en consultation toutes les 60 s, configurable. **Actualiser** force la vérification.

Hors ligne : cache validé consultable, modifications interdites. Sans cache valide, erreur explicite. Un verrou n’expire pas automatiquement : **Configuration → Diagnostic / gestion du verrou** propose sa libération forcée, avec détenteur, dates et saisie de `LIBÉRER`. L’ancien détenteur ne peut ensuite plus publier, même avant son prochain heartbeat.

## Emplacements

| Élément | Windows | Linux |
|---|---|---|
| Données | `%LOCALAPPDATA%/GAIP` | `$XDG_DATA_HOME/GAIP` ou `~/.local/share/GAIP` |
| Configuration | `%LOCALAPPDATA%/GAIP/config.json` | `$XDG_CONFIG_HOME/GAIP/config.json` ou `~/.config/GAIP/config.json` |
| Base locale | `local/gaip-data.json` sous les données | idem |
| Cache partagé | `cache/gaip-data.json` et `cache/cache.info` | idem |

Stockage de référence : `gaip-data.json`, `history.jsonl`, `backup/`, `edit.lock` pendant une édition partagée. `.gaip-io.guard` est un fichier technique permanent de coordination ; ne pas le supprimer pendant l’utilisation. Les temporaires `.tmp` ne sont jamais lus comme base.

Avant chaque publication, sauvegarde de l’ancienne base. Rétention configurable, 30 par défaut. Pas de restauration dans l’interface ; une restauration manuelle se fait avec tous les clients fermés et après conservation de la base courante.

## Publications autonomes

```sh
dotnet publish src/GAIP.Desktop -c Release -r win-x64 --self-contained true -o artifacts/win-x64
dotnet publish src/GAIP.Desktop -c Release -r linux-x64 --self-contained true -o artifacts/linux-x64
```

Ou `./scripts/publish.ps1` / `sh scripts/publish.sh`. Copier **tout le dossier** produit. Windows : `GAIP.exe`. Linux : `chmod +x GAIP`, puis `./GAIP`. Aucun .NET à installer sur le poste utilisateur. Publication multi-fichiers non trimée pour fiabiliser les bibliothèques natives. `linux-arm64` est accepté par les scripts, mais non validé.

Linux nécessite les bibliothèques graphiques usuelles : fontconfig, EGL/OpenGL, Wayland ; X11/XWayland et bibliothèques associées pour le repli. G@IP sélectionne `UseWayland()` si `WAYLAND_DISPLAY` existe. Ce backend Avalonia 12.1 est expérimental ; repli explicite :

```sh
GAIP_USE_X11=1 ./GAIP
```

Référence : [documentation Linux Avalonia](https://docs.avaloniaui.net/docs/platform-specific-guides/linux).

## Limites connues

- IPv4 uniquement. /31 et /32 stockables sans IP attribuable : réseau/broadcast exclus selon le cahier des charges. Les CIDR saisis/importés sont normalisés.
- Recherche de sous-réseau : IP occupées, hostnames et descriptions ; une IP libre se recherche en entier. Grands réseaux paginés ; recherche globale limitée à 1 000 résultats affichés.
- Import transactionnel **par fichier**. L’export CSV complet ne remplace pas une sauvegarde JSON : les formats imposés ne représentent pas les sites sans VLAN ni leurs descriptions.
- Historique append-only avec valeurs avant/après de la base, sans rétention automatique. Sa consultation demande l’accès au stockage de référence, même si la base reste consultable hors ligne.
- La sécurité filesystem suppose un stockage respectant les verrous et renommages. Ne pas modifier les JSON par un outil externe pendant une session. La durabilité physique d’un NAS après acquittement dépend de ce NAS.
- JSON et annexes sont des fichiers distincts : un échec d’historique/cache après publication est signalé, sans annuler les données publiées. En cas de résultat incertain après coupure, actualiser avant de réessayer.
- Les tests UI utilisent Avalonia Headless. L’exécution sous un compositeur Linux et sur un partage SMB/NFS réel reste à valider sur les environnements cibles.

Voir [spécifications](docs/SPECIFICATIONS.md), [architecture](docs/ARCHITECTURE.md), [modèle](docs/DATA_MODEL.md) et [validation](docs/VALIDATION.md).
