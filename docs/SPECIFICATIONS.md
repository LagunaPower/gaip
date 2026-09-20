# Spécifications implémentées — V1

## Métier

G@IP gère sites → VLAN indépendants → zéro ou un sous-réseau IPv4 → attributions IP. Aucun objet Equipment, inventaire, SQL, IPv6, service réseau ou compte applicatif.

- Code site unique sans distinction de casse, nom requis. IDs techniques stables, invisibles.
- VID 1–4094, unique par site. Création multi-sites transactionnelle, puis indépendance totale.
- Sous-réseaux globalement non chevauchants. CIDR normalisé à la saisie/import ; calculs réseau/première/dernière/broadcast/compteurs non stockés.
- Passerelle facultative, utilisable, commentaire facultatif. Ligne spéciale, comptée utilisée, absente des attributions normales.
- IP globalement unique, utilisable, distincte de la passerelle. Hostname ou description obligatoire. Hostnames dupliquables, casse conservée. Aucun statut stocké.
- Description/commentaire 500 caractères sur une ligne ; hostname 255 ; nom 150 ; code 50.
- VLAN et IP triés numériquement.
- Suppression site sans VLAN seulement ; suppression VLAN autorisée dès qu’il ne contient plus d’IP attribuée. Une passerelle seule n’est pas bloquante et est supprimée avec le VLAN. Libération d’une IP avec confirmation. Pas de cascade site → VLAN ni VLAN → IP.
- Dès qu’un sous-réseau contient au moins une IP attribuée, son CIDR ne peut plus être modifié ni retiré tant que toutes les IP n’ont pas été libérées ; le champ CIDR est alors désactivé dans la fiche VLAN. Une passerelle seule ne fige pas le CIDR ; elle doit rester utilisable dans le nouveau réseau ou être ajustée/supprimée.
- /31 et /32 admis sans IP utilisable. /0 calculé sans débordement ni énumération intégrale.
- Multicast IPv4 : adresse de groupe unique dans `224.0.0.0/4`, nom requis et description facultative. Chaque groupe contient zéro ou plusieurs flux ; le port UDP 1–65535 est unique dans le groupe, avec contenu requis, description, sources choisies parmi les IP attribuées et VLAN associés. Un groupe ne peut être supprimé qu’après suppression de ses flux.

## Interface

Cartes responsives avec une largeur minimale cible de 430 px et un maximum configurable de 1 à 8 colonnes (3 par défaut), tous les VLAN affichés sans scroll interne. Le nombre de colonnes s’adapte à la largeur disponible sans dépasser la préférence locale. Les rangées se répartissent la hauteur disponible afin d’éviter une grande zone vide sous la dernière rangée ; les cartes d’une même rangée ont la même hauteur. Les actions « Modifier le site » / « Ajouter un VLAN » restent ancrées en bas et sont centrées. À l’accueil, les lignes VLAN affichent le CIDR sans le masque décimal ; celui-ci reste disponible dans la fiche réseau. Les champs de recherche possèdent une croix de remise à zéro en un clic. Le masque IPv4 décimal est calculé, jamais stocké. Le logo officiel est intégré, son PNG source conservé et des icônes Windows/Linux générées.

Cartes de sites espacées de 8 px. Configuration contient trois onglets : Stockage & synchronisation, Affichage, Données & export. L’onglet Affichage regroupe thème, limite de colonnes et classement des sites par glisser-déposer avec repère d’insertion et défilement aux bords, ou par Monter/Descendre. Enregistrer l’affichage publie le classement s’il a changé et sauvegarde les préférences locales ; Annuler les laisse inchangés. Ordre commun à la base ; en mode partagé, son verrou est acquis automatiquement au moment de l’enregistrement. Tri par code sans préférence ; nouveaux sites en fin de liste après un classement enregistré. Aucun champ numérique ajouté à la fiche du site.

Fiche VLAN : seules passerelle et attributions visibles par défaut. Case « Afficher les adresses libres » pour toute la plage utilisable, sans réseau/broadcast. Liste virtualisée à défilement continu, sans pagination ; un /22 affiche 1022 adresses. Un clic gauche sélectionne une ligne. Un double-clic sur une adresse ouvre sa fiche (attribution si elle est libre, modification si elle est utilisée) ; un double-clic sur la passerelle est sans effet. Le clic droit sur une IP attribuée propose « Modifier » ou « Libérer » ; sur une IP libre, il propose « Affecter ». Parcours intégral jusqu’au /12 inclus ; au-delà, consultation des attributions et recherche d’une IP libre exacte. Recherche globale sur sites, VLAN, CIDR, IP, hostnames, descriptions et passerelles, 1 000 résultats affichés maximum.

Les libellés de création sont textuels, sans préfixe « + ». Dans la fiche VLAN, seul « Ajouter une IP » est proposé comme ajout. Ce bouton est désactivé lorsqu’il n’existe plus d’adresse libre. Le formulaire préremplit l’adresse avec la première IP libre connue et conserve « Prochaine libre » pour la recalculer ; ce bouton est lui aussi désactivé lorsque le sous-réseau est plein. CSV exporte uniquement le VLAN affiché et ses attributions. Historique filtre ses événements (dont modifications/libérations d’IP), avant la limite de 1 000 résultats, et masque les détails des autres VLAN.

Validations métier en direct, calculs réseau et passerelle réévalués pendant la saisie. Une validation de formulaire = une publication ; aucun changement en attente. Après ajout ou suppression d’un site ou d’un VLAN, l’interface revient à l’accueil complet. Thèmes système/clair/sombre. Une seule instance de G@IP peut être active par utilisateur sur une machine, toutes sessions de cet utilisateur confondues ; un autre utilisateur de la même machine peut lancer sa propre instance. Un second lancement pour le même utilisateur affiche « G@IP est déjà en cours d’exécution. » puis se ferme sans ouvrir une seconde fenêtre principale.

## Stockage partagé

Base JSON versionnée autoritaire, cache utilisateur validé SHA-256 associé au chemin partagé. Hors ligne lecture seule avec cache valide. Local : aucune synchronisation réseau ni edit.lock.

Un rédacteur à la fois. L’interface n’expose plus de mode modification manuel : pour chaque écriture, elle actualise d’abord la base, acquiert automatiquement le verrou atomique, publie puis le libère immédiatement. Le verrou conserve identité, hash/révision de départ et heartbeat pour les cas de session technique ou de récupération ; il n’expire jamais automatiquement. Force-unlock dans la gestion avec saisie explicite et audit. Publication recontrôlant le verrou sous la même garde filesystem que force-unlock. Si une IP a été attribuée par un autre poste depuis l’ouverture du formulaire, la revalidation sur la base actualisée rejette la seconde attribution sans écrasement.

Avant publication : validation, hash attendu, sauvegarde vérifiée. Temporaire complet, remplacement dans le même dossier, relecture/hash. Révision +1. Cache/historique, avertissement si leur mise à jour échoue après publication. L’historique est un journal JSONL compact de différences champ par champ ; il ne contient plus de snapshots complets de la base. Le filtrage par VLAN repose directement sur les IDs portés par les changements. La fenêtre d’historique charge au maximum 1 000 actions et propose une recherche locale instantanée sur date, révision, action, cible, utilisateur/machine et détails des changements. Rétention des sauvegardes 30 par défaut.

La configuration peut être exportée en JSON depuis la fenêtre Configuration afin d’être distribuée sur d’autres postes. En mode partagé, le cache local conserve `gaip-data.json` et `history.jsonl` avec leurs SHA-256 et le chemin source. Le diagnostic propose une restauration de secours de la base et de l’historique lorsque les fichiers du partage ont été supprimés accidentellement. La restauration refuse tout écrasement si `gaip-data.json`, `history.jsonl` ou `edit.lock` existe déjà. Le dossier `backup/` est conservé ; si une sauvegarde standard porte une révision supérieure à celle du cache, la restauration automatique est refusée afin d’éviter un retour à un état ancien.

## CSV

UTF-8, BOM en export, séparateur configurable (`;` par défaut). Guillemets et échappements CSV, LF/CRLF. Les valeurs métier multilignes sont refusées.

`vlans.csv` : `site_code;site_name;vid;vlan_name;vlan_description;cidr;gateway;gateway_comment`.

`addresses.csv` : `site_code;vid;ip;hostname;description`.

L’import VLAN crée les sites manquants ; pour un site existant son nom est conservé. Les VLAN sont créés/mis à jour par clé site/VID en conservant les IP. Un import VLAN ne peut pas modifier ou retirer le CIDR d’un VLAN qui possède déjà des IP attribuées. L’import IP crée/met à jour l’attribution du VLAN. Doublons dans un fichier refusés, aucune suppression des lignes absentes. L’aperçu affiche toutes les erreurs détectées ; revalidation sur la base courante à la publication. Une erreur empêche tout le fichier.

L’export complet écrit les deux CSV ; les passerelles restent dans le fichier VLAN. Ce format n’est pas une sauvegarde intégrale du modèle.

## Excel

L’export Excel produit un classeur `.xlsx` complet. Le premier onglet, **Sites et VLAN**, liste tous les sites et VLAN ; la cellule VLAN contient un lien hypertexte vers l’onglet du réseau lorsqu’un sous-réseau est défini.

Chaque sous-réseau possède un onglet dédié. Les informations réseau (site, VLAN, description, CIDR, masque, réseau, broadcast, première/dernière IP utilisable, passerelle, commentaire et compteurs) sont affichées en haut de feuille. Le tableau contient toutes les adresses IPv4 utilisables, y compris la passerelle, avec l’état `PASSERELLE`, `UTILISÉE` ou `LIBRE`, le hostname et la description.

Un VLAN sans sous-réseau reste visible dans l’onglet principal mais n’a pas d’onglet réseau. Un réseau dépassant la capacité maximale d’une feuille Excel est refusé avec un message explicite.

## Changements de mode

Local → partagé : base existante ou initialisation expressément sélectionnée, seulement si absente. Partagé → local : copie base/cache ou base vide, confirmation et sauvegarde préalable si une base locale existe. Aucune fusion automatique.
