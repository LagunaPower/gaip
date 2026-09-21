# Publication

État à partir de la **V1.0.1**.

## Profils

| Profil | Configuration | Contenu | Sortie |
|---|---|---|---|
| Development | Debug | autonome, fichiers séparés, symboles, non trimmed | `artifacts/Development/<RID>/` |
| Release | Release | autonome, single-file compressé, trimming complet, sans PDB | `artifacts/Release/<RID>/` |

**Release est l’unique publication distribuable.** Elle active `PublishTrimmed=true`, `TrimMode=full`, `SelfContained=true`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `EnableCompressionInSingleFile=true` et `PublishReadyToRun=false`. Aucun NativeAOT. Le profil `ExperimentalTrimmed` a été supprimé.

Les scripts `scripts/publish.ps1` et `scripts/publish.sh` publient `win-x64` et `linux-x64` par défaut. Ils vérifient qu’une Release single-file ne contient pas de PDB, DLL, SO ou manifestes runtime séparés.

## CI et Release GitHub

Les workflows restaurent les dépendances de publication avec `PublishTrimmed=true` et `TrimMode=full`, puis publient uniquement le profil Release. Un garde-fou refuse un exécutable Release supérieur à **35 Mio**, seuil volontairement supérieur aux mesures trimmed historiques (~22 Mio sous Windows) mais inférieur aux publications non trimées observées (~47 Mio).

À chaque tag strict `vMAJOR.MINOR.PATCH` :
1. tests Windows et Linux ;
2. publication Windows x64 et Linux x64 ;
3. vérification de taille ;
4. MSI Windows, DEB amd64 et RPM x86_64 ;
5. installation réelle du RPM sous AlmaLinux 8 ;
6. création d’une Release GitHub en brouillon puis publication uniquement si tout a réussi.

Artefacts attendus :
- `GAIP-vX.Y.Z-win-x64.zip`
- `GAIP-vX.Y.Z-win-x64.msi`
- `GAIP-vX.Y.Z-linux-x64.tar.gz`
- `gaip_X.Y.Z_amd64.deb`
- `gaip-X.Y.Z-1.x86_64.rpm`

## Windows et SignPath

Lorsque `SIGNPATH_ENABLED=true`, l’unique `GAIP.exe` Release est soumis à SignPath puis sa signature Authenticode est contrôlée avant création du ZIP et du MSI. Le MSI est ensuite signé à son tour. Il n’existe plus de couple « standard/trimmed ».

Configuration d’exécutable : `.signpath/artifact-configurations/windows-executables.xml`.
Configuration MSI : `.signpath/artifact-configurations/windows-msi.xml`.
Politique publique : [SIGNING.md](../SIGNING.md).

## Linux : backend graphique

Les publications Linux incluent X11 et Wayland, mais **X11/XWayland est le backend par défaut**. Le backend Wayland natif d’Avalonia 12.1 n’est activé que lorsque :
- `XDG_SESSION_TYPE=wayland`
- `GAIP_USE_WAYLAND=1`

```sh
GAIP_USE_WAYLAND=1 ./GAIP
```

Sans cet opt-in, une session GNOME/KDE Wayland utilise XWayland. `WAYLAND_DISPLAY` seul ne déclenche pas Wayland natif.

## Reproductibilité

Le SDK est fixé par `global.json` à .NET 10.0.401. Les dépendances NuGet sont verrouillées par les `packages*.lock.json`. Les workflows utilisent `--locked-mode` et les actions GitHub tierces sont épinglées par SHA.

Les sérialisations System.Text.Json métier utilisent des contextes générés à la compilation afin de rester compatibles avec le trimming complet.

## Historique des tailles

Les comparaisons qui ont conduit au choix du trimming comme profil Release unique sont conservées dans [PUBLICATION_SIZE.md](PUBLICATION_SIZE.md).
