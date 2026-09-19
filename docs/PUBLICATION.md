# Profils de publication

Les fichiers `src/GAIP.Desktop/Properties/PublishProfiles/Development.pubxml` et `Release.pubxml` sont utilisables avec `dotnet publish -r <RID> -p:PublishProfile=<profil>`. Les scripts choisissent aussi la configuration Debug ou Release correspondante.

| Profil | Configuration | Déploiement | Symboles | Sortie |
|---|---|---|---|---|
| Development | Debug | Self-contained, fichiers séparés | PDB conservés | `artifacts/Development/<RID>` |
| Release | Release | Self-contained, single-file | Aucun PDB publié ou incorporé au bundle | `artifacts/Release/<RID>` |
| ExperimentalTrimmed | Release | Self-contained, single-file, trimming complet, Windows x64 seulement | Aucun PDB | `artifacts/ExperimentalTrimmed/win-x64` |

Les scripts publient `win-x64` et `linux-x64` par défaut. `PublishTrimmed` reste faux dans Development et Release. Release conserve `SelfContained=true`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `EnableCompressionInSingleFile=true` et `PublishReadyToRun=false`. Les bibliothèques natives sont incorporées ; les assemblages managés restent chargés depuis le bundle. Le mécanisme d’extraction suit les [règles .NET pour le single-file](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview#native-libraries).

L'expérience ne remplace jamais la sortie Release des scripts. Commande explicite :

```sh
dotnet publish src/GAIP.Desktop -c Release -r win-x64 -p:PublishProfile=ExperimentalTrimmed
```

Ce profil importe Release puis active `PublishTrimmed=true`, `TrimMode=full` et `TrimmerSingleWarn=false`. Aucun NativeAOT, aucune suppression d'avertissements. Le JSON métier, la configuration, les verrous, l'historique et les informations de cache utilisent des contextes System.Text.Json générés à la compilation ; les tests vérifient leur compatibilité avec le format existant.

Les icônes Linux et `install-desktop.sh` sont des fichiers facultatifs d’intégration au menu ; ils ne sont pas nécessaires au démarrage de `GAIP`. Les scripts refusent une sortie Release contenant des PDB, DLL, SO ou manifestes de runtime séparés. Ils ne suppriment aucune DLL après publication. Les anciens dossiers `artifacts/win-x64` et `artifacts/linux-x64` ne sont plus les sorties des scripts.

## Dépendances de développement

- `Avalonia.Diagnostics` n’est pas référencé directement ou transitivement ; aucun appel à `AttachDevTools` dans G@IP. Aucune référence n’a été ajoutée pour la retirer ensuite.
- `Avalonia.DesignerSupport` est livré dans le package principal Avalonia 12.1.0, pour le concepteur. L’inspection des références d’assemblages Avalonia et du code G@IP ne trouve aucune dépendance à son exécution normale. Le profil Release exclut ses références de compilation/runtime après `ResolvePackageAssets`, puis ses éventuels fichiers de publication avant bundling. Il est absent du manifeste de dépendances Release et conservé en Development.
- `Avalonia.Desktop` est un package général qui référence Win32, X11 et Native. Pour `win-x64`, G@IP référence directement Win32, Skia et HarfBuzz ; pour `linux-x64`, X11, Wayland, Skia et HarfBuzz. Les compilations sans RID et les autres RID conservent le package général. Les choix reposent sur le RID cible, et non sur l'OS de la machine de compilation.
- `Program` initialise les mêmes services que les branches correspondantes de `UsePlatformDetect()` : Win32 sous Windows, X11 par défaut sous Linux, avec Wayland natif uniquement pour une session `XDG_SESSION_TYPE=wayland` sans override `GAIP_USE_X11=1`. La compilation sans RID conserve `UsePlatformDetect()`.
- Les bundles Windows ne contiennent plus `Avalonia.X11`, `Avalonia.Wayland`, `Avalonia.FreeDesktop`, `NWayland` ni `Tmds.DBus.Protocol`. Les bundles Linux conservent ces dépendances. `Avalonia.Metal` provient du package principal Avalonia et reste une référence directe de `Avalonia.Skia` : il est conservé, y compris après trimming (13 824 octets non compressés).
- `Microsoft.DiaSymReader.Native.amd64.dll`, `createdump.exe`, `mscordaccore.dll`, sa variante versionnée et `mscordbi.dll` sont des composants de symboles, dumps et débogage du runtime. Le `RuntimeList.xml` du runtime pack .NET les marque `DropFromSingleFile=true` ; `Microsoft.NET.Publish.targets` les exclut automatiquement quand `PublishSingleFile=true`. Ils sont absents des bundles Windows contrôlés. Aucune exclusion supplémentaire ni désactivation globale des diagnostics n'est nécessaire ; la publication Development à fichiers séparés les conserve.
- Aucun fichier `Default*.pubxml` n'appartient au projet. Les profils génériques trouvés appartiennent au SDK local, sous `.tools/dotnet/sdk/10.0.401/Sdks/Microsoft.NET.Sdk.Publish/targets/PublishProfiles/`, et restent intacts.
- Aucune DLL n'est supprimée manuellement dans les artefacts.

## Signature Windows

La chaîne de Release peut signer les exécutables Windows standard et trimmed avec SignPath avant la création des archives ZIP. L’intégration reste désactivée tant que la variable de dépôt `SIGNPATH_ENABLED` n’est pas égale à `true`.

Quand elle est activée, le workflow :

1. dérive la version du tag `vMAJOR.MINOR.PATCH` et l’intègre dans les métadonnées PE ;
2. publie les deux exécutables Windows ;
3. charge les exécutables non signés comme artefact GitHub Actions ;
4. soumet cet artefact à `signpath/github-action-submit-signing-request@v3` ;
5. attend le résultat signé et vérifie localement que les deux signatures Authenticode sont valides ;
6. construit les ZIP uniquement à partir des exécutables signés.

Si SignPath est activé, un échec de signature bloque la publication Windows : il n’y a pas de repli silencieux vers un exécutable non signé.

Configuration GitHub attendue :

- secret `SIGNPATH_API_TOKEN` ;
- variables `SIGNPATH_ENABLED`, `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG` et `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`.

La configuration d’artefact SignPath correspondante est versionnée dans `.signpath/artifact-configurations/windows-executables.xml`. La politique publique est décrite dans [SIGNING.md](../SIGNING.md).

## Vérifications de démarrage

`scripts/smoke-windows.ps1` copie seulement `GAIP.exe` dans un dossier de test, démarre le processus en fenêtre masquée, détecte sa fenêtre Win32 G@IP par PID, attend puis demande une fermeture normale. Il utilise la configuration du compte courant et ne déclenche aucune édition métier.

`scripts/smoke-linux.py <GAIP> <dossier-logs>` copie seulement `GAIP` dans un dossier temporaire et isole données, configuration et extraction .NET. Sous X11/WSLg, il détecte la fenêtre du PID via Xlib, vérifie l’initialisation de la base de test puis envoie `WM_DELETE_WINDOW`. Aucun outil de bureau supplémentaire n’est requis, hormis Python 3 et libX11. Le script ne force pas `GAIP_USE_X11`, afin de tester la détection WSLg.

Les deux tests exigent un code de sortie 0 et ne ferment que le processus qu'ils ont démarré. Résultats et stderr sont sous `artifacts/smoke/`. Le test Linux vérifie WSLg/X11 ; il ne remplace pas une validation sur une vraie session Wayland GNOME/KDE.

Tailles, avertissements et limites de validation : [rapport du 19 septembre 2026](PUBLICATION_SIZE.md).
