# multi-seance-treatment-steps — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
`README.md` is what was asked for; this is what shipped, after the audit pass of 2026-09-07.

---

## An échéance nobody agreed to is not late (`InstallmentLateness`, `Installment.IsAutoRaised`)

**Every devis in every database read « En retard » from the day after it was signed.** Measured on the dev
database: **25 of 27** unpaid échéances flagged, across every status — including devis a note d'honoraires
already billed (whose échéancier can no longer take money at all, as the same card says in as many words) and
devis that had been **cancelled**. A badge that fires on almost every row is a badge a practice stops reading,
which is exactly what had happened.

The cause was never the badge. `TreatmentPlan.Accept` raises **one lump-sum échéance for the whole total, dated
at the acceptance instant**, when the dentist supplied no schedule — and that row is a **ledger container, not a
promise**: a payment needs an échéance to attach to and `Outstanding` is derived from the schedule. Nobody agreed
its date. The create form already said so (« le total est dû à la signature… qui apparaîtra en retard dès
demain »), but the two paths that mint a devis *without* that form — « Éditer le devis » and collecting on a
treatment — had no warning and no way out.

⚠️ **The distinction had to be STORED, not guessed.** A rule like « one row, dated at acceptance » also matches a
schedule a dentist deliberately typed for the signature day, and silencing that one destroys the only thing an
échéancier is for. Hence `Installment.IsAutoRaised`, false by default so every row a human enters is an agreed
date without any caller saying so, and set by the **two** system writers only (`Accept`, `RespreadSchedule`).
⚠️ `Revise` **clears** it — revising is a dentist looking at the dates and settling on them — which is why
`RespreadSchedule` re-marks the rows it trims: trimming a collected row to what it took agrees to nothing.

`InstallmentLateness.IsLate` is the one rule, and it needs five facts a row does not have: the plan's status
(through `PlanBillingRules.CarriesDebt`, so Draft and Cancelled fall out for free), whether a note represents the
plan, whether any act is still unrealised, the clinic's own calendar day, and whether the date was ever agreed.
**Served as `InstallmentDto.IsOverdue`** — the client cannot answer it, and the two surfaces that used to
(`plan-workspace`'s table and its card list) each wrote `isBeforeToday(dueDate)`.

⚠️ **The « unrealised work » term applies to an auto-raised row ONLY.** The dentist's own rule and the right one:
an act billed once and delivered over six visits is paid as the visits happen, so a balance on a treatment still
under way is not yet due. But a schedule somebody typed says when the money is due *whatever* the clinical state
— `InstallmentLatenessTests.An_Agreed_Date_Is_Late_Even_With_Work_Outstanding` is what stops the fix widening
into « nothing is ever late ».

**The migration's backfill is the half that matters** — without it the column only starts meaning something for
devis accepted after the deploy. Its discriminator is exact rather than heuristic: `Accept` writes
`AcceptedDate.Value` verbatim into the row, so the two timestamps are byte-identical, while every hand-entered
échéance comes from a `YYYY-MM-DD` field and lands at midnight. Three clean groups on the dev database, no
overlap. Deliberately conservative — a row it misses stays « agreed » and can still go red, which is at worst
yesterday's behaviour; claiming a typed date was auto-raised is the error nobody can see.

---

## An act's end state is charted when the act is FINISHED (`ToothChartingRules`)

**The odontogram asserted finished work from the first séance of every multi-séance act.** `ToothCondition`'s own
documentation draws the line — a restoration « records work already done » — and
`DentalRecordActParser.BuildToothStates` wrote one on every fiche save with no notion of whether the act was
over. Measured on the live database, seven rows, **every one written from a step 1 of 2**:

| Patient | Teeth | Charted | From |
|---|---|---|---|
| Nadia Jelassi | 16, 36, 46 | **Implant** | step 1 / 2 |
| Karim Hamdi | 13, 43 | **Extrait/absent** | step 1 / 2 |
| Leila Gharbi | 26, 27 | **Obturation** | step 1 / 2 |

Three teeth claiming implants that did not exist; two recorded as *gone* while the extraction was half done. No
error anywhere — the chart simply described a mouth the patient did not have, for the length of the treatment.

⚠️ **Its twin is `DentalRecordLinker.ClearDiagnosesForTreatedTeethAsync`, and the two had to move together.** That
helper deletes the open « à traiter » diagnosis on every tooth the new states name, so séance 1 both claimed the
work was finished *and* forgot it had ever been needed. It is driven off the states this rule produces, so
withholding them withholds the deletion in the same breath — which is why `ChartableActs` returns a filtered
**act list** rather than filtering the states afterwards.

**Both commands now link the plan step BEFORE charting**, because only the aggregate — after the step is marked —
can say whether the act is finished (`DentalRecordLinker.PlanActLink.ItemIsComplete`). Never derive it from the
step's rank: one séance can close two steps, a protocol can be re-cut mid-treatment, and an act booked whole has
no steps and finishes on its first fiche.

⚠️ **`PlanCarriedAct` was extracted for this**, not for tidiness. « Which act of this fiche does the devis carry »
is asked by the fee rule (`PlanCarriedActPricing`, which imposes 0) and now by this one; a re-typed copy prices
the implant and charts the filling. A mixed séance — an implant's step 1 plus a filling done the same day — is
the case that makes it visible, and `ToothChartingRulesTests` pins it.

---

## A séance remembers the teeth the last one treated (`TreatedToothNumbers`)

The fiche's chart selection was prefilled from the **devis line**'s teeth, and a devis line is very often an
« acte général » with none — so séance 2 of an implant opened on a blank chart. Measured on a real implant: its
three fiches recorded the teeth **once between them**, on whichever séance the dentist happened to fill in.

⚠️ It stopped being a convenience the moment the odontogram fix landed: the chart is written when the act
**finishes**, so teeth entered early and absent from the last fiche would chart **nothing at all**. The two
changes are one change and must not be separated.

Derived, never stored: `IDentalRecordRepository.GetTreatedTeethAsync` over the fiches linked to the act's steps
**and** to the act itself — the step link is the important half, since a stepped act takes its own
`LinkedDentalRecordId` only when its last step lands.

---

## A séance says what it WAS, not just which act it belonged to

Three fiches of one implant read « Implant dentaire · Implant dentaire · Implant dentaire » in the patient's
history — which says the patient had three implants — with nothing saying the three were one treatment. The step
label and its rank were on record and never read back. `DentalRecordPlanLinkRow` carries them now, and the row
reads « Implant dentaire / Pose de l'implant · étape 3 / 6 ».

⚠️ **The money cells key on the LINK, not on money having moved.** Branching on `collectedOnTreatment > 0` left a
séance that collected nothing reading « 0,000 DT · 0,000 DT » — indistinguishable from an ordinary free visit
while the treatment showed 1 500 DT outstanding — and on a six-visit implant most séances collect nothing. Zero
renders as « — » with the treatment badge kept, for the reason « Reste » already did.

---

## The header is one action and a menu

Seven controls of equal weight, measured identical at 320 / 390 / 820 / 1180 / 1440: « Facturer le devis ·
Modifier le devis · Arrêter le traitement · Terminer · Devis PDF · Envoyer par e-mail · Annuler ». Two of them,
side by side on a followed treatment, were **« Éditer le devis » and « Modifier le devis »** — near-identical
French for minting a gapless numbered financial document and for correcting an act's price. The file already
argued against a « second door » for `Accepter`/`Éditer` and then grew one when `canAmend` widened to Draft.

`primaryAction` derives the one act from the state (mint → bill → resume); everything else is in a « ⋯ » menu,
the pattern the plans list and the échéancier already use. The amend action is **« Modifier les actes et les
prix »** — named for what it edits.

⚠️ `primaryAction` is a `useMemo` that RUNS DURING RENDER and calls the `const` confirm openers, so it must be
declared **below** them. Placed above, every `Completed` plan threw « Cannot access 'confirmReopen' before
initialization » and the workspace failed to render entirely — invisible to `tsc`, to `check:responsive` and to
the build; only the browser walk saw it.

---

## Two dead controls on a followed treatment

- **« Annuler » was refused every time.** `isActive` is `isPlanLive`, which includes `Draft`; `TreatmentPlan.Cancel`
  throws « Un brouillon se supprime, il ne s'annule pas. » on exactly that status. Browser-proven end to end: the
  motif filled in, the confirmation pressed, the server refusing — and the remedy the sentence names lived only on
  `/treatment-plans`. A devis is cancelled because its **number** must be accounted for; a treatment that never had
  one is deleted or stopped.
- **« Supprimer le brouillon » destroyed recorded séances.** `CanBeDeleted` asked only the status, and the cascade
  takes the acts and their step rows (`DeleteBehavior.Cascade`) while the appointments' links are `SetNull` — so
  the fiches survive attached to nothing, the exact wreckage `StopTreatment` was written to avoid. The dialog
  reassured: « Aucun numéro n'a été consommé ; cette action est irréversible. » `RemoveItem` had refused
  `HasDeliveredWork` **per act** all along; the whole-plan question was simply never asked.

---

## The dashboard counted treatments as unanswered quotes

`DashboardAlertsReader` counted `Status == Draft` for « Devis en attente de réponse ». Since « Suivre ce
traitement » an un-numbered plan is usually a treatment being carried out right now, so the figure put this
week's séances under a heading inviting the practice to chase patients with nothing to answer — a Draft has never
been handed a devis, `Accept` being the only writer of `Number`. `CountUnansweredDraftsAsync` narrows it to
Drafts with no recorded work. ⚠️ The identical premise was already corrected in `RecallWorklistRules.IsUnanswered`
and this copy was missed — the `fixes-dont-propagate` shape, one layer over.

---

## A multi-séance act is split by DEFAULT, and the treatment is created on save (`resolvePlannedProtocols`)

**Two dentists, the same act, and only one of them had the button.** Reported from the field: a client booked
« Couronne / bridge », read « Cet acte se fait normalement en 3 séances » and had **no control at all**
underneath it, while the same act on the same screen here showed « Suivre ce traitement ».

The cause is one line — `create-appointment-dialog.tsx`'s `onStartProtocol={selectedPatientId ? startProtocol
: undefined}` — against a picker that rendered the *sentence* unconditionally and the *button* inside
`{onStartProtocol && …}`. So the offer was absent in three real situations, all of them ordinary:

| Situation | Why the button was gone |
|---|---|
| The act picked before the patient | `selectedPatientId` is `""` until one is chosen |
| « Nouveau patient » (a walk-in) | that mode has **no id at all** until the save |
| The **edit** dialog | it never passed the prop — for everyone, always |

⚠️ **Version skew is excluded, and the argument is worth keeping**: the sentence and the button landed in the
same commit (`918537da`) inside the same JSX block, so anyone who can read the sentence is running the code
that draws the button.

**The fix inverts the default rather than widening the gate.** An act with a catalogue protocol is now split
**by default** — `resolvePlannedProtocols` is the one place that decides it — and « Tout faire en une séance »
is the way out. `SelectedAct.plannedProtocol` is tri-state (`undefined` = nobody decided · `null` = one séance
· a list = the confirmed séances), and `[]` is never stored, because emptying the list *is* choosing one
séance.

⚠️ **Derived on render, never seeded into state by an effect.** The only channel a picker has back to its host
is `onChange`, and the edit dialog's `onChange` also resets `durationTouched` — so an effect seeding through it
would silently re-length every appointment merely by opening it. The rule costs one `map`.

⚠️ **One treatment per booking.** An appointment carries a single `TreatmentPlanId` and `resolveAttachedPlanId`
refuses two, so only the first undecided protocol act is followed; the others resolve to `null` and their card
says which act holds the slot. `setPlannedProtocol` writes back over **`value`**, not over the resolved list —
freezing the resolution would mean that saying « une seule séance » on a crown also froze the implant beside it
at the same answer, which is the slot it was only waiting for.

### The treatment is created when the booking is saved

`materialiseTreatments` (in `use-patient-plan-acts.ts`, beside `resolveAttachedPlanId`) turns every
followed act into an un-numbered `Draft` at save time and rewrites its row through `planItemToPreset` +
`presetToSelectedAct`, so a row attached here is identical to one attached from « Actes du devis ».

That deferral **is** the fix, not an implementation detail. Creating on press needed a patient id, which is
exactly what the three broken cases lacked; it also left an orphan treatment behind whenever the dentist
abandoned the booking afterwards, and made the decision un-takeable-back without deleting a server aggregate.

⚠️ **`createdPlansRef` is load-bearing, and it is `createdPatientIdRef`'s reason one object over.** Both
dialogs re-run their save **from the top** on every confirmation the server asks for — slot taken, out of
hours, past time — so without a per-catalogue-act memo one « créer quand même » leaves the patient with **two
identical treatments**. Verified by booking deliberately onto an occupied slot: one confirmation accepted, one
treatment.

### The séances are editable in the booking dialog, for this patient only

`AppointmentProtocolEditor` — rename, reorder, add, remove, chair time, and the minimum wait — inline in the
act's card. `StartTreatmentCommand.Steps` carries the confirmed list and **never writes back to
`ProcedureType.DefaultSteps`**; a test pins that, because reaching for the entity already in hand is the
obvious way to break it.

⚠️ **The exit is a disclosure, never « Terminer ».** Reported from use within minutes of shipping: with the
list open the row read « Terminer · Tout faire en une séance », so the *exit* looked like a validation of the
séances just typed and the control beside it looked like the confirm — one press from collapsing the whole
treatment into a single visit. Nothing there needs validating (the list is form state; « Créer le rendez-vous »
is the only save on the screen), so it says « Masquer les séances », and the one-séance action moves **inside**
the editor while it is open, beside « Rétablir le protocole », where it reads as one of the list's own
operations.

⚠️ **The price field renames itself to « Prix du traitement (N séances) » on a followed act.** An act is priced
once; « Prix pour ce rendez-vous » on a 2 000 DT implant invites the dentist to type this visit's share, and
the treatment would then be created at that share for all six visits.

`check:responsive`'s **N26 `booking-materialises-its-treatments`** derives the guard from the picker itself: a
surface that renders it and never calls the materialiser books an ordinary one-off while its own card says
« Traitement en 3 séances » — no error, no toast, and the treatment simply never exists.

---

## « Modifier les étapes » was erasing the minimum interval on every save

Found while wiring the editor above, and **older than it**: `SetTreatmentPlanItemStepsCommand`'s request had
no `MinDaysAfterPrevious` at all, so its mapping built `new TreatmentPlanItemStepInput(s.Id, s.Label,
s.EstimatedDurationMinutes)` — three arguments against a record whose fourth **defaults to null** — and
`SetSteps` has replace semantics. Renaming one step of an implant therefore wiped the osseointegration wait off
**all** of them, including steps the caller had only echoed back. The client had been sending the field all
along; the old dialog did not, and now does.

⚠️ The symptom is not an error anywhere. It is `RecallWorklistRules` reporting a correctly-progressing implant
as abandoned after a flat fortnight — the exact failure `MinDaysAfterPrevious` was added to prevent.

`StartTreatmentStepsTests.A_step_input_copied_from_a_source_carries_its_interval` is the derived guard, and it
flags a **copy** rather than every three-argument call: `ContinueRecordedActCommand` synthesises two séances
out of prose and has no source interval to carry, so demanding a fourth argument there would be noise — and a
guard that reports things nobody should act on stops being read. A copy is two or more arguments reading
members off one receiver, which is what every mapping from a request, a template or an existing step looks
like. Red-proved against the real defect.

---

## `/factures` names the money it has no row for

**Reported twice, the second time as « il y a deux sources de vérité, on croit à une fuite d'argent ».** A
dentist recorded the first séance of a multi-séance act, took 120 DT at the chair, went to « Factures » — and
found no facture. La caisse showed it, filed as an échéance de devis.

The behaviour is correct and deliberate: an act done over several séances is **priced once on the treatment**,
so collecting at the chair records an échéance payment and no note d'honoraires (`CollectOnTreatmentCommand`).
What was wrong is what the screen said about it. « Total encaissé » deliberately counts **both** money tracks —
that is what keeps it equal to la caisse and to the dashboard — while the table beneath it lists the invoice
ledger alone. So the headline moved and no row appeared, which is indistinguishable from money going missing.

⚠️ **It had already been answered once, in prose, and prose was not enough.** The hint read « paiements de
notes et échéances de devis »: it says the gap exists and never *how big it is*, so a practice adding up the
« Encaissé » column still comes out short and still cannot tell where the rest went. Measured on the dev
database: **2 050,000 DT across seven payments**, not one of them visible anywhere on that page.

- **`InvoiceRevenueDto.CollectedOnTreatmentPlans`** carries the share. Served, never derived in the browser:
  only the server can apply `PlanBillingRules.BilledPlanIds`, which drops a devis already bridged into a note —
  without it a plan collected *through* an invoice would be counted on both tracks and announced twice.
  ⚠️ It is a **component of** `TotalCollected`, and `Revenue_Reports_How_Much_Was_Collected_On_Devis…` asserts
  the subtraction lands exactly on the invoice ledger's own figure, because a sentence built on it is
  arithmetic the reader is invited to check.
- The hint on the figure becomes **« dont 2 050,000 DT sur des devis »**, and a `role="status"` line under the
  strip explains why there is no facture and **links to the rows** — `/caisse`'s extrait and `/treatment-plans`.
  A reader whose question is « où est mon argent ? » must not have to know that la caisse is where it lives.
- Both are rendered **only when the figure is non-zero**, so a practice that raises a note for everything meets
  nothing new. `A_Practice_With_No_Devis_Collection_Reports_Zero` pins that.

⚠️ **Neither branch of the read could be trusted to report it by accident**: the windowed one and the
date-free one compute the plan share separately (there is no date-free plan aggregate), and the date-free one
is what `/factures` loads on arrival — so it is the branch where the new figure could have stayed silently at
zero while the total was right. It has its own test.

---

## La refonte : un objet, un vocabulaire, et rien d'inerte à l'écran

Signalé de trois côtés à la fois — « trop chargé, la même information répétée », « pourquoi l'acte est écrit
deux fois », « pourquoi ce 0 que je ne peux pas modifier », « les champs d'argent en bas ont l'air bizarres »,
et « tant que je n'enregistre pas la première fiche, ça reste brouillon … we need visibility ».

Un relevé sur les six écrans que la fonctionnalité touche a trouvé **14 problèmes, dont 5 vérifiés en base ou
à l'écran**. Le diagnostic n'était pas le nombre de clics — réserver un acte en trois séances en coûte
**zéro**, la répartition étant le défaut — mais la **lecture** : le même objet portait six noms, et trois
écrans montraient des chiffres sur lesquels personne ne pouvait agir.

### « Total » écrivait à travers un champ verrouillé

`distributeSessionTotal` ne filtrait que sur `isActNamed`. Taper 150 dans « Total » réécrivait le prix de
l'acte — **celui que `act-card` rend `readOnly`** — puis `PlanCarriedActPricing` le forçait à 0 à
l'enregistrement. Mesuré sur l'application : « 0,000 » → « **150,000** » à l'écran, « 0,000 » en base après
sauvegarde, sans un mot.

⚠️ **Sur une séance MIXTE c'est pire et plus discret** : une couronne (portée) à côté d'un détartrage (non
porté), et le total tapé se répartissait entre les deux — le détartrage était donc sous-facturé de la part
allée à l'acte qui ne peut rien porter. `check:responsive` **N27 `session-total-skips-carried-acts`** dérive
la garde de la fonction elle-même, et a été prouvée rouge sur une violation délibérée.

### Masqué, jamais déverrouillé — et la condition exacte n'est pas la même des deux côtés

- Le **prix** et la bascule « / dent · forfait » disparaissent **par acte** (`act.billedOnPlan`).
- « **Total** » et « **Payé** » disparaissent **par séance**, et seulement si **toute** la séance est portée.

⚠️ `seanceIsWhollyOnTreatment` est désormais **structurel** — « tous les actes nommés sont portés » — et non
plus `roundMillimes(grandTotal) === 0`. L'ancien test était vrai d'une séance tenant une couronne portée *et*
un détartrage que personne n'avait encore chiffré : « Payé » s'effaçait au moment précis où le dentiste allait
saisir les honoraires du détartrage.

⚠️ « **Mode** » suit l'argent : il part avec `amountCollectedOnPlan` sur le même payload, et il était apparié
à « Total ». Il descend à côté de « Encaissé sur le traitement », qui est le seul champ qui prend quelque chose
sur une telle séance. Une seule définition (`PaymentMethodField`), deux emplacements.

Mesuré : **sept « 0,000 » → un**, et celui qui reste est le champ vide où l'on tape.

### « Couronne / bridge (par élément) · Couronne / bridge (par élément) »

Le libellé était `numéro ?? titre · désignation`. Un traitement suivi n'a pas de numéro et son **titre EST le
nom de l'acte** (`StartTreatmentCommand` : « le dentiste l'a nommé en le choisissant »). Cinq lignes de la base
étaient dans cette forme, et chaque traitement suivi jamais créé le sera. `planItemHeading` / `planDisplayName`
vivent avec `planStatusLabel` — c'est aussi là que « **Devis — brouillon** », écrit en dur dans le bandeau
patient à huit pixels du badge que ce module avait délibérément renommé « Sans devis », a disparu.

### La liste des séances n'est plus un mode

Elle était cachée derrière « Modifier les séances », et l'ouvrir coûtait **515 px à 1440 et 843 px sur un
téléphone de 844**, pour trois séances. C'est aussi la sortie qui avait été signalée : ouverte, la rangée
lisait « Terminer · Tout faire en une séance ».

C'est une **frise lisible** maintenant — une ligne par séance, avec ses chiffres en pastilles — et **une seule
rangée** s'ouvre, en l'éditeur complet d'avant. **303 px au repos, 400 px une rangée ouverte** ; 518 px à
390 px de large au lieu de 843.

⚠️ **Les neuf opérations sont conservées** : renommer · minutes · jours · monter · descendre · supprimer ·
ajouter · rétablir · tout-en-une. La seule chose retirée est « masquer / afficher », qui n'est pas une
capacité. « Tout faire en une seule séance » est un bouton pleine largeur permanent — plus visible qu'avant,
où il apparaissait et disparaissait selon l'état de la liste.

### Un traitement réservé ce matin apparaît ce matin

`GetTreatmentsInProgressAsync` exigeait `item.Status == InProgress`, atteint seulement quand une première
séance a été enregistrée. Deux traitements suivis étaient en base, en `Planned`, **sur aucune liste** — et dans
« Devis et échéanciers » en brouillon sans numéro, rangés parmi les devis. Le filtre accepte désormais
`Planned` **quand l'acte a des séances** ; `Steps.Any()` est ce qui garde l'élargissement honnête, un acte sans
protocole n'ayant jamais eu sa place ici. 13 lignes → **28**.

⚠️ **L'ordre passe de « devis le plus récent » à « ce qui est dû »**, en deux clés : non réservé avant réservé
(`false < true` en PostgreSQL), puis la date dont chaque groupe parle. C'est ce qui rend les trois groupes de
l'écran — **En retard · À planifier · Séance prévue** — *contigus* plutôt qu'entremêlés.

⚠️ La sous-requête « cette séance est-elle réservée ? » doit répondre **exactement** ce que
`TreatmentsInProgressReader` répond, sinon l'écran groupe selon une règle et ordonne selon une autre. Deux
erreurs au premier essai, les deux observées : par **acte** au lieu de par **étape**, et avec un plancher
« à partir d'aujourd'hui » — cinq lignes « prochaine séance le 3 sept. » se sont retrouvées au milieu du groupe
« à planifier ».

⚠️ C'est aussi le seul `AddDays` de cette requête, et l'interface dit que l'addition est délibérément tenue
hors du SQL. Cela reste vrai de la **projection** ; ceci est un `ORDER BY`, qui ne peut pas être composé après
la pagination sans réordonner une page au lieu de la liste.

### L'odontogramme apprend « en cours »

Il avait deux lectures et il en manquait une. « Diagnostics » garde le « à traiter » (correct — ce n'est pas
fini) ; « Actes réalisés » colore la dent **dès la première séance**. Entre les deux, **rien** ne disait qu'une
couronne est en cours sur la 16, séance 2 sur 3. Vérifié sur deux patients : ni « en cours », ni « séance N »,
ni « étape N » nulle part.

Un **anneau azur pointillé** — un `outline`, pas un `ring` : un ring est une `box-shadow` et ne peut pas être
pointillé — plus une info-bulle qui nomme le traitement et où il en est. `teethUnderTreatment` est dérivé des
plans que la page tient déjà pour le bandeau, donc la marque et le bandeau ne peuvent pas se contredire.

⚠️ **La ligne du devis l'emporte quand elle nomme des dents ; les dents traitées ne sont qu'un repli — et
l'union par laquelle cela a commencé était mesurablement fausse.** `treatedToothNumbers` vient des fiches, et
une fiche enregistre les dents de **toute la séance**, pas d'un acte à l'intérieur. Mesuré : une « Extraction
simple » chiffrée sur 13 et 43 dont la 1re séance a été saisie sur une fiche nommant **13, 27, 36, 37, 43** —
l'union posait donc un anneau « traitement en cours » sur trois dents sans aucun traitement.

⚠️ La priorité est l'**inverse** de celle d'`openPlanItems`, délibérément : celle-là sème le schéma d'une
fiche, où les dents réellement travaillées sont la meilleure proposition ; celle-ci répond « sur quelles dents
porte ce traitement », c'est-à-dire ce qui a été chiffré.

### La fiche patient s'ouvre sur l'odontogramme

⚠️ **C'est un revirement**, et les deux raisons méritent d'être gardées. Le bandeau avait été monté au-dessus
du schéma sur demande, avec un vrai argument : « où en est le traitement ? » est un fait qu'il faut avant de
toucher à quoi que ce soit. Ce qui a changé est l'autre moitié de l'échange, demandée en toutes lettres —
« the patient page should have the odontogramme as focus ». L'argument du pli est **répondu** plutôt
qu'abandonné : le schéma porte désormais la présence du traitement, et le bandeau juste dessous en porte le
détail.

⚠️ Le bandeau montrait **un** plan en détail et réduisait les autres à des pastilles de comptage — un patient
avec cinq traitements en voyait quatre représentés par un nombre. Chaque traitement vivant a sa ligne ; les
terminés gardent leur pastille, où un compte est bien toute l'histoire.

### Ce qui a été refusé

Passer un traitement suivi en **`Accepted`**, pour la visibilité. `Accepted` fait partie de
`DebtBearingPlanStatuses` : le montant deviendrait une créance du patient **à la seconde où le rendez-vous est
réservé**, avant que quiconque ait accepté quoi que ce soit — le naufrage que « Arrêter » → « Reprendre le
traitement » a déjà produit une fois. Le statut ne bouge pas ; il cesse de s'appeler « brouillon », et le
traitement apparaît dans les listes dès sa création.

### Vérification

Suite complète **4264 tests**, **50 contrôles responsive** (N27 nouvelle, prouvée rouge), `tsc` et `build` au
vert. Parcours navigateur en un seul passage sur les quatre écrans : les trois groupes contigus (`En retard 1 ·
À planifier 13 · Séance prévue 11`), l'odontogramme en tête (595 px contre 1085), les dents exactement
marquées sur deux patients avec l'info-bulle qui nomme la séance, la frise à 303/400 px avec ses six contrôles
dans la rangée ouverte, et la fiche à un seul « 0,000 ». 320 / 390 / 820 / 1180 / 1440 : aucun défilement
latéral.

⚠️ **Trois des cinq « échecs » du premier passage étaient la sonde** — un `/Séance \d+ sur \d+/` sensible à la
casse contre une ligne en `uppercase`, un seuil `< 400` contre une carte de 400, et un `/0,000/` qui compte
aussi « 500,000 ». Le quatrième (l'anneau sur trois dents de trop) était réel et est corrigé ci-dessus.

---

## Deux surfaces annonçaient l'étape SUIVANTE comme si elle avait eu lieu

Reported from use, in one breath: « i went to add a new fiche dentaire for the first step, nothing in the fiche
modal mentions the step that was done, only the act name … then in odontogramme, when i hover on the teeth
worked, i see couronne/bridge … étape 2/3 essai de l'armature à planifier, i just did étape 1 ». Two defects
with one cause, and the cause is a habit rather than a bug: **a rank derived from a count**.

### L'odontogramme (`teeth-under-treatment.ts`)

`toothTreatmentSummary` printed `séance ${Math.min(stepsDone + 1, stepsTotal)} sur ${stepsTotal}` followed by
`${nextStepLabel} à planifier`. Both halves were about the future and neither said so, on the one diagram a
dentist reads at a glance for what is **in the mouth** — so every word of it is taken as a clinical fact.
Measured on the dev database, the two readings it produced:

| Data | Was printed | Is printed |
|---|---|---|
| Couronne/bridge, `Préparation` done of 3 | séance 2 sur 3 · essai de l'armature à planifier | 1 étape sur 3 faite : Préparation |
| Parodontal 5 séances, only step **2** done | séance 2 sur 5 · Surfaçage 1er quadrant à planifier | 1 étape sur 5 faite : Surfaçage 2e quadrant |
| Bridge 3 séances, steps 1 **and 3** done | séance 3 sur 3 (i.e. « finished ») | 2 étapes sur 3 faites : Scellement |

The last two rows are why the fix is **a count phrased as a count** and not a corrected rank: `DentalRecordLinker`
deliberately allows a séance to be recorded out of protocol order (« the dentist may legitimately book the
scellement before the essayage »), so *any* ordinal is a guess. A count plus the name of the last séance
actually carried out — by `sequenceNumber`, matching what the aggregate does when one fiche closes two steps —
is true under every ordering.

⚠️ **The future was removed rather than reworded.** « Prochaine étape » already has three homes that say so in
as many words (`PlanStepStrip`, `patient-plans-strip`, « Traitements en cours »), and `patient-plans-strip` sits
directly under this chart. Nothing is lost; a planning instruction simply stops being the thing a tooth says.

### La fiche de soins (`patient-record-modal.tsx`)

It said « Séance 1 sur 3 » in small caps and never the step's **name**, so the préparation and the scellement of
one couronne produced identical screens six weeks apart. `TreatmentPlanItemStep.Label` had the answer all along —
`RecordActsSummary` reads it back on the *saved* fiche — so the screen that could not name the séance is the
screen that creates it. It now leads with « Cette séance : étape 1 sur 3 · Préparation », and `PlanItemOption`
carries the **steps** rather than the `stepsTotal`/`stepsDone` pair it printed `stepsDone + 1` from: which steps
a fiche closes is decided server-side from the appointment's own procedure rows (several of them when
« préparation + empreinte » share a visit), falling back to `NextStep`. The memo mirrors
`ResolveStepsOfTheSeanceAsync` exactly, because a second opinion here would name one séance while the save
recorded another.

⚠️ Two gates, each fixing a wrong-séance reading found while verifying the first fix:

- **A reopened fiche answers from ITSELF.** The page passes no appointment when a record is being edited, so
  with nothing booked to read the fallback resolved « the next pending step » — on a fiche that recorded the
  préparation, that is the empreinte. The same defect, one door further in. It reads `treatmentStepLabel`.
- **The line is gated on itself, never on `carriedByDevis`.** `linkedPlanItemId` is never hydrated from a saved
  record, so `billedPlanItem` is null on an edited fiche and every money statement below is absent there.
  « Which séance is this » is not a money statement.

### La règle, et le fait qu'elle avait déjà été trouvée

« Traitements en cours » prints « étape 2 / 2 **à faire** » and `patient-plans-strip` « étape 2 / 6 **à faire** »,
both because *three reviewers each* read the bare fraction as progress and took the treatment for finished. That
fix reached two surfaces and not the third — this repo's dominant defect shape. Two more bare counters were
found while writing the guard and given their word: `plan-step-strip`'s « 2 / 3 **faites** » (whose `sr-only`
« Étapes réalisées : » was already correct, so only the sighted reader was left guessing) and
`treatment-plan-form-modal`'s « 6 / 6 **incluses** ».

`check:responsive`'s **N31 `step-counter-says-done-or-to-do`** now fails on a printed step counter carrying no
word saying which question it answers. Two things about it are load-bearing:

- **It blanks `sr-only` spans before scanning.** With them included, the guard passed a deliberately-stripped
  `plan-step-strip` — satisfied by the offscreen « Prochaine étape : … » two lines under the bare figure.
- **« prochaine » and « en cours » are not accepted words.** Either can sit beside either counter, so neither
  tells them apart.

⚠️ And the 320 px eye pass earned its place: adding « incluses » widened a `shrink-0` cell by ~47 px, which left
the act name ~63 px beside its `[overflow-wrap:anywhere]` and broke « Prothèse amovible (partielle / complète) »
to **one word per line** — a 160 px row where 76 px was needed. `basis-full sm:flex-1` (§ 10.1) fixed it. `tsc`,
`check:responsive` and `build` were all green over that regression.

---

## Une fiche réouverte avait perdu son devis — et offrait une remise que personne n'avait accordée

The step fix above exposed this and the browser found it: a **reopened** fiche of a devis-carried act read as
un-carried. `linkedPlanItemId` had three writers and none was the record — the open effect resets it to
`NO_PLAN_ITEM`, the appointment effect is guarded by `|| record ||`, and the page passes no appointment when a
record is being edited. So `billedPlanItem` was null, `carriedByDevis` false, and:

- « Acte planifié : **Aucun** » on a fiche the server *has* linked;
- no « Suivi comme traitement » / « Déjà facturé » notice;
- no « Encaissé sur le traitement » field — the séance could not take the patient's money at all;
- and `markBilledOnPlan` could never fire, so the card read its stored 0 against the catalogue tarif and
  announced « **Tarif catalogue 700,000 DT — geste de 700,000 DT** » beside a « remettre au tarif » link. A
  discount nobody granted, one press from re-charging the devis — which is verbatim what that back-fill's own
  docstring says it exists to prevent.

`DentalRecordPlanLinkRow` and `DentalRecordDto` now carry `TreatmentPlanItemId` (the id, because an act is
addressed by id and two lines of one devis may share a name), a fourth writer hydrates
`linkedPlanItemId` from it, and `markBilledOnPlan` takes the record as a second, independent input — never
`carriedByDevis`, which reads the very flag it sets.

⚠️ **The act id is cleared server-side when the money read names a different plan than the clinical link.**
`DentalRecordCollectedRow` carries no act, so an id left standing beside another plan's number would be a pair
that cannot both be true — and the modal resolves the plan *from the act*, so the fiche would quietly re-link
itself to the other treatment.

⚠️ **`schedulablePlanItems` was the wrong list, and that half of the fix reached only half the fiches.** It is
the *offer* list and drops a `Done` act, correctly, because proposing one for a new fiche is what `MarkDone`
refuses. But an existing record's link is a historical fact — and the last séance of every finished treatment
has exactly that shape. Measured: a « Facette » fiche whose act was `Done` had **no « Acte planifié » control
at all** (the Select renders only for a non-empty list) and the phantom discount intact. `page.tsx` therefore
appends the edited record's own act from **any** plan and **any** status — neither `isPlanLive` nor
`activeItems`, since a Completed plan and a Withdrawn act both still have fiches that happened.

### Deux défauts trouvés *dans* le correctif, tous deux au navigateur

1. **Hiding « Payé » would have hidden money already recorded.** `seanceIsWhollyOnTreatment` withdraws
   « Payé », « Mode » and « Total » because on such a séance they can only be 0 — true of a fiche composed
   today, false of one saved before that rule existed, and those fiches are the direct product of this very
   defect. **23** single-act fiches in the dev database carry a non-zero `AmountPaid` on a plan-linked act. The
   value was never at risk (the save sends it back from state) but unseen money that cannot be corrected is
   worse than a field reading 0, so the withhold takes a `hasStoredSeancePayment` exception — keyed on the
   **record's stored** figure, never the live state, which would un-hide the field the moment a digit was
   typed. « Total » takes no exception: it holds no recorded money and `distributeSessionTotal` has no eligible
   act, so un-hiding it would restore a control that accepts a figure and changes nothing. ⚠️ The exception
   also made the two `PaymentMethodField` instances reachable at once — it hard-codes `id="paid-method"` — so
   the lower one is now gated on the exact complement.

2. **« Payé » displayed the session total over a stored payment of 999,000 DT.** The open effect sets the field
   from the record and then `setPaidDirty(true)`, but the mirror effect runs in the **same commit** and reads
   the `paidDirty` of the render it was scheduled from — still `false` — and being declared later its update
   lands last. Whether it wins is ordering luck, which is why the defect surfaced only once the hydration
   changed the timing. The row was untouched (`AmountPaid` 999.000, written weeks earlier), so this was a
   *display* defect on a money field with the save enabled: one press would have written 40,000 over it. The
   mirror now gates on `record` — a saved amount is never a mirror of anything, which is what the flag was
   already trying to say from one commit too late.

⚠️ It also replaced advice that was actively wrong. That fiche's overpaid line used to read « Corrigez le
montant, ou **ajoutez l'acte qui manque** » — on a devis séance there is no missing act, and a dentist
following it literally adds one and prices the treatment twice. With the link hydrated it now reads « Cette
séance est portée par le devis 2026-0031 : l'argent remis au fauteuil s'encaisse sur son échéancier, pas ici. »

### Trois défauts de largeur, tous à 320 px, tous révélés par la même valeur devenue longue

The hydration replaced « Aucun » with « 2026-0027 · Couronne / bridge (par élément) », and a long value is what
finds a missing width rule. All three were measured, not reasoned about:

- **`SelectTrigger` is `w-fit`**, so the trigger sized to its content and the cell's `min-w-0` had nothing to
  shrink: **380 px in a 257 px cell**, pushing the Patient/Date/Acte row 108 px past the dialog. The primitive's
  value was already `block min-w-0 truncate` — correct, and unreachable while the box grew instead. The call
  site passes `w-full`. Any Select whose value can be long needs it.
- **The devis form's act row had no `min-w-0`**, so the `Input`'s placeholder (« Désignation de l'acte (ou
  choisir au catalogue) ») set an intrinsic floor of ~316 px and pushed « Supprimer l'acte » to a right edge of
  **343** against a scrollport ending at **296**. A capability reachable only by a sideways drag nobody tries
  (§ 0). Pre-existing, and unrelated to the counter word above it.
- **`DialogBody` was `position: static`** — and a scroll container that is not positioned does not clip its own
  `absolute` children, which is the rule the root guide already states for `AppShell`'s `<main>`. Tailwind's
  `sr-only` *is* `position: absolute`, so an offscreen label widened the scrollable area: `scrollWidth` 275
  against a 257 px box with **nothing painted out there**, i.e. a horizontal scrollbar onto blank space on the
  narrowest supported screen. One word, in the primitive, for every sheet dialog.

⚠️ The fiche's arch still scrolls sideways inside its **own** `overflow-x-auto`, and that is § 11 working as
intended, not a fourth defect — a detector that flags it is measuring against the wrong box.

## Le rendez-vous durait l'acte entier, pas la séance qu'il réservait

Un implant réparti en six séances affichait « Traitement en 6 séances. Ce rendez-vous est la 1re : « Bilan
pré-implantaire » », la frise en dessous donnait ce bilan à **45 min**, et le rendez-vous se réservait à
**60 min** — la durée catalogue de l'acte entier. La carte de l'acte imprimait « 60 min » à côté, donc les deux
chiffres se confirmaient l'un l'autre.

`totalActsDuration` savait déjà qu'**une étape réservée ne vaut pas l'acte entier** — c'est le commentaire qui
s'y trouve depuis le bridge (« Empreinte, 30 min » dans un « Bridge, 60 min ») — mais il ne le savait que pour
une étape de **devis** (`treatmentPlanItemStepId` + `stepOptions`). Une séance issue d'un `plannedProtocol` n'a
pas encore de devis : le traitement n'est créé qu'à l'enregistrement. Les séances 2 à 6 étaient donc
correctement dimensionnées, et seule la 1re — celle qu'on est en train de réserver — ne l'était pas.

`unbookedActMinutes` est le seul endroit où cette règle vit maintenant, et les deux lecteurs y passent : la
puce « 45 min » de la carte et la durée pré-remplie du formulaire. Deux points à ne pas défaire :

- **`totalActsDuration` résout lui-même les protocoles.** La répartition est le **défaut** et rien ne la réécrit
  dans l'état du dialogue (`resolvePlannedProtocols` est dérivé, jamais semé par un effet — la raison est
  écrite à sa définition), donc sans résoudre ici la durée répondait à un acte que la carte, elle, montrait déjà
  découpé.
- **Le repli reste la durée de l'acte** quand la séance n'annonce pas la sienne : `durationMinutes` d'une étape
  est nulle tant que personne ne l'a estimée, et un protocole non chronométré doit continuer à réserver ce
  qu'il réservait hier plutôt que de retomber sur les 30 min par défaut du formulaire.
