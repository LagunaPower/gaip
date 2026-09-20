# Packaging G@IP

Les paquets installables sont construits à partir de la publication **ExperimentalTrimmed** afin de limiter leur taille. Les archives portables standard et trimmed restent publiées en parallèle.

## Windows MSI

Le MSI est décrit par `windows/Package.wxs` et construit avec WiX Toolset 6.0.2. Il installe `GAIP.exe` dans `Program Files\G@IP`, crée un raccourci dans le menu Démarrer, apparaît dans les applications installées et gère les mises à niveau majeures.

Le MSI est généré à partir de l'exécutable trimmed. Lorsque SignPath est activé, le binaire inclus est déjà signé et le MSI est ensuite signé lui-même avant sa publication.

## Linux DEB / RPM

`linux/build-packages.sh` construit :

- `gaip_<version>_amd64.deb` ;
- `gaip-<version>-1.x86_64.rpm`.

Les deux installent le binaire autonome sous `/usr/lib/gaip/GAIP`, un lanceur `/usr/bin/gaip`, l'entrée de menu sous `/usr/share/applications` et les icônes hicolor.

Les dépendances natives déclarées couvrent les dépendances .NET self-contained et Avalonia nécessaires sur les familles Debian/Ubuntu et RHEL/Fedora.
