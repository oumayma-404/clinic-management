# `src/jsonld/` — les données structurées

Un fichier par page, **nommé comme la page** : `index.json` → `index.html`,
`odontogramme.json` → `odontogramme.html`. Fichier absent = aucun balisage, et
`{{JSONLD}}` est remplacé par du vide. Rien à déclarer dans le front-matter.

`build.mjs` reparse le JSON puis le re-sérialise : **un fichier invalide fait échouer le
build**, jamais la page en ligne. Tout `<` est échappé en `<` à la sortie — une
valeur contenant une balise de fermeture de script fermerait le bloc et casserait le
reste du `<head>`.

⚠️ Un `.json` n'accepte pas de commentaire. Les décisions vivent donc ici.

## Les quatre règles qui coûtent le balisage entier si on les casse

### 1 · Aucune `aggregateRating` sans des avis réels et **visibles sur la page**

C'est la violation la plus courante et Google ne la traite pas comme une erreur mineure :
le balisage de la page est rejeté *en entier*, pas seulement la note. Le jour où il y a de
vrais témoignages affichés sur le site, la note pourra être déclarée — pas avant.

### 2 · Aucun `offers` tant que le prix n'est pas public

Le résultat enrichi « application » demande `offers.price` **et** `priceCurrency`. Le tarif
n'est pas arrêté (la section « L'essai et le prix » est parquée dans
`src/_parked/essai.html` pour cette raison), et écrire `"price": "0"` pour remplir le champ
est faux : le logiciel n'est pas gratuit. Donc pas de nœud `offers` du tout, et
`isAccessibleForFree: false` dit la vérité sans inventer de chiffre.

Le jour où le prix est publié : ajouter `offers` avec `priceCurrency: "TND"`, et
`hasMerchantReturnPolicy` / l'essai de 30 jours en `Offer.eligibleDuration`.

### 3 · Le texte d'une `Question` / `Answer` est recopié **mot pour mot** depuis la page

Google compare le balisage au texte visible. Une réponse qui reformule, complète ou
raccourcit la réponse affichée fait rejeter le `FAQPage`. Les quatre questions viennent
de la section `#faq` de `src/pages/index.html` : **si tu modifies une réponse là-bas, tu
modifies `index.json` dans le même commit.**

Les `&nbsp;` du HTML sont écrits en espace normale ici — Google normalise les espaces, et
un U+00A0 dans un `.json` est une source d'ennuis d'encodage pour rien.

⚠️ À savoir : depuis 2023, Google n'**affiche** plus le résultat enrichi FAQ que pour les
sites gouvernementaux et de santé reconnus. Le balisage reste utile — il dit à Google de
quoi la page parle — mais il ne faut pas attendre les accordéons dans les résultats.

### 4 · Les URL sont absolues et vraies

Chaque `@id`, `url`, `image` et `screenshot` doit répondre 200 sur `https://apexa.tn`.
Une capture citée mais absente de `dist/assets/img/` est une erreur silencieuse : Google
ignore le champ et personne ne le voit. Les quatre `screenshot` cités sont les captures
téléphone, qui sont bien sur la page ; les captures bureau (`dashboard.webp`,
`agenda.webp`, …) sont construites mais n'apparaissent sur aucune page.
