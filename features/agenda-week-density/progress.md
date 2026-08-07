# Agenda — vue Semaine, passe 1 (« Une seule barre »)

Option **B livrée en deux temps**, choisie sur la maquette (`agenda-audit`, artifact). Ceci est la **passe 1**.
La passe 2 — la ligne de charge par jour + la légende des **actes** qui sert de filtre — n'est pas faite.

## Ce qui a été fait

| Constat | Fix | Fichier |
|---|---|---|
| F1 · quatre rangées d'outils (≈ 215 px), la date deux rangées après le sélecteur de vue | Une seule rangée, `flex-nowrap`, titre en `min-w-0 flex-1 truncate`. Le calendrier possède toute la barre ; la page lui passe `onNewAppointment` / `doctorFilter` / `googleControls` | `appointment-calendar.tsx`, `app/appointments/page.tsx` |
| F2 · 24 lignes d'heures pour 11 h d'ouverture | `gridWindow` = horaires de la clinique **∪** l'amplitude des RDV réellement pris, + « Afficher les 24 heures ». `fromHour * 60` soustrait dans les deux seuls endroits qui font l'arithmétique | `appointment-calendar.tsx` |
| F3 · la légende décrit des statuts pleins que la grille ne peint pas | Déplacée **dans** « Filtres », à côté des interrupteurs qu'elle explique. ⚠️ **Toujours à moitié fausse** : la couleur de l'acte n'y est pas — c'est la passe 2 | idem |
| F4 · le badge `60m` mange la largeur du nom | Badge supprimé (la hauteur dit la durée), remplacé par `HH:mm · acte` | idem |
| F5 · rien ne marque aujourd'hui | Colonne du jour teintée (en-tête + corps). ⚠️ **La partie « la ligne s'arrête aux bords de la colonne » a été annulée** — voir la régression ci-dessous | idem |
| F6 · aucun résumé par jour | Compteur « N RDV » sous chaque date (« fermé » si le cabinet est fermé, jamais « 0 ») + total de la fenêtre dans le titre. *Partiel : pas de temps occupé ni de barre de charge — passe 2* | idem |
| F7 · référence, contrôles et action mélangés sur une ligne | Référence → « Filtres » ; administration ponctuelle → « ⋯ » | idem |
| F8 · « Déconnecter Google » en rouge, en permanence au-dessus du planning | Dans « ⋯ », admins seulement (`googleControls` vaut `undefined` sinon) | idem |
| F9 · zéro glisser-déposer | **Non traité, volontairement** — comportement, pas visuel. Chantier séparé | — |

## Régression introduite par la passe 1, puis corrigée : la ligne « maintenant » disparaissait

**Symptôme signalé :** la ligne de l'heure courante avait disparu de la grille.

**Cause, vérifiée en base :** ce cabinet ouvre **09:00–17:00** (`Clinics.WorkingHoursJson`). La ligne lit sa
position sur le **DOM** — `querySelector('[data-time-slot="18:00"]')` — ce qui est précisément ce qui lui permet de
se re-baser gratuitement quand la fenêtre bouge. Le rognage a rendu cette ligne d'heure *conditionnelle* : à 18:39
la rangée `18:00` n'existait plus, et le `else setCurrentTimePosition(null)` — qu'une grille rognée exige vraiment,
sinon elle affiche une ligne rouge à une heure fausse — la supprimait. Donc : **tous les soirs après 17:00, et tous
les matins avant 09:00, plus de ligne.**

**Correctif :** l'heure courante entre dans l'union de `gridWindow`, exactement comme un RDV pris — `nowHour` et
`nowHour + 1` (une rangée vaut une heure et la ligne est *dedans*), et seulement si aujourd'hui est à l'écran.
Vérifié pour 07/08/09/13/17/18/19/23 h : la rangée existe dans les huit cas, et une semaine qui ne contient pas
aujourd'hui n'ajoute aucune rangée fantôme.

⚠️ **Piège qui vient avec :** la fenêtre est désormais reconstruite à chaque changement d'heure, donc les deux
effets qui en dépendent prennent `gridWindow.fromHour` / `.toHour` et **jamais l'objet** — sinon un `scrollTo` part
à chaque heure pile et arrache l'agenda de ce que l'utilisateur lisait.

⚠️ **Annulé à la demande :** la ligne repasse sur **toute la largeur** de la grille (gouttière → bord droit), comme
avant. La version limitée à la colonne du jour (F5, présente dans la maquette) reste défendable — 15:47 est un fait
sur aujourd'hui, pas sur dimanche, et c'est ce que fait Google Agenda — mais ce n'est pas ce que les utilisateurs
avaient. L'expression exacte pour la rétablir est conservée en commentaire au point de rendu.

## Progressive disclosure de la barre (mobile/tablette d'abord)

| Largeur | Ce qui est rendu |
|---|---|
| < 768 | `AgendaPhoneHeader` (inchangé) + le FAB « Nouveau RDV ». La barre desktop est `hidden md:flex` |
| 768–1023 (iPad portrait, ~532 px utiles avec le rail) | ‹ › · icône Aujourd'hui · titre tronqué · **J/S/M en initiales** · Filtres (icône) · ⋯ · Nouveau |
| ≥ 1024 | libellés complets (Aujourd'hui, Jour/Semaine/Mois, Filtres) + « · N RDV » dans le titre |
| ≥ 1280 | le Select praticien sort de « Filtres » et devient visible dans la barre |

Le Select praticien est rendu par **une** fonction (`renderDoctorSelect`) à deux endroits, donc les deux instances
ne peuvent pas diverger sur la liste des médecins ; ni l'une ni l'autre ne détient d'état.

## Gate

```
npm run check:responsive   ✓ 11/11 (dont agenda-scroll : HOUR_HEIGHT=48, colonnes 120px, fluide en lg:, wrapper intact)
npx tsc --noEmit           ✓ 0 erreur
npm run build              ✓ compile
```

Stack relancée et vérifiée : API `http://localhost:5000/swagger` → 200, front `http://localhost:3000` → up,
`/appointments` → 307 vers `/login` (attendu sans session), aucune erreur de compilation dans le log du dev server.

⚠️ **La passe à l'œil (320 / 390 / 820 / 1180 / 1440 + téléphone en paysage + clavier) est DUE et n'a pas été
faite** : elle demande une session authentifiée dans un navigateur, ce que l'environnement de cette session n'a
pas. C'est la moitié porteuse du gate (`.claude/rules/frontend-web.md` § 14) — à faire avant de considérer la
passe 1 terminée. Points à regarder en priorité :

1. **820 px avec le rail déplié** — la barre doit rester sur une ligne, le titre se tronquer, rien ne doit passer
   à la ligne. C'est le risque n° 1 de cette passe.
2. **Un RDV de 45 min** (36 px) — le nom + la ligne `HH:mm · acte` doivent tenir sans être coupés à mi-hauteur.
3. **Un RDV de 60 min** — vérifier que la disparition du badge « non synchronisé » sous 62 px de haut
   (`hasRoomForSync`) est acceptable, ou remonter le seuil.
4. **Le jour du cabinet fermé** (dimanche) — en-tête « fermé », colonne grisée, et la ligne « maintenant » qui ne
   la traverse plus.
5. **« Afficher les 24 heures »** — le retour en arrière doit ramener l'œil sur les mêmes heures (l'effet de
   scroll dépend de `gridWindow` pour ça).
