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
- Suppression site sans VLAN seulement ; VLAN sans attribution **ni passerelle** seulement. Libération d’une IP avec confirmation. Aucune cascade.
- Changement de CIDR revalidé avec toutes les attributions et la passerelle, affichage des adresses invalides.
- /31 et /32 admis sans IP utilisable. /0 calculé sans débordement ni énumération intégrale.

## Interface

Cartes sur 3/2/1 colonnes (seuils 1 500/980 pixels disponibles), tous les VLAN affichés sans scroll interne. Le masque IPv4 décimal est calculé et affiché avec le CIDR, jamais stocké. Le logo officiel est intégré, son PNG source conservé et des icônes Windows/Linux générées.

Cartes de sites espacées de 8 px. Configuration contient Général et Ordre d’affichage. Le second onglet classe les sites par glisser-déposer avec repère d’insertion et défilement aux bords, ou par Monter/Descendre. Enregistrer l’ordre publie ce classement, Annuler le laisse inchangé ; les réglages Général se sauvegardent dans leur onglet. Ordre commun à la base, verrou requis en mode partagé. Tri par code sans préférence ; nouveaux sites en fin de liste après un classement enregistré. Aucun champ numérique ajouté à la fiche du site.

Fiche VLAN : seules passerelle et attributions visibles par défaut. Case « Afficher les adresses libres » pour toute la plage utilisable, sans réseau/broadcast. Liste virtualisée à défilement continu, sans pagination ; un /22 affiche 1022 adresses. Parcours intégral jusqu’au /12 inclus ; au-delà, consultation des attributions et recherche d’une IP libre exacte. Recherche globale sur sites, VLAN, CIDR, IP, hostnames, descriptions et passerelles, 1 000 résultats affichés maximum.

Les libellés de création sont textuels, sans préfixe « + ». Dans la fiche VLAN, seul « Ajouter une IP » est proposé comme ajout, avec « Prochaine libre » dans le formulaire. CSV exporte uniquement le VLAN affiché et ses attributions. Historique filtre ses événements (dont modifications/libérations d’IP), avant la limite de 1 000 résultats, et masque les détails des autres VLAN.

Validations métier en direct, calculs réseau et passerelle réévalués pendant la saisie. Une validation de formulaire = une publication ; aucun changement en attente. Thèmes système/clair/sombre.

## Stockage partagé

Base JSON versionnée autoritaire, cache utilisateur validé SHA-256 associé au chemin partagé. Hors ligne lecture seule avec cache valide. Local : aucune synchronisation réseau ni edit.lock.

Un rédacteur, verrou atomique, heartbeat 10 s, identité et hash/révision de départ. Pas d’expiration automatique. Force-unlock dans la gestion avec saisie explicite et audit. Publication recontrôlant le verrou sous la même garde filesystem que force-unlock.

Avant publication : validation, hash attendu, sauvegarde vérifiée. Temporaire complet, remplacement dans le même dossier, relecture/hash. Révision +1. Cache/historique, avertissement si leur mise à jour échoue après publication. Rétention des sauvegardes 30 par défaut.

## CSV

UTF-8, BOM en export, séparateur configurable (`;` par défaut). Guillemets et échappements CSV, LF/CRLF. Les valeurs métier multilignes sont refusées.

`vlans.csv` : `site_code;site_name;vid;vlan_name;vlan_description;cidr;gateway;gateway_comment`.

`addresses.csv` : `site_code;vid;ip;hostname;description`.

L’import VLAN crée les sites manquants ; pour un site existant son nom est conservé. Les VLAN sont créés/mis à jour par clé site/VID en conservant les IP. L’import IP crée/met à jour l’attribution du VLAN. Doublons dans un fichier refusés, aucune suppression des lignes absentes. L’aperçu affiche toutes les erreurs détectées ; revalidation sur la base courante à la publication. Une erreur empêche tout le fichier.

L’export complet écrit les deux CSV ; les passerelles restent dans le fichier VLAN. Ce format n’est pas une sauvegarde intégrale du modèle.

## Excel

L’export Excel produit un classeur `.xlsx` complet. Le premier onglet, **Sites et VLAN**, liste tous les sites et VLAN ; la cellule VLAN contient un lien hypertexte vers l’onglet du réseau lorsqu’un sous-réseau est défini.

Chaque sous-réseau possède un onglet dédié. Les informations réseau (site, VLAN, description, CIDR, masque, réseau, broadcast, première/dernière IP utilisable, passerelle, commentaire et compteurs) sont affichées en haut de feuille. Le tableau contient toutes les adresses IPv4 utilisables, y compris la passerelle, avec l’état `PASSERELLE`, `UTILISÉE` ou `LIBRE`, le hostname et la description.

Un VLAN sans sous-réseau reste visible dans l’onglet principal mais n’a pas d’onglet réseau. Un réseau dépassant la capacité maximale d’une feuille Excel est refusé avec un message explicite.

## Changements de mode

Local → partagé : base existante ou initialisation expressément sélectionnée, seulement si absente. Partagé → local : copie base/cache ou base vide, confirmation et sauvegarde préalable si une base locale existe. Aucune fusion automatique.
