# Feature Specification: PC de secours

**Status:** APPROVED
**Challenged:** Yes (2026-10-06 — 14 gaps, see « Challenge decisions »)
**Created:** 2026-10-06
**Feature:** A cloud clinic keeps a live copy of its whole record on one PC in the cabinet; when the cabinet's
internet drops, that PC takes over and the cabinet keeps working; when it returns, everything goes back to the
cloud.

## Overview

Cloud clinics stop working whenever the cabinet loses its internet, and they depend entirely on the vendor's
server for their records. Doctors asked for the app to work without internet, and for their data to always be
on a machine they own.

With this feature, the cloud stays the main copy and every device keeps working on it from anywhere, as today.
One PC in the cabinet — the **PC de secours** — holds a live copy of the clinic: every record and every patient
file, a few seconds behind the cloud. When the cabinet's internet drops, the PC de secours takes over within
about a minute and a half: the cabinet's PCs and phones switch to it already signed in, with whatever was being
typed still in the form, and everything keeps working — fiches, rendez-vous, notes d'honoraires, caisse, devis.
People outside the cabinet can still read the cloud but not write, so two places never write at once. When the
internet returns, the PC de secours sends the cabinet's work to the cloud and the devices return to it. If the
cloud is ever lost for good, the vendor can turn the PC de secours into a standalone local server holding
everything.

Delivered in four parts, the first usable alone: **Part A — La copie** (US-1, US-2, US-8, US-9) ·
**Part B — La relève** (US-3 → US-7) · **Part C — Les appareils suivent** (FR-7) · **Part D — Prouvé**
(FR-9, FR-10). Technical approach: [`blueprint.md`](blueprint.md). Facts: [`exploration.md`](exploration.md).

### Owner decisions (2026-10-06)

| Question | Answer |
|---|---|
| Main copy | **The cloud.** The PC holds a copy and takes over only during a cut |
| Name | « PC de secours » (« PC du cabinet » already means the self-hosted server) |
| Who can have one | **Every cloud clinic**, included in the subscription; one per clinic |
| How it is set up | The Windows app **offers it** to an admin: one click, authenticator code, one Windows permission prompt |
| PC goes silent while the cabinet's internet works | **The cabinet's other devices** tell the cloud, which unlocks itself within ~2 min |
| Signing in at the cut | **None** — devices are prepared in advance and switch already signed in |
| Typing at the switch | **Kept** — the form reopens on the other side as typed |
| Cloud lost for good → standalone local server | **Vendor only** |
| Retiring a PC | It stops updating; its copy **stays on that PC until erased on that PC** |

### Calls made while writing this spec (reversible — say if one is wrong)

| Call | Value |
|---|---|
| Takeover timing | The cloud stops accepting the clinic's saves 60 s after last hearing from the PC; the PC takes over at 90 s, **only if it still reaches the cabinet's internet box**. Never both |
| Return timing | After **2 minutes** of stable internet (not 30 s — a flapping line must not switch back and forth) |
| Ready means | The cloud's last answer confirmed the PC holds everything up to that answer; the PC and the cloud act on that **one** answer. Files may still be copying |
| Clock | During a cut the PC uses the cloud's time, learned at its last contact |
| Changed on both sides | Saves made in the cloud in the seconds before the cut that the PC never received are **listed for an admin**, never overwritten silently. A record changed on both sides keeps the cabinet's version; the cloud's is listed |
| Numbers | A note, devis or avoir number is final on the cloud only once the PC de secours holds it, so it is **never** issued twice and no gap is created — except after « Reprendre la main » (AC-7.5) |
| Reminders | Held for the clinic during a cut; at the return, **at most one** per visit, none for a visit already passed |
| Subscription ending during a cut | The PC keeps accepting work up to **7 days** past the end date, then turns read-only like the cloud; the cloud's rule applies after the return |
| Freshness wording | Under one minute: « Copie à jour il y a 3 s ». Otherwise a time: « Copie de 14:32 » |
| Unencrypted disk | Warned at setup, not refused (same exposure as today's local installs) |
| Lost or stolen PC | « Déclarer perdu ou volé » retires it and makes every account choose a new password and a new authenticator |
| Who may erase a retired copy | An admin signed in **on that PC**, with an authenticator code. After retiring, only admins can open it |
| Existing archive copy / file mirror on that PC | **Keep running** on the PC de secours: a live copy deletes what the cloud deletes, so the 4 weekly copies and the never-deleting mirror are the clinic's only history on its own PC |
| « Mode hors ligne » (offline-drafts, not built) | Not offered to a clinic that has a PC de secours |
| Post-visit review pop-up | Paused during a cut |

### Challenge decisions (2026-10-06)

| Gap found | Decision |
|---|---|
| PC de secours alone cut off (cable, Wi-Fi) took over while the tablets unlocked the cloud → two writers | The PC takes over only while it reaches the cabinet's box; only devices **on the cabinet's network** can unlock a silent PC (FR-3, AC-6.2, AC-6.6) |
| A note saved 2 s before the cut may not have reached the PC → its number issued again | A number is final on the cloud only once the PC holds it (FR-3) |
| Version stamps differ between the cloud and the PC → every carried form refused, « Recharger » discarded the typing | Refused once with a switch sentence; « Recharger » keeps the typing (AC-3.2) |
| A cloud restored from backup is older than the PC → the hourly check would delete the newest records | The PC never follows a cloud that went back in time; it fills the gap (AC-9.4, FR-9) |
| Power cut in the cabinet → the cloud is locked for everyone outside | Stated as a limit; setup asks for an onduleur (AC-1.13) |
| Sign-ins, account changes and vendor actions on the cloud during a cut would be overwritten at the return | They stay live; the return never overwrites accounts or the subscription (FR-11) |
| « Same subscription rules » vs « work until the return » → block the cloud, work forever | 7 days past the end date, then read-only |
| Archive copy and file mirror stopped on the PC → no history on the clinic's own PC | Keep both |
| The PC's address changes when the box restarts → devices lose it; its certificate breaks | Devices still find it, no warning in the apps (FR-7, EC-18) |
| A plain browser shows a certificate warning on the PC | « Préparer ce navigateur » on the Paramètres card; otherwise a warning, stated (AC-3.7) |
| « A re-pressed save is recorded once » — nothing does this today | A one-time key on every save, in scope (FR-6) |
| Promotion code « from the console » — the console dies with the cloud | Issued by the vendor without the cloud (AC-9.3) |
| « Ready » vs « En retard » undefined between seconds and 15 min | One shared answer; « En retard » after 2 min not ready (FR-2) |
| The cloud itself down → banner says « Internet coupé », which is false | « Le cloud est injoignable » (AC-3.4, EC-20) |

### Limits accepted by the owner

| Limit | Why |
|---|---|
| During a cut, people outside the cabinet read but cannot write | two writers would issue the same number |
| Internet **and** the PC de secours down together → nobody in the cabinet can work | same as today |
| **Power cut in the cabinet** (PC and box dark) → the cloud is read-only for everyone outside too, until the power returns or an admin presses « Reprendre la main » | the cloud cannot tell a power cut from an internet cut; setup asks for an onduleur |
| A PC de secours cut off from the cabinet's box (its cable, its Wi-Fi) does not take over | it cannot tell its own cable from the line; the tablets keep working on the cloud |
| A plain browser not prepared in advance shows a security warning on the PC de secours | its certificate is the cabinet's own; the Paramètres card prepares a browser in one step |
| Cloud lost suddenly → the last 1–2 seconds may be missing from the PC | copying is never instant |
| The PC de secours must be on during opening hours | off, it catches up when switched on, but cannot take over |
| ~90 s between the cut and the takeover | safety gap |
| A plain browser does not switch by itself | the Windows and Android apps do; a browser opens the PC's saved address |
| SMS reminders and Google Agenda wait for the internet | sent at the return |
| Account changes (new user, password, authenticator, role) wait for the internet | the cloud owns accounts |
| PC de secours dies during a cut and an admin forces the cloud back → that cut's work on it is listed for re-entry, and a number printed on it is issued again by the cloud (AC-7.5) | rare; the only path that can cost typing |

---

## User Stories

### US-1: Set up a PC de secours
As a clinic admin, I want to turn a PC in the cabinet into a PC de secours in one step, so that the cabinet is
covered without an installer hunt.

**Acceptance Criteria:**
- **AC-1.1:** When the Windows app starts on a PC where an admin is signed in and the clinic has no PC de secours,
  it shows once: « Garder une copie du cabinet sur ce PC ? » with « Oui », « Plus tard », « Pas sur ce PC ».
- **AC-1.2:** The offer appears only at app start, never over an open form.
- **AC-1.3:** « Plus tard » asks again 7 days later on that PC. « Pas sur ce PC » never asks again on that PC.
- **AC-1.4:** « Oui » asks for the admin's authenticator code, then shows one Windows permission prompt; after
  that, installation, pairing and the first copy need nobody.
- **AC-1.5:** The same action is in the Windows app's menu on any PC (« Installer le PC de secours ici… ») and asks
  for an admin's email, password and authenticator code — so the always-on reception PC can be chosen even when a
  secretary is signed in.
- **AC-1.6:** On a PC with a battery, the offer adds: « Ce PC est un portable : s'il quitte le cabinet, il ne
  pourra pas prendre le relais. » It does not refuse.
- **AC-1.7:** On a PC whose disk is not encrypted, the offer adds: « Le disque de ce PC n'est pas chiffré : la copie
  du cabinet y sera lisible si le PC est volé. » It does not refuse.
- **AC-1.8:** On a PC without enough free space for the clinic's records and files twice over, « Oui » is refused
  with « Il faut N Go libres sur ce PC (M Go disponibles). »
- **AC-1.9:** Until the first copy is complete, the Windows app and « Paramètres → PC de secours » show « Copie en
  cours (40 %) ». A first copy interrupted by a shutdown resumes where it stopped.
- **AC-1.10:** Once one PC is set up — or being set up — the offer disappears on every device, and a second PC is
  refused with « Un PC de secours est déjà installé (ou en cours d'installation) : PC-ACCUEIL. »
- **AC-1.11:** Refusing the Windows prompt installs nothing; the offer stays available.
- **AC-1.12:** The offer is never shown in a browser or on Android.
- **AC-1.13:** The offer always adds: « Branchez ce PC et la box internet sur un onduleur : sans courant au cabinet,
  personne ne peut enregistrer, même à l'extérieur. » It does not refuse.

### US-2: Know the PC de secours is ready
As a clinic admin, I want to see whether the PC de secours can take over right now, so that I learn about a
problem before the internet goes, not during.

**Acceptance Criteria:**
- **AC-2.1:** « Paramètres → PC de secours » (admin only) shows the PC's name, one state from FR-2 with its
  sentence, and the list of the clinic's devices prepared to switch (FR-7) with when each last checked in.
- **AC-2.2:** When the PC de secours is not ready during the clinic's opening hours (off, late by more than 15 min,
  disk nearly full, copy not matching and not repaired), every admin gets one bell row naming the problem.
- **AC-2.3:** Nobody but admins sees anything while the PC is ready.
- **AC-2.4:** A failure to load the state reads « Impossible de lire l'état du PC de secours » with « Réessayer » —
  never « aucun PC de secours ».

### US-3: Keep working when the cabinet's internet drops
As anyone working in the cabinet, I want the app to keep working when the internet drops, so that a patient in the
chair is never blocked by the line.

**Acceptance Criteria:**
- **AC-3.1:** About 90 s after the cut, if the PC de secours still reaches the cabinet's internet box, every Windows
  and Android app on the cabinet's network switches to it by itself, **already signed in** as the same person.
- **AC-3.2:** A form open at the switch reopens on the PC de secours with everything typed; nothing is saved until
  the person presses Enregistrer. Its first save is refused once with « Le serveur a changé pendant votre saisie —
  vérifiez puis enregistrez à nouveau. »; « Recharger » keeps everything typed, and the next Enregistrer saves.
  The same applies at the return (AC-5.1).
- **AC-3.3:** A save pressed during the ~90 s before the switch is refused with « Internet coupé — le PC de secours
  prend le relais dans quelques instants. », the form stays open, and the save can be pressed again after the
  switch.
- **AC-3.4:** While the PC de secours is in charge, every screen shows « Internet coupé — le cabinet travaille sur
  le PC de secours. Tout partira dans le cloud au retour d'internet. » When the cabinet's internet works but the cloud
  does not answer, it starts « Le cloud est injoignable » instead of « Internet coupé ».
- **AC-3.5:** Everything works on the PC de secours with the same roles, « Mode discret » and rules as on the cloud,
  except the « online only » list (FR-5), which is refused with « Possible uniquement quand internet est revenu au
  cabinet. »
- **AC-3.6:** A file not yet copied to the PC de secours opens with « Ce fichier n'est pas encore sur le PC de
  secours. » A file kept in the coffre opens as it does on the cloud. Every other file opens as usual.
- **AC-3.7:** In a plain browser, staff open the PC de secours from the address saved in « Paramètres → PC de
  secours » (shown with a QR code to scan on a tablet); they sign in there as usual. A browser prepared in advance
  from that card (« Préparer ce navigateur ») opens it with no warning; any other shows a security warning.
- **AC-3.8:** If the PC de secours was not ready when the internet dropped, devices show today's « Impossible de
  joindre le serveur » screen with one more line: « Le PC de secours n'était pas à jour : il ne peut pas prendre le
  relais. »
- **AC-3.9:** Takeover and every switch are announced to screen readers, not only drawn.
- **AC-3.10:** A phone in the cabinet on mobile data (not the cabinet's Wi-Fi) stays on the cloud, read-only (US-4).
- **AC-3.11:** If the PC de secours restarts during a cut (Windows Update, power), it is still in charge when it comes
  back; meanwhile devices show today's « Impossible de joindre » screen and reconnect by themselves. Windows Update
  never restarts it during opening hours.

### US-4: Read the cloud from outside during a cut
As a doctor away from the cabinet, I want to know why I cannot save, and how fresh what I read is, so that I do
not trust an agenda the desk has changed since.

**Acceptance Criteria:**
- **AC-4.1:** Every cloud screen of that clinic shows « Le cabinet travaille sur le PC de secours depuis 10:42 —
  ici, lecture seule. Données arrêtées à 10:42. »
- **AC-4.2:** Save buttons stay; pressing one is refused with « Le cabinet travaille sur le PC de secours depuis
  10:42. Ici, vous pouvez consulter mais pas enregistrer jusqu'au retour d'internet au cabinet. », the form stays
  open with what was typed. Signing in, account changes by admins and the vendor's actions still work (FR-11).
- **AC-4.3:** When an expired subscription and a cut apply together, both banners show, the cut's first.

### US-5: Go back to the cloud when the internet returns
As anyone in the cabinet, I want the cabinet's work to reach the cloud on its own, so that nobody has to think
about it.

**Acceptance Criteria:**
- **AC-5.1:** After 2 minutes of stable internet, the PC de secours sends its work to the cloud and the devices
  return to the cloud, already signed in, with open forms kept as in AC-3.2.
- **AC-5.2:** During the few seconds of the return, a save is refused with « Retour au cloud en cours —
  réessayez dans quelques secondes. » and the form stays open.
- **AC-5.3:** After the return: every note, devis and avoir number continues without a gap or a duplicate;
  la caisse, « Solde patient » and « Créances » include the cut's payments once; stock includes the cut's use
  once; rent is never posted twice.
- **AC-5.4:** Reminders for visits booked or changed during the cut are sent — at most one per visit, none for a
  visit already passed. Appointments booked during the cut reach Google Agenda.
- **AC-5.5:** Every change made on the PC de secours appears in « Journal d'activité » under its author, marked
  « via PC de secours ».
- **AC-5.6:** Changes made in the cloud just before the cut that the PC de secours never received are kept or
  listed — never lost: every admin gets a bell row « N modifications faites dans le cloud juste avant la coupure
  sont à vérifier », opening a list that shows each one, who made it, and the cabinet's version when both changed
  the same record.
- **AC-5.7:** Settings the PC de secours does not hold (SMS/WhatsApp keys, Google Agenda link) are untouched by the
  return.
- **AC-5.8:** Cloud screens that were open during the cut refresh by themselves after the return.
- **AC-5.9:** If the return cannot finish (internet drops again, the cloud refuses, an update fails), the cabinet
  keeps working on the PC de secours and it tries again on its own; after 15 minutes stuck, admins get a bell row
  and the vendor is alerted.

### US-6: The PC de secours goes off while the internet works
As anyone in the cabinet, I want the cabinet to keep working on the cloud when the PC de secours is switched off
or crashes, so that one PC does not block everyone.

**Acceptance Criteria:**
- **AC-6.1:** A PC de secours shut down, restarted (Windows Update included) or put to sleep properly tells the
  cloud first; nothing is locked.
- **AC-6.2:** When it goes silent without warning (crash, power cut on that PC, cable unplugged) while the cabinet's
  other devices still reach the cloud, the cloud unlocks itself within about 2 minutes, using those devices'
  reports that they reach the cloud but not the PC de secours. Only a device on the cabinet's network counts; a
  phone on mobile data or a device outside the cabinet never does.
- **AC-6.3:** In that time, saves are refused with « Le PC de secours ne répond plus — réessayez dans un instant. »
  and forms stay open.
- **AC-6.4:** Admins get a bell row « Le PC de secours ne répond plus depuis 10:42 ».
- **AC-6.5:** When the PC de secours comes back, it catches up by itself and becomes ready again.
- **AC-6.6:** A PC de secours cut off from the cabinet's box (cable unplugged, Wi-Fi lost) never takes over, so it
  and the cloud never both accept saves; on that PC the Windows app shows today's « Impossible de joindre » screen.

### US-7: Force the cloud back when the PC de secours is lost during a cut
As a clinic admin, I want to put the cloud back in charge when the PC de secours died during a cut, so that the
clinic is not read-only for days.

**Acceptance Criteria:**
- **AC-7.1:** On the cloud, « Paramètres → PC de secours » offers « Reprendre la main » to admins while the clinic is
  locked, after an authenticator code and a warning: « Ce que le cabinet a enregistré sur PC-ACCUEIL depuis 10:42 ne
  partira pas dans le cloud : il faudra le saisir à nouveau, et les numéros de notes émis sur ce PC seront en double.
  Appelez le cabinet avant de continuer. »
- **AC-7.2:** It is a row in « Journal d'activité » naming the admin.
- **AC-7.3:** When that PC de secours comes back, it stops taking work, catches up from the cloud, and shows admins
  « À reprendre » — every record entered on it during that cut that the cloud never received.
- **AC-7.4:** Each row shows what it is (patient, act, amount, date, author) and offers « Repris » once a person has
  entered it on the cloud; the list stays, with a daily bell row for admins, until every row is « Repris ».
- **AC-7.5:** A note, receipt or devis printed on the PC during that cut is flagged « Document n° 2026-0042 remis au
  patient : ce numéro n'est pas valable, à refaire. »
- **AC-7.6:** « À reprendre » reads as cards on a phone, a table from desk width, can be printed, and has three
  distinct empty states: nothing to re-enter · nothing matching the filter · failed to load.

### US-8: Retire, replace, erase, or declare a PC de secours stolen
As a clinic admin, I want to stop using a PC de secours and decide what happens to the copy on it, so that a
replaced PC never leaves with patient files by accident.

**Acceptance Criteria:**
- **AC-8.1:** « Retirer ce PC » (admin) stops the copy and frees the clinic to set up another; the retired PC shows
  « Copie arrêtée le 06/10 » and opens read-only, for admins only.
- **AC-8.2:** On a retired PC, « Effacer la copie » (admin signed in on that PC + authenticator code) erases every
  record and file of the clinic from it.
- **AC-8.3:** Uninstalling asks « Effacer aussi la copie du cabinet ? » (ticked by default) and counts as retiring.
- **AC-8.4:** « Déclarer perdu ou volé » (admin + authenticator code) retires the PC and makes every account of the
  clinic choose a new password — and, if it had one, a new authenticator — at its next sign-in.
- **AC-8.5:** Retire, erase, uninstall and « perdu ou volé » are each a row in « Journal d'activité ».
- **AC-8.6:** Pressed while the clinic is locked, « Retirer ce PC » and « Déclarer perdu ou volé » also take the cloud
  back, with AC-7.1's warning.

### US-9: The vendor sees every PC de secours and can recover a lost cloud
As the vendor, I want to know which clinics are covered and to be told when a PC de secours fails, so that « the
data is safe on the PC » is a fact I can check.

**Acceptance Criteria:**
- **AC-9.1:** The console clinic list shows a « PC de secours » column: Aucun · Prêt · En retard · Éteint depuis … ·
  En relève depuis … · Ne correspond pas · Retiré.
- **AC-9.2:** Console accounts are emailed (through the alert channel shared with `server-loss-recovery` Part 3)
  when a PC de secours is unseen for 24 h, behind by more than 15 min during opening hours, not matching after its
  own repair, or stuck returning for 15 min.
- **AC-9.3:** If the cloud is lost for good, only the vendor can turn a PC de secours into a standalone local
  server, with a one-time code the vendor issues **without the cloud** (the console lives on the same server and is
  lost with it); afterwards it is an ordinary local install.
- **AC-9.4:** If the cloud comes back from a backup older than the PC de secours, the PC keeps its copy, sends the
  cloud everything it is missing (the minutes before the loss, then the cut's work) and the vendor is alerted. It
  never deletes what the cloud no longer has.

---

## Functional Requirements

### FR-1: What the PC de secours holds
- Shall hold every record of the clinic, every patient file stored in the cloud, the clinic's accounts (so people
  can sign in), its subscription dates and its bell.
- Shall not hold: the SMS/WhatsApp keys, the Google Agenda link, other clinics' anything, the vendor's data, the
  coffre (it already lives on the cabinet's own machine).
- Patient data and sign-in secrets on the PC are readable only on that PC.

### FR-2: States of a PC de secours
| State | Sentence | Seen by |
|---|---|---|
| Installation | « Copie en cours (40 %) » | admin, Windows app on that PC |
| Prêt | « Copie à jour il y a 3 s » / « Copie de 14:32 » | admin |
| En retard | « Copie en retard de 25 min » | admin, vendor |
| Éteint | « Éteint depuis 08:12 » | admin, vendor |
| En relève | « Le cabinet travaille sur ce PC depuis 10:42 » | everyone (banner), vendor |
| Retour au cloud | « Retour au cloud en cours » | everyone (banner) |
| Mise à jour | « Mise à jour du PC de secours en cours » | admin |
| Disque presque plein | « Il reste 2 Go sur PC-ACCUEIL » | admin, vendor |
| Ne correspond pas | « La copie ne correspond pas au cloud — réparation en cours » | admin (only if the repair fails), vendor |
| Retiré | « Copie arrêtée le 06/10 » | admin, vendor |
| Échec d'installation | the reason, with « Réessayer » | admin |
Every state is words plus an icon, never colour alone.

- **Prêt** = the cloud's last answer confirmed the PC holds everything up to that answer. The PC and the cloud act
  on that same answer, never on two separate judgements.
- **En retard** = not ready for more than 2 min. Bell row and vendor alert after 15 min during opening hours
  (AC-2.2, AC-9.2).

### FR-3: Takeover
- The cloud shall stop accepting the clinic's saves 60 s after it last heard from a ready PC de secours; the PC
  shall take over at 90 s, **only while it still reaches the cabinet's internet box**. At no moment shall both
  accept saves.
- Only devices on the cabinet's network count toward unlocking a silent PC (AC-6.2).
- A note, devis or avoir number is final on the cloud only once the PC de secours holds it. If the PC does not
  confirm within a few seconds, the save is refused with « Le PC de secours ne répond plus — réessayez dans un
  instant. » and the form stays open. A PC that is off and said so (AC-6.1) is not waited for.
- A PC that was not ready at its last contact shall not take over (AC-3.8).
- During a cut, the PC shall date everything with the cloud's time as last learned — across its own restarts too.

### FR-4: Work on the PC de secours
- Every screen and action of the clinic app works, with the same rights, « Mode discret » and subscription rules,
  except FR-5. A subscription ending during the cut allows work up to 7 days past its end date, then the PC is
  read-only like the cloud.
- New patient files added during a cut are kept on the PC and reach the cloud at the return, within the cloud's
  usual limits.

### FR-5: Online only during a cut
Creating or deactivating an account · changing a password, an authenticator, a role · « Rappels » settings ·
connecting or syncing Google Agenda · « Abonnement » · downloading or restoring the clinic archive. Each refused
with AC-3.5's sentence. A person locked out during a cut stays locked out until the 15-minute lock ends or the
internet returns.

### FR-6: Return to the cloud
As AC-5.1 → AC-5.9. Work saved once is never recorded twice — including a save whose answer was lost at the moment
of the cut and was pressed again on the PC.
- ⚠️ Nothing does this today: a save pressed again after a lost answer makes a second row even with no PC de
  secours. Every save gains a one-time key, carried with the form across a switch. In scope.

### FR-7: Devices switch by themselves
- Every Windows and Android app of the clinic prepares itself while online: it learns the PC's address and trusts
  it, with **no certificate step and no address to type**, and is signed in to it in advance.
- A device still finds and trusts the PC de secours on the cabinet's network after its address changed (box
  restarted — the first thing people do when the internet drops).
- A device knows whether it is on the cabinet's network; only then does it switch (AC-3.1) or report (AC-6.2).
- A device's preparation ends when its person signs out, and when their account is disabled (at the next contact).
- Only a device whose person is signed in switches.

### FR-8: Journal d'activité
One row each for: setup, first copy complete, takeover, return, « Reprendre la main », retire, erase, uninstall,
« perdu ou volé », promotion. Every change made on the PC keeps its author, marked « via PC de secours ».

### FR-9: The copy is checked, not assumed
The PC de secours compares itself with the cloud every hour while ready; a difference is repaired by itself; only
a difference it cannot repair reaches admins and the vendor. A cloud that went back in time (restored from a
backup) is never « repaired » toward: AC-9.4 applies.

### FR-11: The cloud during a cut
- Still accepted for that clinic: signing in; account changes by its admins (create, disable, password,
  authenticator, role); the vendor's actions (payment, suspension, password or authenticator reset, WhatsApp
  quota).
- The return never overwrites accounts or the subscription. Sign-in traces from the cabinet (last login, a used
  recovery code, a lockout) are merged, so a recovery code used on the PC cannot be used again.

### FR-12: History on the PC de secours
The Windows app's weekly archive copy and the patient file mirror keep running on the PC de secours, as on any
other PC.

### FR-10: Proof before release
One automated test cuts the network between a cloud and a PC de secours, works on both sides, restores it, and
checks that the money, the numbers and the records match. It also covers: a PC cut off from the box (no takeover
while the cloud unlocks), a note saved seconds before the cut, and a cloud restored from an older backup. One real
rehearsal on a Windows PC in a cabinet setting.

---

## API Endpoints

All under `/api/relay/*`, error body `{ error, code }` with the French sentence. Device and PC endpoints
authenticate with the PC's own key or the device's preparation, never with a staff password.

| Method · path | Who | Purpose | Refusals (status · code) |
|---|---|---|---|
| `POST /api/relay/pairing-codes` | admin + authenticator | start a setup | 409 `relay_already_paired` |
| `POST /api/relay/pair` | installer on the PC | pair with the code | 410 `pairing_code_expired` · 409 `relay_already_paired` |
| `GET /api/relay/status` | admin | state, devices, address, QR | — |
| `DELETE /api/relay` | admin | retire | — |
| `POST /api/relay/lost` | admin + authenticator | « perdu ou volé » | — |
| `POST /api/relay/reclaim` | admin + authenticator | « Reprendre la main » | 409 `relay_not_holding` |
| `GET /api/relay/for-this-device` | signed-in device | the PC's address and trust, to prepare | 404 `no_relay` |
| `GET /api/relay/review` | admin | « modifications à vérifier » after a return | — |
| PC ↔ cloud (copy, heartbeat, return, files, check) | the PC | FR-1, FR-3, FR-6, FR-9 | 409 `relay_version_mismatch` |
| any save, on the cloud during a cut | everyone | — | 423 `clinic_on_relay` |
| any save, during a return | everyone | — | 423 `relay_handing_back` |
| any save, PC silent, not yet unlocked | everyone | — | 423 `relay_unreachable` |
| a numbered save (note, devis, avoir), PC not confirming | everyone | — | 423 `relay_unconfirmed` |
| an FR-5 action on the PC | everyone | — | 423 `online_only` |

---

## Device & Interface Behaviour

**Leading device:** the reception PC (it is usually the PC de secours, and where a cut is noticed). Second: the
chairside tablet and phones that must switch without anyone touching them. People outside the cabinet are mostly
on phones.

| Surface | Phone (< 640) | Tablet portrait (640–1023) | Desktop |
|---|---|---|---|
| Cut / return / read-only banners | one line + icon; detail on a second line; never covers the page title | one line | one line |
| « Paramètres → PC de secours » card | stacked: state, then actions full width; device list as cards | same as phone | state left, actions right; device list as a table |
| Setup offer | not offered (Android) | not offered | dialog in the Windows app; fits a 730 px-tall window with its buttons visible |
| « À reprendre » | cards: patient + what + amount, « Repris » 44 px | cards | table from `lg:` |
| « Modifications à vérifier » | cards | cards | table from `lg:` |

- **« Préparer ce navigateur »** (AC-3.7) sits on the Paramètres card: a button on the device itself, a QR code to
  prepare a tablet or phone.
- **Touch paths:** every action is a visible button; nothing appears on hover only. « Repris » and « Réessayer »
  are 44 px on a coarse pointer.
- **Rotation / switch:** typed input survives rotating a tablet and switching server (AC-3.2).
- **Named exceptions:** setup happens only in the Windows app (a browser or a phone cannot install a server); the
  Android app and browsers show « Installer le PC de secours depuis l'application Windows d'un PC du cabinet. » in
  the Paramètres card instead.

---

## Scope

### In Scope
- Cloud clinics; one PC de secours per clinic on Windows 10/11.
- Setup offer, install, first copy, live copy, states, alerts, devices preparing themselves.
- Takeover, work during a cut, return, « modifications à vérifier », « Reprendre la main », « À reprendre ».
- Retire, erase, uninstall, « perdu ou volé ».
- Console column, vendor alerts, vendor-only promotion.
- The cloud during a cut (FR-11), a one-time key on every save (FR-6), history kept on the PC (FR-12).
- « Recharger » keeps the typing on a form carried across a switch (AC-3.2).
- The automated cut test and one real rehearsal.

### Out of Scope
- Self-hosted local clinics (their backups: `server-loss-recovery` Part 6, owned elsewhere).
- What a promoted PC becomes afterwards (an ordinary local install, under that product's rules).
- The iOS app (never built); Safari gets the browser behaviour.
- A Mac or Linux PC de secours; two per clinic.
- Merging changes automatically.
- The coffre.
- « Mode hors ligne » (offline-drafts) for clinics with a PC de secours.

---

## Edge Cases

### EC-1: Internet flaps every minute
- **Scenario:** The line drops and returns repeatedly.
- **Expected:** No takeover before 90 s of silence; no return before 2 min of stable internet. Staff see at most
  the AC-3.3 refusal during short drops.

### EC-2: PC off at opening, cut at 08:31
- **Scenario:** The PC de secours was switched on at 08:30 and is still catching up.
- **Expected:** It does not take over; devices show AC-3.8's line; admins get a bell row when the internet returns.

### EC-3: Cut during the first copy
- **Scenario:** Setup is at 40 % when the internet drops.
- **Expected:** No takeover; the copy resumes when the internet returns.

### EC-4: Cut lasting three days
- **Scenario:** The cabinet's line is down from Friday to Monday.
- **Expected:** The cabinet works on the PC throughout; the return sends everything; outside users read-only
  throughout.

### EC-5: Cut across midnight, the 1st of the month, or New Year
- **Scenario:** Payments and notes on both sides of the boundary.
- **Expected:** La caisse days, the month's rent and the new year's number series are exactly as if there had been
  no cut.

### EC-6: A save lost in the cut
- **Scenario:** A payment reached the cloud at the instant of the cut; its answer never came back; the secretary
  presses Enregistrer again on the PC.
- **Expected:** One payment after the return.

### EC-7: Someone at home saved just before the cut
- **Scenario:** A doctor at home moved a rendez-vous 10 s before the cut; the desk moved the same one on the PC.
- **Expected:** The desk's version is kept; the doctor's change is in « Modifications à vérifier » with both
  versions.

### EC-8: Two admins accept the offer on two PCs at once
- **Expected:** The second is refused with AC-1.10's sentence; nothing half-installed is left on it.

### EC-9: Setup stopped half-way and never finished
- **Scenario:** The PC was switched off at 40 % and not switched on again.
- **Expected:** After 24 h it is released: the offer returns and another PC can be set up; admins see « Installation
  abandonnée sur PC-ACCUEIL ».

### EC-10: Wrong clock on the PC
- **Scenario:** The PC's clock is a day behind.
- **Expected:** During a cut, dates follow the cloud's time; admins see « L'horloge de PC-ACCUEIL est fausse » at
  the next contact.

### EC-11: Cloud update during a cut
- **Scenario:** A new version ships while the cabinet is on the PC.
- **Expected:** At the return, the PC updates itself first, then sends its work; the cabinet sees « Retour au cloud
  en cours » a little longer.

### EC-12: Disk fills during a cut
- **Expected:** New files are refused on the PC with « Plus de place sur le PC de secours pour ce fichier. »;
  records keep saving.

### EC-13: Account disabled in the cloud during a cut
- **Expected:** That person can keep working on the PC until the return, then is signed out everywhere.

### EC-14: PC de secours stolen
- **Expected:** « Déclarer perdu ou volé » (AC-8.4); the vendor console shows « Retiré ».

### EC-15: Subscription ends during a cut
- **Expected:** The cabinet keeps working on the PC up to 7 days past the end date, then the PC is read-only; at the
  return the cloud is read-only as for any expired subscription; the cabinet's work is kept.

### EC-16: Reminder 24 h and 6 h both due at the return
- **Expected:** One reminder for that visit.

### EC-17: The PC de secours's cable is unplugged while the cabinet's internet works
- **Expected:** No takeover. The cloud unlocks within ~2 min from the tablets' reports. That PC shows « Impossible
  de joindre » until the cable is back, then catches up.

### EC-18: Box restarted during a cut
- **Scenario:** The PC de secours gets a new address on the cabinet's network.
- **Expected:** Devices still find it and keep working, already signed in.

### EC-19: Power cut in the cabinet
- **Expected:** Nobody in the cabinet works; the cloud is read-only outside; admins get AC-6.4's bell row and may
  « Reprendre la main ». When the power returns with the internet still down, the PC takes over as usual — unless
  « Reprendre la main » was pressed (AC-7.3 then applies at the return).

### EC-20: The cloud itself is down (outage, or an update longer than 90 s)
- **Expected:** Every cabinet with a ready PC de secours takes over; the banner says « Le cloud est injoignable »;
  return as usual.

### EC-21: Cloud restored from a backup
- **Scenario:** The server is rebuilt from a backup 5 min older than its loss; the PC de secours was in charge for
  the 4 h of the rebuild.
- **Expected:** AC-9.4 — nothing is lost, numbers continue without a gap or a duplicate.

### EC-22: Note printed 2 s before the cut
- **Expected:** The save waited for the PC de secours (FR-3), so the PC's next note takes the next number.

### EC-23: A phone in the cabinet on mobile data during a cut
- **Expected:** It stays on the cloud, read-only (AC-3.10), and does not unlock it.

---

## Non-Functional Hints

- **Performance:** in normal use the copy is seconds behind. A typical clinic's first copy (~100 MB) takes
  minutes; one with 10 GB of radiographs can take hours on a cabinet line and must not slow the cabinet's own use.
- **Security:** sign-in secrets on the PC open only on that PC; the copy is opened by clinic accounts only; a
  retired copy by admins only; every sensitive action needs the authenticator.
- **Accessibility:** state changes announced (`role="status"`), never re-announcing « il y a 3 s » every second;
  states are words + icon; focus stays in the reopened form after a switch.
- **Scalability:** the cloud keeps one PC per clinic for every cloud clinic at once without slowing anyone's saves
  (a numbered save may wait about 1 s for the PC, FR-3).
- **Cloud updates:** the cloud's own downtime during an update stays under 60 s, or every clinic's cloud locks and
  past 90 s every cabinet takes over (measured worst today: 25 s).

## Dependencies

- `server-loss-recovery` Part 3 — the vendor alert channel (owned by another session; specced, not built).
- `server-loss-recovery` rebuild — a restored cloud must hand its gap to the PC de secours (AC-9.4).
- `offline-drafts` spec — must state it is not offered to a clinic with a PC de secours (not added yet).
- Windows app self-update (Velopack) and a new Android app version.
- `clinic-archive-auto-copy` / `patient-file-mirror` — **keep running** on the PC de secours (FR-12).
- New, built here: a one-time key on every save (FR-6) — nothing exists today.
- New, built here: « Recharger » keeping the typing on a carried form — 5 of the 6 forms that save a version
  discard it today (`edit-appointment-dialog`, `edit-patient-dialog`, `treatment-plan-form-modal`,
  `plan-workspace`, `plan-item-steps-dialog`).
- New: today devices find a cabinet server only by a typed address, and its certificate is tied to the address it
  had at install (FR-7).

## Open Questions

None. Challenged 2026-10-06. Next: `/plan-feature` (the blueprint predates the challenge — see its banner).
