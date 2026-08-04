# Design — Site vitrine « Gestion Clinique »

**Status: DRAFT (v3)** — exécution de site produit haut de gamme.

**Voir les maquettes :** <https://claude.ai/code/artifact/b9e3239a-5e52-4269-a2cc-e7095f2e9a9e>
Les cinq pages dans un lien. Sélecteur de page en bas, bascule clair/sombre dans la barre du haut.

| Page | Fichier |
|---|---|
| Accueil | [`mockups/01-accueil.html`](mockups/01-accueil.html) |
| Fonctionnalités | [`mockups/02-fonctionnalites.html`](mockups/02-fonctionnalites.html) |
| Tarifs | [`mockups/03-tarifs.html`](mockups/03-tarifs.html) |
| Cloud ou Local | [`mockups/04-cloud-vs-local.html`](mockups/04-cloud-vs-local.html) |
| Contact | [`mockups/05-contact.html`](mockups/05-contact.html) |
| Jetons et primitives | [`mockups/shared-styles.css`](mockups/shared-styles.css) |
| Comportements | [`mockups/site.js`](mockups/site.js) |

Versions précédentes conservées : [`mockups/v1-dense/`](mockups/v1-dense/) · [`mockups/v2-airy/`](mockups/v2-airy/)

## Les trois tentatives

| | v1 | v2 | **v3** |
|---|---|---|---|
| Retour | « trop chargé, trop d'infos » | « pas bien du tout, la v1 était mieux » | — |
| Pages | 5 | 3 | **5** (comme v1) |
| Graisse des titres | 650 | **300 — l'erreur** | **600, crénage −0,028em** |
| Rythme | filets horizontaux | blanc seul | **bandes claires / presque noires** |
| Animation | aucune | aucune | **révélation au défilement + cadre qui grandit** |
| Réserves honnêtes | bandeaux ambre criants | petit texte gris | **notes de bas de page numérotées** |

**Ce que la v2 avait faux.** Deux choses, et la seconde était la vraie faute :

1. *Trop vidée.* J'ai lu « trop d'infos » comme « supprime des infos », alors qu'un site produit haut de
   gamme est **dense** — il ne le paraît pas parce que chaque section est une seule idée à grande échelle.
   Le remède n'était pas de couper le contenu mais de l'espacer et de le hiérarchiser.
2. *Les titres en graisse 300.* Un titre maigre en très grand paraît fragile, pas élégant. Les sites
   produit de référence font l'inverse : **graisse 600 et crénage négatif serré**. C'est ce couple qui
   produit l'impression de qualité, pas la finesse du trait.

## La direction v3

**Couleur.** Les neutres d'un site produit haut de gamme, assumés parce que c'est la référence demandée —
`#fbfbfd` / `#1d1d1f` / `#6e6e73` / `#d2d2d7`. L'accent reste **exactement** le `--primary` du produit
(`oklch(0.49 0.105 188)`), remonté à `oklch(0.72 0.10 187)` sur fond sombre où la valeur d'origine
devient sourde. La bande sombre (`#080c0e`) a ses propres jetons fixes : c'est un **décor de section**,
pas le thème de la page, donc elle ne bouge pas avec la bascule clair/sombre.

**Typographie — le crénage suit la taille**, jamais une valeur unique :

| Rôle | Taille | Graisse | Crénage |
|---|---|---|---|
| Titre de héros | `clamp(40px, 7vw, 88px)` | 600 | **−0,028em** |
| Titre de section | `clamp(30px, 4.4vw, 56px)` | 600 | −0,022em |
| Accroche | `clamp(18px, 1.9vw, 25px)` | 400 | −0,012em |
| Texte courant | 17px | 400 | −0,008em |
| Petit texte / notes | 12,5px | 400 | **+0,005em** |

Grand → serré, petit → légèrement ouvert. L'interlignage suit l'inverse : 1,04 sur le héros, 1,6 sur le
texte courant. Police système d'abord — elle embarque déjà l'optical sizing et les tables de crénage, et
aucune police n'est liée (la CSP des Artifacts bloque les CDN ; un lien mort donnerait un repli silencieux).

**Mise en page.** Texte à 980 px, visuels à 1240 px. Rythme vertical `clamp(76px, 9.5vw, 132px)`. La
structure vient de **l'alternance des bandes** : héro clair → cartes → **sombre** → odontogramme →
**sombre** → faits → appel. Cadres produit à 28 px de rayon avec une ombre profonde, sans fausse barre
de navigateur.

**Mouvement — quatre choses, et rien de plus.** Élégant veut dire peu et bien :

1. **Révélation au défilement** : opacité + 26 px de montée, décalée de 70 ms entre les enfants d'un
   groupe. Une seule fois — rien ne re-disparaît.
2. **Le cadre du héros grandit** de 0,94 à 1 en entrant, lié à la position de défilement.
3. **La barre de navigation** est un matériau translucide (`backdrop-filter`) : le contenu défile
   dessous, et le filet n'apparaît qu'une fois la page bougée — un filet n'a de sens qu'au recouvrement.
4. **Réponse au pointer-down** : `scale(0.97)` instantané sur les boutons, jamais au relâché.

Détails qui font la différence : on n'anime que `transform` et `opacity` ; le défilement est lu dans un
`requestAnimationFrame` et jamais dans l'écouteur ; le survol des cartes est conditionné à
`(hover: hover) and (pointer: fine)` pour qu'un état de survol ne reste pas collé après un toucher.
Aucun carrousel, aucun compteur animé, aucun parallaxe de fond.

**Accessibilité du mouvement.** `prefers-reduced-motion` ne veut pas dire « aucun retour » : la montée
devient un fondu de 240 ms, les décalages tombent à zéro, le grandissement est coupé — et si le réglage
change en cours de session, le cadre reprend sa taille sans rechargement.
`prefers-reduced-transparency` rend la barre opaque.

## Les cinq pages

**01 · Accueil** — héro centré, puis la capture du tableau de bord (barre latérale, quatre KPI, courbe) ;
six cartes de module ; bande sombre « La connexion tombe. Le cabinet continue. » ; l'odontogramme FDI
complet ; bande sombre CNAM avec le peigne IDU ; trois faits (3 décimales, 24 gouvernorats, 100 % en
français) ; prix et appel.

**02 · Fonctionnalités** — cinq blocs alternés texte/visuel avec deux bandes sombres : agenda semaine,
dossier patient à sept onglets, devis avec l'état de chaque acte, les chiffres du mois, puis six modules
secondaires.

**03 · Tarifs** — trois formules, bascule mensuel/annuel, licence locale en achat unique, et un
comparatif ramené à **16 lignes** en deux groupes (« Partout » / « Selon la formule ») au lieu des 35
lignes de la v1. FAQ en accordéon.

**04 · Cloud ou Local** — les deux modes à poids égal avec leur topologie réseau dessinée en HTML, la
liste d'états hors ligne, le tableau de décision en six questions, les six étapes d'installation.

**05 · Contact** — formulaire à droite, coordonnées et objections à gauche. Le mode de déploiement est
trois pastilles, pas une liste déroulante : c'est la question qui décide du prix et de l'installation.
L'état « envoyé » est inclus.

## Honnêteté du discours — en notes de bas de page

Toutes les réserves de la v1 sont là ; ce qui a changé, c'est **la forme**. Elles sont devenues des
**notes numérotées** avec appels en exposant dans le texte — la convention des sites produit haut de
gamme, lisible pour qui la cherche sans casser la page. Vérifiées dans le code :

| Sujet | Ce qui est écrit |
|---|---|
| Agenda Google | Envoi **automatique** vers Google ; import depuis Google **en un clic**. Pas de tâche planifiée. |
| SMS / WhatsApp | **Rien n'est fourni** : votre passerelle SMS, votre compte WhatsApp Business, modèle approuvé par Meta. Envois facturés par eux. |
| El Fatoora | Prêt côté logiciel ; transmission réelle = identifiants TTN + certificat. **Avoirs non transmis.** |
| Sauvegarde | En un clic, **manuelle**, installation locale, administrateur. Aucune planification. |
| Multi-clinique | Un compte = une clinique. Pas de sélecteur multi-cabinets. |
| Installateur | Vérifié par une procédure déroulée poste par poste, **pas** un téléchargement en libre-service. |
| Estimation CNAM | Aide à la saisie : ni enregistrée, ni imprimée. C'est la CNAM qui liquide. |
| Résumé IA du patient | **Absent du site** — l'endpoint existe mais aucun écran ne l'appelle. |

Toujours **aucun témoignage, aucun logo client, aucun « N cabinets nous font confiance »** : rien dans le
dépôt ne l'étaie. La preuve, sur ces pages, est ce que le produit fait.

## Note technique — la page de prévisualisation

`site-preview.html` réunit les cinq maquettes dans un document (jetons, styles et `site.js` inlinés,
la CSP des Artifacts bloquant toute requête externe). Deux pièges rencontrés, corrigés :

- Une page cachée n'a **aucune mise en page**, donc ses éléments ne peuvent pas être observés avant
  d'être affichés : il faut relancer l'initialisation à chaque changement de page.
- Ce qui rend `site.js` **réexécutable**. Sans garde-fou, la bascule de thème recevait un écouteur de
  plus à chaque relance : au deuxième passage, un clic la déclenchait deux fois et le thème ne changeait
  plus. Les écouteurs de fenêtre sont donc posés une seule fois, ceux des éléments marqués par
  `data-bound`, et l'état vit dans `window.__site` plutôt que dans une fermeture périmée. Les points
  d'accroche sont passés d'`id` à attributs `data-*`, parce qu'un id ne peut pas être unique quand cinq
  pages partagent un document.

## Reste à décider

1. **Le prix.** `120 / 290 / 3 900 DT` sont des placeholders, annotés comme tels sur la page Tarifs.
2. **Le nom.** « Gestion Clinique » est le `PRODUCT_NAME` du code, dont le commentaire dit qu'il sert de
   *repli* quand le nom de la clinique est inconnu. Ce n'est peut-être pas le nom commercial.
3. **Le logo.** Il n'existe pas : `web/public/` ne contient que les SVG du gabarit Next.js, et les quatre
   icônes déclarées dans `layout.tsx:20-36` sont **absentes du dépôt**. Les maquettes utilisent la marque
   réelle — l'icône lucide `Stethoscope`. Un vrai logo et des favicons restent à créer.
4. **Domaine, e-mail, téléphone** — exemples.
5. **Mentions légales** — les liens du pied de page ne pointent nulle part.

## Déviations par rapport à `/design-ui`

- **Pas de `spec.md`** : un site vitrine ne modifie rien dans `web/` ni `api/`.
- **Pas d'exploration navigateur** : aucun outillage dans ce dépôt (`agent-browser` absent, pas de script
  de démarrage). Repli de la compétence appliqué — identité visuelle dérivée par lecture des sources,
  comme dans `features/app-design-language/design.md`.

## Votre revue

Points sur lesquels votre avis compte le plus :

- Les **bandes sombres** — est-ce le bon rythme, ou faut-il en garder une seule sur l'accueil ?
- Les **animations** : assez présentes, ou encore trop discrètes ? (Elles sont volontairement au nombre
  de quatre ; on peut en ajouter, par exemple un moment collant où la capture reste et le texte défile.)
- La **densité** : la v3 revient au niveau d'information de la v1 — est-ce le bon dosage cette fois ?
