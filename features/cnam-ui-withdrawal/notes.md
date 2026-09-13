# La CNAM n'a plus d'interface — et tout le serveur est intact

**2026-09-13.** Retrait de **toute** la surface CNAM du navigateur, `api/` inchangé. Un seul commit, pour que
le retour en arrière soit `git revert`.

Ce n'était pas un nettoyage cosmétique : la CNAM était le bulletin de soins **BS1**, l'arrêt de travail
**P 061**, une section « Identité CNAM » de dix champs sur la fiche patient, un plafond annuel, une estimation
de remboursement par acte, et la page **Actes dentaires** — dont le seul but était de nourrir tout cela.
320 occurrences réelles sur 38 fichiers.

## Ce qui a disparu

| Surface | Fichiers |
|---|---|
| Page « Actes dentaires » | `web/app/dental-acts/`, `dental-acts-table.tsx`, `dental-act-form-modal.tsx`, `cnam-letter-values-card.tsx`, `lib/api/dental-acts.ts`, l'entrée `nav.ts`, la paire `zones.ts` |
| Modules CNAM purs | `lib/cnam.ts`, `lib/arret-travail.ts`, `components/cnam/cnam-ceiling-notice.tsx`, `billingApi.getPatientCnamCeiling` |
| Les deux formulaires officiels | les branches `bulletin-cnam` / `arret-travail` de `document-editor-content.tsx` (~1 200 lignes) : `isOfficialForm`, l'aperçu BS1 en direct, le sélecteur d'actes DCH, l'estimation par ligne, les deux garde-fous K2/L11, `buildBulletinContent` / `buildArretContent` |
| Fiche patient | la section « Identité CNAM » de `edit-patient-dialog.tsx` |
| Réglages | le champ « Code prof. santé (CNAM) » du répertoire Médecins |
| DTO | `DentalActDto`, `CnamLetterValueDto`, `CnamInfo`, `CnamCeilingDto`, `PatientDto.cnamInfo` |

## Ce qui est resté, et pourquoi — **lire avant de « finir le nettoyage »**

Chacun de ces points a l'air d'un oubli. Aucun n'en est un.

1. **Les deux entrées `arret-travail` et `bulletin-cnam` de `web/lib/documents.ts`.** `check:responsive`'s N33
   `document-type-set-has-one-owner` compare le jeu de types **dans les deux sens** avec
   `api/…/Documents/DocumentTypes.cs`, qui les déclare toujours. Les retirer fait échouer le gate — et fait
   afficher un document déjà enregistré sous sa clé brute (« arret-travail ») dans l'onglet Documents du
   patient, ce qui a déjà été livré une fois.

2. **`RealtimeResource.DentalActs` dans `web/lib/realtime/clinic-hub.ts`.**
   `RealtimeResourceResolverTests` compare ce jeu au dossier `api/…/Features/DentalActs` **dans les deux
   sens** ; le retirer casse la suite backend.

3. **`dentalActCodeId` / `codeActe` dans `invoice-form-modal.tsx`.** Ce modal est le **seul écrivain** de ce
   champ dans tout le produit. Rien ne l'affiche ni ne l'attache plus, mais il doit continuer à faire
   l'aller-retour : sans cela, **rouvrir** une note ancienne suffirait à en effacer les codes.

4. **`cnamInfo` est ABSENT des charges utiles de la fiche patient, jamais envoyé vide.**
   `UpdatePatientCommand.cs:323` — `if (request.CnamInfo != null)`. Absent = inchangé. Un objet construit
   depuis un state vidé effacerait l'identifiant, le régime et le lien de **chaque patient** au premier
   enregistrement ordinaire. Même raison pour `codeProfessionnelSante`, dont l'état et la charge utile
   restent alors que l'`<Input>` est parti.

5. **`CnamClosedSetContractTests` est *skippé par attribut*, pas supprimé** — le seul fichier `api/` touché,
   et c'est un test, pas du code produit. Il épinglait `web/lib/cnam.ts` contre `CnamInfo`, caractère par
   caractère (« Convention bilatérale » et son accent : une divergence faisait tomber le `switch` du
   renderer et imprimait une case **vide**, sans erreur nulle part). Son propre commentaire interdit de se
   taire quand un côté manque — d'où un `Skip` **déclaré**, visible dans la sortie, plutôt qu'un retour
   anticipé silencieux. Le `FileNotFoundException` bruyant est laissé en place : qui enlève les `Skip` sans
   restaurer le module retrouve l'échec d'origine.

6. **Un refus de route pour les deux types**, dans `app/documents/[type]/page.tsx` (`isWithdrawnDocumentType`).
   Cette route n'a jamais validé son segment : sans le garde, « Modifier » sur un bulletin enregistré
   monterait l'éditeur **générique par-dessus** — un document libre et vide sur un formulaire légal, à un
   « Enregistrer » de le remplacer. Le garde est sur la **route** et non dans l'éditeur, parce qu'un retour
   anticipé sur un paramètre de route réordonnerait les hooks du composant.
   **Lire un document enregistré n'a pas bougé** : `DocumentPreviewDialog` encadre le PDF rendu par le
   serveur, et les deux renderers d'overlay sont toujours là — Imprimer et Télécharger produisent toujours le
   vrai formulaire. Seule l'édition est retirée.

## La conséquence qu'un `revert` ne défera pas

Le serveur sert toujours `GET /patients/{id}/billing-summary` et `GET /patients/{id}/cnam-ceiling`. Comme
plus rien ne remplit `InvoiceLine.DentalActCodeId` — le seul écrivain était le formulaire ci-dessus, et il
n'attachait déjà plus rien depuis que le sélecteur lit `/procedure-types` — ces lectures **rapportent
structurellement zéro**, et le font comme un vrai chiffre, pas comme « indisponible ». Personne ne le voit
aujourd'hui, puisque rien ne les lit. Mais **la consommation du plafond repart de zéro pour toute la période
d'extinction** : à la réactivation, un patient soigné entre-temps aura un plafond qui paraît intact.

## Rallumer

`git revert` du commit, puis :
- supprimer les `Skip` de `CnamClosedSetContractTests` (le module qu'il épingle revient avec le revert) ;
- ré-vérifier N33 et `card-fallback` dans `check:responsive` ;
- les dix `follow-up/cnam-*.md` repassent de `parked` à leur état d'origine.

Rien à faire côté `api/` : il n'a pas bougé. Les dossiers `features/cnam-*` (spec, stories) décrivent toujours
ce qui a été construit et restent la référence.

## Vérifié

`tsc --noEmit` · `check:responsive` **64/64** · `next build` (la route `/dental-acts` a bien disparu de la
table) · `dotnet test -c Release` **4599 passés, 6 skippés, 0 échec**.
⚠️ `e2e/scripts/check-coverage.mjs` sort en 1 (64 lignes tier-0 sans test) — **antérieur à ce commit** :
`e2e/` ne contient aucune occurrence CNAM et ni `e2e/specs/` ni `scenarios.md` § tier-0 n'ont été touchés.
