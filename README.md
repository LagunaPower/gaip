# G@IP — Gestion d’Adresses IP

Application desktop C# / .NET 10 / Avalonia 12.1 pour gérer sites, VLAN et plans IPv4. Sans SQL, serveur, inventaire matériel ou compte applicatif. Windows x64 et Linux x64 ; Wayland natif prioritaire, repli XWayland explicite. G@IP est un logiciel open source distribué sous licence MIT.

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

- Accueil : recherche globale, utilisateur système, synchronisation et actualisation. Cartes de sites responsives avec largeur minimale cible d’environ 430 px et maximum configurable de 1 à 8 colonnes (3 par défaut), affichant tous leurs VLAN triés sans défilement interne. Les rangées occupent la hauteur disponible, les cartes d’une même rangée ont la même hauteur et leurs deux boutons sont centrés en bas.
- Lancement : une seule instance de G@IP par utilisateur et par machine, y compris si le même utilisateur possède plusieurs sessions ouvertes. Un second lancement affiche un message puis se ferme ; un autre utilisateur de la machine peut lancer sa propre instance.
- **Ajouter un site** : code unique, nom, description. **Ajouter un VLAN** : VID, nom et cases multi-sites ; CIDR et passerelle facultatifs par site. Les VLAN créés sont indépendants.
- **Multicast** : un groupe est identifié par son adresse IPv4 multicast, avec nom et description. Chaque groupe contient des flux identifiés par leur port UDP, avec contenu, description, zéro ou plusieurs IP sources déjà attribuées dans G@IP et zéro ou plusieurs VLAN associés. Dans la vue d’un groupe, un clic sélectionne un flux, un double-clic l’édite et le clic droit propose Modifier/Supprimer ; ces interactions valent aussi en cliquant sur les cellules ✔/✖. La fiche d’un groupe affiche une colonne par site, dans l’ordre d’affichage, avec ✔ vert si le flux utilise au moins un VLAN du site et ✖ rouge sinon. Dans l’éditeur de flux, sources et VLAN disposent chacun d’un champ de recherche filtrant leur multi-sélection. Un groupe non vide ne peut pas être supprimé.
- Cliquer un VLAN ouvre sa fiche/IP. À l’accueil, seule la notation CIDR est affichée pour garder les lignes compactes ; le masque décimal reste calculé et visible dans la fiche réseau et les formulaires. Un `/22` contient 1022 IP utilisables, avec un masque `255.255.252.0`.
- Par défaut, seules la passerelle et les IP enregistrées sont affichées. **Afficher les adresses libres** affiche toute la plage dans une liste virtualisée à défilement continu, **sans pagination**. Réseau et broadcast exclus. **Ajouter une IP** ouvre le formulaire du VLAN courant avec la première adresse libre préremplie ; **Prochaine libre** recalcule cette adresse en ignorant la passerelle et les IP utilisées.
- Vue compacte : logo de 96 px sur les deux lignes du bandeau, actions regroupées en haut et état de connexion en pied de fenêtre. Le résumé conserve CIDR, masque, passerelle et compteurs ; **Détails réseau** déplie les bornes et la description du VLAN. **Accueil** suffit pour revenir aux sites. Les champs de recherche disposent d’une croix pour les vider en un clic ; les champs de saisie n’affichent aucun texte indicatif en fond et les aides restent disponibles en infobulle.
- **Modifier le VLAN**, **Modifier le site**, **Libérer l’adresse** : modifications et suppressions avec validation. Aucun parent non vide n’est supprimé. Une IP utilisée comme source multicast et un VLAN associé à un flux ne peuvent pas disparaître tant que la référence multicast subsiste. Dès qu’un sous-réseau contient au moins une IP attribuée, son CIDR devient non modifiable jusqu’à libération de toutes les IP ; une passerelle seule ne bloque pas ce changement.
- **CSV** : import avec toutes les erreurs détectées avant publication, export VLAN, IP ou les deux. **Excel** : export `.xlsx` complet avec un onglet Sites et VLAN, des liens hypertextes vers un onglet par réseau, les informations réseau et toutes les IP utilisables. **Recherche globale** : inclut aussi groupes/ports multicast, sources et VLAN associés. **Historique** : 1 000 dernières actions, détails dépliables regroupés par objet sans répétition inutile, recherche instantanée et filtrage contextuel d’un groupe multicast.
- Dans une vue VLAN, **CSV** exporte uniquement ce VLAN et ses IP ; **Historique** affiche ses dernières actions liées, y compris les libérations d’IP, sans exposer les autres VLAN dans les détails. Les ajouts génériques sont accessibles à l’accueil.
- **Configuration** : trois onglets — **Stockage & synchronisation**, **Affichage** et **Données & export** — regroupent mode/chemin partagé, fréquence/rétention/diagnostic, thème/colonnes/ordre des sites/répartition des multicast et séparateur/export JSON. En mode partagé, le diagnostic permet aussi de restaurer la base et l’historique supprimés accidentellement à partir du cache local validé, sans écraser de fichier existant.
- **Configuration → Affichage** : déplacer les sites par glisser-déposer ; l’ordre est enregistré avec les autres réglages de l’onglet. Les multicast sont triés numériquement et répartis sur un nombre local de tuiles limité automatiquement pour conserver environ 5 groupes minimum par tuile. Monter/Descendre permet aussi un classement au clavier. Annuler conserve l’ordre précédent. L’ordre est commun aux postes ; en mode partagé, le verrou est pris automatiquement uniquement pendant l’enregistrement. Sans ordre enregistré, tri par code ; les nouveaux sites suivent les sites déjà classés. Les cartes de l’accueil sont espacées de 8 px.

La base initiale est vide. Pour une démonstration volontaire, importer `samples/vlans.csv`, puis `samples/addresses.csv`. Le jeu de démonstration contient 6 sites avec respectivement 5, 7, 9, 11, 13 et 15 VLAN, soit 60 VLAN au total, 6 tailles de sous-réseaux différentes (/23 à /28) et 3 470 attributions IP, avec entre 5 et 200 adresses par VLAN.

## Local et partagé

Local : chaque formulaire publie immédiatement avec sauvegarde et historique. Aucun verrou `edit.lock` ni synchronisation réseau.

Partagé : choisir un dossier filesystem existant accessible en lecture/écriture (`\\NAS\IPAM` ou `/mnt/ipam`). Utiliser une base existante ou autoriser explicitement l’initialisation depuis la base actuelle, seulement si absente. Aucun merge automatique.

En mode partagé, il n’y a plus de bouton de passage en modification. L’utilisateur peut ouvrir directement un formulaire d’ajout, de modification, de libération, d’import ou de classement. Au clic sur **Enregistrer** ou après confirmation d’une suppression/libération, G@IP actualise la base, prend automatiquement le verrou, revérifie le hash et toutes les règles métier, publie puis libère immédiatement le verrou. Si un autre poste a publié entre-temps, la nouvelle base est prise en compte avant validation ; une IP devenue utilisée est donc rejetée sans écrasement et le formulaire reste ouvert pour correction. **Actualiser** force la vérification hors publication.

Hors ligne : cache validé consultable, modifications interdites. Sans cache valide, erreur explicite. Un verrou n’expire pas automatiquement : **Configuration → Diagnostic / gestion du verrou** propose sa libération forcée, avec détenteur, dates et saisie de `LIBÉRER`. L’ancien détenteur ne peut ensuite plus publier, même avant son prochain heartbeat.

## Emplacements

| Élément | Windows | Linux |
|---|---|---|
| Données | `%LOCALAPPDATA%/GAIP` | `$XDG_DATA_HOME/GAIP` ou `~/.local/share/GAIP` |
| Configuration | `%LOCALAPPDATA%/GAIP/config.json` | `$XDG_CONFIG_HOME/GAIP/config.json` ou `~/.config/GAIP/config.json` |
| Base locale | `local/gaip-data.json` sous les données | idem |
| Cache partagé | `cache/gaip-data.json`, `cache/history.jsonl` et `cache/cache.info` | idem |

Stockage de référence : `gaip-data.json`, `history.jsonl`, `backup/`, `edit.lock` pendant une publication partagée. `history.jsonl` est un journal compact de différences champ par champ et ne recopie plus les états complets de la base. La garde technique permanente est rangée dans `.gaip/io.guard` ; le dossier `.gaip` est masqué sous Windows lorsque possible. Au premier accès, l’ancien `.gaip-io.guard` est retiré uniquement s’il n’est plus utilisé. Tous les postes qui utilisent le même partage doivent donc être mis à jour ensemble. Les temporaires `.tmp` ne sont jamais lus comme base.

Avant chaque publication, sauvegarde de l’ancienne base. Rétention configurable, 30 par défaut. Les backups ne sont pas restaurés automatiquement depuis l’interface ; la récupération depuis le cache restaure uniquement la base courante et son historique lorsque ceux-ci ont disparu, tout en conservant les sauvegardes présentes.

## Téléchargements

Les versions publiées de G@IP sont disponibles dans les [GitHub Releases](https://github.com/LagunaPower/gaip/releases).

Les releases Windows destinées au public utilisent la chaîne de signature décrite dans [SIGNING.md](SIGNING.md). Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

À chaque tag `vMAJOR.MINOR.PATCH`, la Release publie les archives portables standard et trimmed, ainsi qu’un **MSI Windows x64**, un **DEB amd64** et un **RPM x86_64**. Les trois paquets installables sont construits à partir de la variante trimmed ; les archives standard restent disponibles comme solution de repli.

## Publications autonomes

```sh
dotnet publish src/GAIP.Desktop -c Release -r win-x64 -p:PublishProfile=Release
dotnet publish src/GAIP.Desktop -c Release -r linux-x64 -p:PublishProfile=Release
```

Ou `./scripts/publish.ps1` / `sh scripts/publish.sh` : le profil **Release** par défaut génère à la fois la version standard et la version **ExperimentalTrimmed** pour `win-x64` et `linux-x64`. Les sorties standard sont dans `artifacts/Release/<RID>/` et les trimmed dans `artifacts/ExperimentalTrimmed/<RID>/`. `linux-arm64` reste standard uniquement. L’exécutable seul suffit : `GAIP.exe` sous Windows ; `chmod +x GAIP`, puis `./GAIP` sous Linux. Aucun .NET à installer. Le runtime et les bibliothèques natives sont inclus ; .NET extrait automatiquement les bibliothèques natives au démarrage. Les fichiers Linux supplémentaires servent uniquement à installer le lanceur et son icône.

Le profil **Development** reste autonome à fichiers séparés, avec symboles : `./scripts/publish.ps1 -Profile Development` ou `sh scripts/publish.sh --profile Development`. Sorties dans `artifacts/Development/<RID>/` ; copier tout ce dossier pour diagnostiquer. Les deux profils gardent `PublishTrimmed=false`. Release n’embarque aucun PDB et exclut les références de conception inutiles via MSBuild. `linux-arm64` reste accepté par les scripts, mais non validé. Voir [les profils et vérifications de publication](docs/PUBLICATION.md).

Une publication **ExperimentalTrimmed**, séparée, est disponible pour `win-x64` et `linux-x64` avec `dotnet publish src/GAIP.Desktop -c Release -r <RID> -p:PublishProfile=ExperimentalTrimmed`. Elle active le trimming complet sans NativeAOT et ne remplace pas la Release standard. Voir [les tailles et validations](docs/PUBLICATION_SIZE.md).

Linux nécessite les bibliothèques graphiques usuelles : fontconfig, EGL/OpenGL, Wayland ; X11/XWayland et bibliothèques associées pour le repli. Les publications x64 sélectionnent les packages et services Avalonia par RID : Win32 sous Windows, X11 par défaut sous Linux, Skia et HarfBuzz dans les deux cas. Les compilations sans RID conservent `UsePlatformDetect()`. Sous Linux, G@IP sélectionne Wayland natif uniquement avec `XDG_SESSION_TYPE=wayland`, sauf override `GAIP_USE_X11=1`. Une variable `WAYLAND_DISPLAY` seule ne déclenche pas Wayland natif : sous WSLg avec un type de session vide, G@IP conserve X11/XWayland. Le package `Avalonia.Wayland` reste inclus dans Linux. Repli explicite :

```sh
GAIP_USE_X11=1 ./GAIP
```

Référence : [documentation Linux Avalonia](https://docs.avaloniaui.net/docs/platform-specific-guides/linux).

## Identité et icônes

Le PNG officiel fourni est conservé à l’identique dans `src/GAIP.Desktop/Assets/gaip-logo.png` et intégré à l’interface. L’icône Windows multi-résolution `GAIP.ico` est intégrée à l’exécutable et aux fenêtres. Les PNG Linux de 16 à 1024 pixels, le lanceur `GAIP.desktop` et `install-desktop.sh` sont inclus dans la publication Linux.

Après copie définitive du dossier Linux, exécuter `sh install-desktop.sh` depuis ce dossier pour enregistrer le lanceur et l’icône dans le menu utilisateur. Déplacer ensuite le dossier nécessite de relancer ce script. Les icônes dérivées se régénèrent sous Windows avec `./scripts/generate-icons.ps1`, sans modifier le PNG source.

## Limites connues

- IPv4 uniquement. /31 et /32 stockables sans IP attribuable : réseau/broadcast exclus selon le cahier des charges. Les CIDR saisis/importés sont normalisés.
- Défilement intégral virtualisé jusqu’à 1 048 574 adresses (`/12`), sans limite à 254 et sans pagination. Au-delà, les IP enregistrées restent consultables et une IP libre peut être recherchée en entier ; un message explicite remplace l’énumération intégrale. La recherche des réseaux affichables accepte aussi une partie d’adresse. Recherche globale limitée à 1 000 résultats affichés.
- Import transactionnel **par fichier**. L’export CSV complet ne remplace pas une sauvegarde JSON : les formats imposés ne représentent pas les sites sans VLAN, leurs descriptions ni leur ordre d’affichage. Un import conserve l’ordre des sites existants.
- Historique append-only avec valeurs avant/après de la base, sans rétention automatique. Sa consultation demande l’accès au stockage de référence, même si la base reste consultable hors ligne.
- La sécurité filesystem suppose un stockage respectant les verrous et renommages. Ne pas modifier les JSON par un outil externe pendant une session. La durabilité physique d’un NAS après acquittement dépend de ce NAS.
- JSON et annexes sont des fichiers distincts : un échec d’historique/cache après publication est signalé, sans annuler les données publiées. En cas de résultat incertain après coupure, actualiser avant de réessayer.
- Les parcours UI utilisent Avalonia Headless ; les exécutables Release ont aussi été démarrés réellement sous Windows et Ubuntu 24.04/WSLg (X11/XWayland). Une session GNOME/KDE Wayland native et un partage SMB/NFS réel restent à valider sur les environnements cibles.

## Code signing policy

La politique de signature publique de G@IP est décrite dans [SIGNING.md](SIGNING.md).

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

Les exécutables Windows des releases sont destinés à être signés Authenticode par la chaîne GitHub Actions/SignPath une fois le projet SignPath Foundation approuvé et l’intégration activée. Les builds locaux et de développement ne sont pas couverts par cette politique.

## Licence

G@IP est distribué sous licence [MIT](LICENSE). Vous pouvez l’utiliser, le modifier et le redistribuer dans les conditions prévues par cette licence.

Voir [spécifications](docs/SPECIFICATIONS.md), [architecture](docs/ARCHITECTURE.md), [modèle](docs/DATA_MODEL.md) et [validation](docs/VALIDATION.md).
