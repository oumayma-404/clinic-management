# Référencement d'apexa.tn — à faire à la main

Ce que le code ne peut pas faire à ta place. **Dans cet ordre**, et seulement **après** que le
nouveau build est en ligne (sinon la Search Console mesure encore l'ancien canonique, celui qui
pointait sur github.io).

Requête visée : **« logiciel de gestion de cabinet dentaire »**, seule et avec « tunisie ».

---

## 1 · Google Search Console — la propriété

1. Va sur <https://search.google.com/search-console>.
2. Connecte-toi avec **le compte Google qui doit posséder le site pour de bon**. Celui-là devient
   propriétaire et c'est lui qui recevra les alertes. Note lequel c'est quelque part.
3. Regarde la liste des propriétés, en haut à gauche : **`apexa.tn` y est peut-être déjà.**
   Un enregistrement TXT `google-site-verification=zuSC9Mt6uj…` est **déjà dans la zone DNS** du
   domaine (vérifié le 14/09/2026), donc quelqu'un a déjà commencé — peut-être fini.
   - **Elle y est** → passe à l'étape 2.
   - **Elle n'y est pas** → continue.
4. **Ajouter une propriété** → prends la colonne de **gauche, « Domaine »**. Pas « Préfixe d'URL ».
   La colonne « Domaine » couvre `apexa.tn`, `www.apexa.tn`, `http` et `https` d'un seul coup ;
   « Préfixe d'URL » n'en couvre qu'un et tu te retrouves avec quatre propriétés à surveiller.
5. Saisis `apexa.tn` (sans `https://`, sans `www`).
6. Google affiche un enregistrement TXT à ajouter. **Compare-le à celui qui est déjà en DNS.**
   - **La même chaîne** → clique **Valider**. C'est fini.
   - **Une autre chaîne** → il faut l'ajouter chez OVH (le domaine est chez OVH Tunisie) :
     *OVH Manager → Domaines → apexa.tn → **Zone DNS** → Ajouter une entrée → **TXT***
     · sous-domaine : **laisse vide**
     · valeur : `google-site-verification=…` (celle que Google vient d'afficher)
     Puis attends 10 à 60 minutes et clique **Valider**.

> ⚠️ **Tu ajoutes une ligne, tu ne remplaces rien.** La zone contient déjà
> `v=spf1 include:mx.ovh.com -all` (l'e-mail du domaine) et `brevo-code:…` (l'envoi des
> messages du produit). Si tu écrases l'un des deux, l'e-mail de `contact@apexa.tn` tombe, et
> ça ne ressemblera pas du tout à une erreur de DNS.

---

## 2 · Le sitemap

*Sitemaps*, dans le menu de gauche → tape `sitemap.xml` → **Envoyer**.

Il contient les cinq pages, l'accueil en premier. Sous 24 h le statut doit lire « Réussite » avec
le nombre d'URL découvertes. S'il lit « Impossible de récupérer », ouvre
<https://apexa.tn/sitemap.xml> dans un navigateur : si la page s'affiche, c'est que Google n'a pas
encore repassé, attends.

---

## 3 · Forcer le premier passage

*Inspection de l'URL* — c'est la barre de recherche en haut de la Search Console.

Une par une, tape l'adresse puis clique **Demander une indexation** :

```
https://apexa.tn/
https://apexa.tn/odontogramme.html
https://apexa.tn/logiciel-dentaire-hors-ligne.html
https://apexa.tn/guide-choisir-logiciel-cabinet-dentaire.html
https://apexa.tn/confidentialite.html
```

⚠️ Le quota est d'une dizaine de demandes par jour. Inutile de réclamer deux fois la même URL :
ça ne va pas plus vite.

---

## 4 · Vérifier deux semaines plus tard que le vrai défaut est mort

Le site n'était pas invisible par manque de contenu. Il disait à Google, sur chaque page, que la
vraie page était sur `oumayma-404.github.io` — une adresse qui redirige vers apexa.tn. C'est
corrigé, et voici comment tu vérifies que Google l'a compris :

| Où regarder | Ce qui doit s'afficher |
|---|---|
| *Inspection de l'URL* sur `https://apexa.tn/`, ligne « Canonique sélectionné par Google » | `https://apexa.tn/` |
| *Indexation → Pages* | **pas** de motif « Autre page avec balise canonique correcte » pour l'accueil |
| *Indexation → Pages* | les 5 pages en « Indexée », pas en « Détectée, actuellement non indexée » |

Si « Autre page avec balise canonique correcte » revient sur l'accueil, dis-le-moi : ça voudrait
dire qu'un canonique est resté faux quelque part.

---

## 5 · Deux choses de plus, dix minutes chacune

### Bing Webmaster Tools

<https://www.bing.com/webmasters> → **Importer depuis Google Search Console** → un clic, tout est
repris. Ça vaut le coup ici : tes acheteurs sont devant un PC Windows où Edge cherche sur Bing par
défaut.

### Le test des résultats enrichis

<https://search.google.com/test/rich-results> → colle `https://apexa.tn/`. Tu dois voir
`SoftwareApplication`, `Organization`, `WebSite` et `FAQPage`, sans erreur.

⚠️ **Ne t'attends pas aux accordéons FAQ dans les résultats.** Depuis 2023 Google ne les *affiche*
plus que pour les sites d'État et de santé reconnus. Le balisage reste utile — il dit à Google de
quoi la page parle — mais il ne donnera pas de résultat enrichi visible.

---

## 6 · Fiche d'établissement Google — une décision à prendre, pas une tâche

C'est **le levier numéro un** sur « … en Tunisie » et le seul moyen d'apparaître dans le bloc
carte, avant tous les résultats classiques.

Il demande une **adresse et un numéro de téléphone publics** en Tunisie, vérifiables par courrier
ou par appel. Le site ne donne aujourd'hui qu'une adresse e-mail.

À toi de décider si tu veux publier une adresse. Je ne prends pas cette décision.

---

## 7 · Ce qui compte plus que tout ce qui est au-dessus

**Un lien entrant depuis un vrai site tunisien.** Le domaine n'en a aucun aujourd'hui, et c'est ce
qui décide entre la page 1 et la page 3 sur une requête que trois concurrents visent déjà.

Les pistes qui coûtent une demande, pas de l'argent :

- l'ordre ou une association de dentistes (annuaire des fournisseurs) ;
- un cabinet client, un lien « notre logiciel » en pied de son site ;
- un salon, une journée de formation, un article de presse locale ;
- un annuaire d'entreprises tunisiennes sérieux — pas les fermes à liens.

---

## 8 · Le calendrier, dit honnêtement

| Quand | Quoi |
|---|---|
| 2 à 7 jours après l'étape 3 | les pages commencent à être indexées |
| 2 à 3 semaines | on voit les premières impressions dans *Performances* |
| 2 à 4 mois | un classement stable sur la requête principale, **si** il y a des liens entrants |

Rien de ce document ne donne un résultat le lendemain. Ce qui a été corrigé côté code était la
condition pour que le reste serve à quelque chose, pas le résultat lui-même.

---

## Ce que le code fait déjà, et qu'il ne faut pas refaire à la main

| | Où c'est |
|---|---|
| `canonical`, `og:url`, `og:image` sur le bon domaine | `build.mjs`, constante `BASE` |
| L'accueil canonique à `/` et non `/index.html` | `build.mjs`, `canonOf` |
| `robots.txt` et `sitemap.xml` | `build.mjs`, étape 5 — le sitemap se remplit tout seul quand on ajoute une page |
| Les données structurées | `src/jsonld/` — **lis le README avant d'y toucher**, quatre règles y font rejeter le balisage entier |
| Les trois scènes d'animation hors de l'index | `src/scenes/*.html`, `noindex` + `Disallow` |
| Titre et description d'une page | le front-matter JSON en tête de `src/pages/<page>.html` |
