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
```
