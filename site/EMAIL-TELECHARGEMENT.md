# L'email d'installation — texte type

⚠️ **Ne jamais mettre le `.exe` en pièce jointe.** Gmail bloque les exécutables, y compris à l'intérieur
d'un `.zip` ou d'un `.rar` ([liste officielle](https://support.google.com/mail/answer/6590)). Outlook fait
pareil. Le mail arrive vidé de sa pièce jointe, ou n'arrive pas. L'email porte **un lien** vers
<https://apexa.tn/#telecharger>, jamais le fichier.

⚠️ **Le lien de téléchargement lui-même** est `https://app.apexa.tn/api/meta/client-download`. Il est
public (route anonyme `MetaController.ClientDownload`) et sert toujours la dernière version déposée dans
`deploy/updates/` sur le VPS. Ne pas coller ce lien-là dans l'email : envoyer vers la section, qui montre
l'avertissement Windows d'avance. Un lien direct, c'est un dentiste seul devant un écran bleu.

---

## Objet

```
Votre installation APEXA — le lien et les 2 minutes que ça prend
```

## Corps

```
Bonjour Docteur [NOM],

Voici le lien pour installer APEXA sur le PC du cabinet :

  https://apexa.tn/#telecharger

Comptez deux minutes. Un point pour éviter la mauvaise surprise : au lancement
du fichier, Windows affiche un écran bleu « Windows a protégé votre PC ».
C'est normal — Windows affiche cela pour tout logiciel récent qu'il ne connaît
pas encore, ce n'est pas une alerte de virus.

Il suffit de cliquer sur « Informations complémentaires », puis sur
« Exécuter quand même ». Le lien ci-dessus vous montre l'écran en image.

Une fois installé, APEXA se met à jour tout seul : vous ne reverrez plus
jamais cet écran.

La moindre hésitation, répondez à ce message ou écrivez à contact@apexa.tn —
nous répondons le jour même.

Bien à vous,
[PRÉNOM NOM]
APEXA
```

---

## Ce que le texte fait, et pourquoi

| Choix | Raison |
|---|---|
| L'avertissement est annoncé **avant** qu'il n'arrive | Un dentiste qui le découvre seul referme et appelle. C'est le seul vrai coût de l'absence de signature |
| « ce n'est pas une alerte de virus » écrit noir sur blanc | C'est exactement la conclusion qu'un utilisateur tire de cet écran |
| « vous ne reverrez plus jamais cet écran » | Vrai : les mises à jour Velopack sont silencieuses et per-user, l'écran ne concerne que la première installation |
| Un lien vers la section, pas vers le `.exe` | Elle montre l'écran bleu en image, avec les deux boutons numérotés |

## Ce qui ferait vraiment disparaître l'écran

Un **certificat de signature de code OV** (~250-350 $/an), câblé dans
`.github/workflows/client-installer.yml` via `vpk pack --signParams`. Rien de gratuit ne le supprime en
distribution publique : ni un certificat auto-signé (SmartScreen juge la réputation, pas la chaîne), ni
SignPath Foundation (qui exige un dépôt public), ni la soumission à Microsoft (un fichier à la fois, à
refaire à chaque version). La réputation s'accumule sur **l'éditeur**, donc elle se transmet aux versions
suivantes — c'est ce qui rend le certificat rentable dès qu'on distribue publiquement.
