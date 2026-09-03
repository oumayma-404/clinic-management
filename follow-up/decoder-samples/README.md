# Fichiers d'essai — les formats que l'application sait désormais afficher

Sept fichiers **réels** (téléchargés ou produits ici, jamais des octets bidon), un par cas que
`web/lib/files/decoders` doit couvrir. Déposez-les dans le tiroir d'un patient
(`/patients/<id>/files`) puis ouvrez chacun avec « Aperçu ».

| Fichier | Poids | Ce qu'il doit produire | Ce qu'il éprouve |
|---|---|---|---|
| `photo-intrabuccale-iphone.heic` | 287 Ko | **la photo à l'écran** | Le décodeur HEIC (libheif). C'est le format par défaut d'un iPhone : jusqu'ici il s'affichait en icône grise. Et une **vignette** dans la liste, construite à l'envoi. |
| `sourire-avant-traitement.heif` | 2,4 Mo | idem | Même décodeur, l'autre extension. Assez gros pour que la construction de la vignette soit visible. |
| `radio-retroalveolaire.tif` | 303 Ko | **la radio à l'écran** | Le décodeur TIFF (`utif2`), extension courte. |
| `cliche-scanner.tiff` | 799 Ko | idem | Extension longue — les deux doivent être reconnues. |
| `bon-labo-couronne-26.zip` | 485 Ko | **la liste de ce qu'il contient** (3 entrées) | La lecture de l'index d'archive : rien n'est décompressé, seuls les derniers kilo-octets sont lus. Contient un `.heic`, un `.tif` et un `.txt`, donc les noms affichés doivent être exacts. |
| `panoramique-haute-definition.tiff` | 31,6 Mo | selon le poste : au **coffre** s'il est apparié, sinon un refus qui dit pourquoi | **Au-dessus de la ligne des 25 Mo**, donc le catalogue l'envoie au coffre. C'est le cas qui prouve qu'un original du coffre s'ouvre depuis le disque local plutôt qu'en le redemandant au serveur (qui ne l'a jamais eu). |
| `etude-cbct-export.zip` | 34 Mo | la liste de ses 3 entrées, **lue depuis le coffre** | Même chose pour une archive : l'index est lu sur place, sans télécharger 34 Mo ni les décompresser. |

## DICOM — cinq fichiers, un par branche du décodeur

| Fichier | Ce qu'il doit produire | Ce qu'il éprouve |
|---|---|---|
| `radio-jpeg-encapsule.dcm` | **l'image à l'écran** | Pixels encapsulés en **JPEG Baseline** : le fragment *est* un JPEG, décodé par le navigateur lui-même. ⚠️ Sa table d'offsets est **vide**, ce qui est le cas ordinaire et exige l'autre lecteur de `dicom-parser`. |
| `coupe-cbct-hounsfield.dcm` | une coupe anatomique correcte | Brut 16 bits **signé**, rescale −1024 (unités Hounsfield), et **aucune fenêtre** dans le fichier → fenêtre dérivée de l'étendue de l'image. ⚠️ Son préambule commence par `II*\0`, le marqueur TIFF — voir plus bas. |
| `coupe-avec-fenetrage.dcm` | idem | Brut 16 bits signé avec la fenêtre 600/1600 **du fichier**, donc l'autre moitié de la même branche. Préambule TIFF lui aussi. |
| `photo-couleur-rgb.dcm` | 3×3 pixels de couleur | Brut RGB 8 bits — la branche couleur, qui ne fenêtre rien du tout. |
| `radio-jpeg-12-bits.dcm` | **« ce format ne s'affiche pas »** | JPEG Extended 12 bits : aucun navigateur ne le décode. C'est le **refus** qui est éprouvé ici, et un décodeur dont on n'essaie jamais les refus est un décodeur à moitié vérifié. |

### Les cinq VRAIS clichés cliniques — commencez par ceux-là

Les cinq ci-dessus éprouvent les *branches* du décodeur ; ceux-ci sont de vraies images de vrais appareils, et
c'est avec eux qu'on juge si l'aperçu est bon.

| Fichier | Poids | Ce que c'est |
|---|---|---|
| **`radiographie-thorax-mono1.dcm`** | 379 Ko | **Le meilleur essai.** Une vraie radiographie (modalité CR — la même famille qu'un cliché dentaire), 440×440, stockée en **MONOCHROME1**, c'est-à-dire inversée. Un décodeur qui oublie l'inversion la rend en négatif — os sombres, air clair — ce qui se lit comme un CONSTAT et non comme un bogue. Elle doit sortir côtes et rachis **clairs**, champs pulmonaires **sombres**. |
| `coupe-ct-512.dcm` | 513 Ko | Coupe de scanner 512×512, **VR implicite** (l'autre encodage brut), fenêtre 20/400 du fichier. |
| `irm-cerebrale-256.dcm` | 206 Ko | IRM 256×256 avec sa propre fenêtre. |
| `etude-16-images.dcm` | 1 Mo | Une étude de **16 images**. Seule la première s'affiche, et le bandeau le dit. |
| `coupe-jpeg-2000.dcm` | 98 Ko | **JPEG 2000** — aucun navigateur ne le décode. Doit afficher « ce format ne s'affiche pas », ce qui est vrai et n'est pas une panne. C'est le cas réel le plus probable parmi les formats non pris en charge. |

Provenance : jeu de données d'essai du projet **dwv** (`ivmartel/dwv`, GPL, `tests/data`), lui-même agrégeant des
échantillons publics GDCM / OsiriX / BBMRI. Aucun patient identifiable.

```bash
BASE=https://github.com/ivmartel/dwv/raw/master/tests/data
curl -L -o radiographie-thorax-mono1.dcm $BASE/gdcm-CR-MONO1-10-chest.dcm
curl -L -o coupe-ct-512.dcm              $BASE/dicompyler-ct.0.dcm
curl -L -o irm-cerebrale-256.dcm         $BASE/bbmri-53323851.dcm
curl -L -o etude-16-images.dcm           $BASE/multiframe-test1.dcm
curl -L -o coupe-jpeg-2000.dcm           $BASE/osirix-toutatix-100.dcm
```

⚠️ **`etude-16-images.dcm` a fait sortir un manque réel**, et c'est pour cela qu'il est dans la liste : sur le
**chemin rapide** — celui qui peint l'appoint enregistré, donc le chemin ordinaire — rien n'analyse le fichier,
donc le nombre d'images est réellement inconnu. Le bandeau ne disait donc rien du tout, et un lecteur devant une
coupe sur seize n'avait aucune raison de soupçonner les quinze autres. Il dit maintenant « si l'étude contient
plusieurs images, seule la première est affichée » ; quand le décodeur tourne pour de bon, il donne le compte exact.

⚠️ **Les deux `coupe-*` ont révélé un vrai défaut du produit** : leur préambule DICOM porte le marqueur TIFF
(la norme laisse ces 128 octets libres, et certains exportateurs y logent l'en-tête d'un autre format pour
qu'un fichier s'ouvre dans deux logiciels). Le validateur d'envoi demandait « ces octets prétendent-ils être un
autre format ? » **avant** de remarquer que le marqueur `DICM` du fichier était là, à l'octet 128 — donc le
serveur refusait deux DICOM parfaitement valides en disant « le fichier a peut-être été renommé ».

⚠️ **`radio-jpeg-encapsule.dcm` est FABRIQUÉ ici**, par `build-dicom-sample.mjs`, et pas téléchargé : aucun
jeu d'essai DICOM public joignable ne porte du JPEG Baseline, et les deux qui l'étaient sont du JPEG Extended
12 bits — c'est-à-dire la branche d'échec, pas celle du succès. C'est une pièce d'essai : l'image à l'intérieur
est générée, aucun patient n'y figure.

## Ce qu'il faut regarder

1. **La liste elle-même.** Avant ce lot, aucun fichier hébergé ne portait de vignette — la vignette
   n'existait que pour le coffre. Le HEIC et le TIFF doivent apparaître **en image** dans la grille,
   pas en icône.
2. **Les fichiers déjà présents** dans le tiroir (déposés avant ce lot) n'ont pas de vignette
   enregistrée : les **petites** images (< 2 Mo) doivent quand même s'afficher, servies par la route
   d'aperçu à partir de l'original. Les grosses gardent leur icône, c'est voulu.
3. **Le ZIP** ouvre sur une liste, pas sur « ce format ne s'affiche pas ».
4. **Le fichier du coffre** ne dit jamais « échec » sur un poste qui ne l'a pas : il dit où il est.

## Provenance

- `photo-intrabuccale-iphone.heic`, `sourire-avant-traitement.heif`, `cliche-scanner.tiff` —
  filesamples.com (échantillons publics).
- `radio-retroalveolaire.tif` — collection TIFF publique de J. Burkardt (Univ. of South Carolina).
- `panoramique-haute-definition.tiff`, les deux `.zip` — produits ici, parce qu'un fichier
  franchissant la ligne des 25 Mo ne se télécharge pas commodément et que l'archive devait contenir
  précisément les formats à éprouver.

⚠️ Ce sont des **images de démonstration**, pas des clichés cliniques : aucun patient réel n'y figure.

## Les fichiers ne sont pas dans git

Seuls ce `README.md` et `build-samples.mjs` le sont — 71 Mo d'images d'échantillon dans l'historique est un coût
permanent pour quelque chose qui se reconstitue en une minute. Les deux téléchargés :

```bash
curl -L -o photo-intrabuccale-iphone.heic  https://filesamples.com/samples/image/heic/sample1.heic
curl -L -o sourire-avant-traitement.heif   https://filesamples.com/samples/image/heif/sample1.heif
curl -L -o cliche-scanner.tiff             "https://filesamples.com/samples/image/tiff/sample_640%C3%97426.tiff"
curl -L -o radio-retroalveolaire.tif       https://people.math.sc.edu/Burkardt/data/tif/at3_1m4_01.tif
```

Puis les trois fabriqués (le gros TIFF et les deux archives) :

```bash
node build-samples.mjs
node build-dicom-sample.mjs
```

Et les quatre DICOM téléchargés (jeu d'essai de pydicom, MIT) :

```bash
BASE=https://github.com/pydicom/pydicom/raw/main/src/pydicom/data/test_files
curl -L -o coupe-cbct-hounsfield.dcm  $BASE/CT_small.dcm
curl -L -o coupe-avec-fenetrage.dcm   $BASE/MR_small.dcm
curl -L -o photo-couleur-rgb.dcm      $BASE/SC_rgb_small_odd.dcm
curl -L -o radio-jpeg-12-bits.dcm     $BASE/JPEG-lossy.dcm
```
