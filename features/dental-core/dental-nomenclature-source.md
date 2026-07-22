# Dental Act Nomenclature — Seed Source (Tunisia, CNAM/STMDLP)

**Source:** CNAM « Liste des actes des professions de santé » (`RBactes.pdf`, pages 33–37) — *Actes effectués par les médecins dentistes, Titre III, Chapitre VII: Dents et Gencives*. Tariff values from the CNAM *Convention sectorielle des médecins dentistes de libre pratique* (Déc. 2020) + `TARIFS-v1.pdf`.

**Code system:** `DCH` + section (2 digits) + item (3 digits), e.g. `DCH020060`. `DCH` = the dental chapter code prefix (this is the real "DCH" — not a separate system).

**Tariffs (for reference; CNAM reimbursement only — dentist fees are free/*libres*):**
- Consultation dentiste (`Cd`) = **30,000 DT** · Consultation spécialiste/orthodontiste (`Cds`) = **45,000 DT**
- Lettre-clé **`D` = 3,000 DT** per coefficient unit → CNAM reimbursement ≈ `coefficient × 3,000 DT`.

**Coefficients (cotation) are NOT in the source** — they're in the NGAP arrêté. Seed `Coefficient` = null (admin-editable). `LettreCle` = `"D"` for every act below.

**Not in the CNAM list** (so must be free-text plan lines, not catalog acts): fixed prosthetics (couronne céramique, bridge fixe), implants, blanchiment — CNAM doesn't reimburse these.

`AP` column: **AP** = Accord Préalable required · **—** = Sans Accord Préalable.

## Section I — Soins conservateurs, obturations définitives (`Category: Soins conservateurs`)
| Code | Désignation | AP |
|------|-------------|----|
| DCH010010 | Cavité simple — traitement global (obturation) | — |
| DCH010020 | Cavité simple — traitement global, dent permanente enfant < 14 ans | — |
| DCH010030 | Cavité composée — traitement global intéressant deux faces | — |
| DCH010040 | Cavité composée — deux faces, dent permanente enfant < 14 ans | — |
| DCH010050 | Traitement global intéressant trois faces et plus | — |
| DCH010060 | Pulpotomie/pulpectomie coronaire avec obturation de la chambre pulpaire (traitement global) | — |
| DCH010070 | Coiffage pulpaire / pulpectomie coronaire simple (hors obturation définitive) | — |
| DCH010080 | Pulpectomie coronaire et radiculaire, obturation des canaux — groupe incisivo-canin | — |
| DCH010090 | Pulpectomie coronaire et radiculaire — groupe prémolaire | — |
| DCH010100 | Pulpectomie coronaire et radiculaire — groupe molaire | — |

## Section II — Soins chirurgicaux (`Category: Soins chirurgicaux`)
| Code | Désignation | AP |
|------|-------------|----|
| DCH020010 | Résection de capuchon muqueux d'une dent de sagesse | — |
| DCH020020 | Incision d'abcès et drainage | — |
| DCH020030 | Extraction dentaire simple — groupe incisivo-canin | — |
| DCH020040 | Extraction dentaire simple — groupe prémolaire | — |
| DCH020050 | Extraction dentaire simple — groupe molaire | — |
| DCH020060 | Extraction de plusieurs dents dans une même séance | — |
| DCH020070 | Extraction multiple — chacune des suivantes, groupe incisivo-canin | — |
| DCH020080 | Extraction multiple — chacune des suivantes, groupe prémolaire | — |
| DCH020090 | Extraction lors d'accidents inflammatoires/osseux aigus — majoration pour la première | — |
| DCH020100 | Extraction lors d'accidents aigus — majoration pour chacune des suivantes | — |
| DCH020110 | Extraction de la/des racine(s) d'une dent par alvéolectomie | — |
| DCH020120 | Extraction d'une dent en malposition | — |
| DCH020130 | Tamponnement alvéolaire pour hémorragie post-op (séance autre que l'extraction) | — |
| DCH020140 | Extraction chirurgicale d'une dent incluse ou enclavée | — |
| DCH020150 | Extraction chirurgicale d'une canine incluse | — |
| DCH020160 | Extraction chirurgicale d'un odontoïde ou dent incluse/enclavée | — |
| DCH020170 | Dent en désinclusion, couronne partiellement/entièrement sous-muqueuse | — |
| DCH020180 | Dent en désinclusion, couronne sous-muqueuse position palatine ou linguale | — |
| DCH020190 | Dent ectopique et incluse (coroné, gonion, branche montante, bord basilaire) | — |
| DCH020200 | Germectomie | — |
| DCH020210 | Germectomie d'une dent de sagesse | — |
| DCH020220 | Extraction chirurgicale d'une dent permanente incluse (trait. radiculaire, réimplantation, contention) — une dent | — |
| DCH020230 | Idem — deux dents | — |
| DCH020240 | Dégagement chirurgical de la couronne d'une dent permanente incluse | — |
| DCH020250 | Régularisation localisée d'une crête alvéolaire | — |
| DCH020260 | Régularisation étendue de la crête alvéolaire (y compris suture) | — |
| DCH020270 | Régularisation de crête (hémimaxillaire ou canine à canine) | — |
| DCH020280 | Curetage périapical par trépanation vestibulaire, avec/sans résection apicale | — |
| DCH020290 | Exérèse kyste de petit volume par voie alvéolaire élargie | — |
| DCH020300 | Exérèse kyste étendu aux apex de deux dents (trépanation osseuse) | — |
| DCH020310 | Exérèse kyste étendu à un segment important du maxillaire | — |
| DCH020320 | Exérèse kyste corono-dentaire | — |
| DCH020330 | Cure d'un kyste par marsupialisation | — |
| DCH020340 | Chirurgie pré-prothétique — désinsertion musculaire vestibulaire partielle | — |
| DCH020350 | Désinsertion musculaire étendue à tout le vestibule | — |
| DCH020360 | Désinsertion musculaire du plancher de la bouche (section myo-hyoïdiens) | — |
| DCH020370 | Approfondissement d'un vestibule par greffe cutanée | — |

## Section III — Hygiène bucco-dentaire & parodontopathies (`Category: Parodontologie`)
| Code | Désignation | AP |
|------|-------------|----|
| DCH030010 | Détartrage complet sus et sous gingival (quel que soit le nombre de séances) | — |
| DCH030020 | Traitement des gingivites: détartrage, curetage, surfaçage radiculaire (4 séances max) | AP |
| DCH030030 | Gingivectomie partielle | AP |
| DCH030040 | Gingivectomie étendue à une hémi-arcade ou canine à canine | AP |
| DCH030050 | Intervention à lambeaux (curetage, surfaçage, suture) — de 1 à 3 dents | AP |
| DCH030060 | Intervention à lambeaux — par dent supplémentaire | AP |
| DCH030070 | Intervention à lambeau + traitement d'une lésion osseuse par comblement et suture | AP |
| DCH030080 | Greffe gingivale libre (prélèvement + suture) | AP |
| DCH030090 | Hémi-section molaire inférieure / amputation radiculaire molaire supérieure | — |
| DCH030100 | Ligature métallique dans les parodontopathies | — |
| DCH030110 | Attelle métallique dans les parodontopathies | — |
| DCH030120 | Prothèse attelle de contention (quel que soit le nb de dents/crochets) | — |
| DCH030130 | Analyse occlusale avec examen de labo et meulage sélectif | — |
| DCH030140 | Frénectomie (excision du frein labial) | — |

## Section IV — Pédodontie / Prévention (`Category: Pédodontie`)
| Code | Désignation | AP |
|------|-------------|----|
| DCH040010 | Couronne pédodontique préformée | — |
| DCH040020 | Résine de scellement des puits et fissures (sealants) | — |
| DCH040030 | Application topique de fluor par gouttière préfabriquée (5 séances max), par séance | — |
| DCH040040 | Application topique de fluor par gouttière thermoformée | — |
| DCH040050 | Mainteneur d'espace fixe | — |
| DCH040060 | Appareillage fixe pour blocage d'éruption | — |
| DCH040070 | Guide d'éruption | — |
| DCH040080 | Appareil d'interception mobile | — |

## Section V — Orthopédie dento-faciale (ODF) (`Category: Orthopédie dento-faciale`)
| Code | Désignation | AP |
|------|-------------|----|
| DCH050010 | Examen + prise d'empreintes, diagnostic et durée probable du traitement | AP |
| DCH050020 | Analyse céphalométrique (en supplément) | AP |
| DCH050030 | Traitement préventif par dispositif orthopédique | AP |
| DCH050040 | Rééducation neuro-musculaire (série de 12 séances renouvelables), par séance | AP |
| DCH050050 | Traitement simple ne dépassant pas 6 mois | AP |
| DCH050060 | Traitement simple ne dépassant pas 12 mois | AP |
| DCH050070 | Dysmorphoses importantes — première année | AP |
| DCH050080 | Dysmorphoses importantes — deuxième année | AP |
| DCH050090 | Dysmorphoses importantes — troisième année | AP |
| DCH050100 | Contention après traitement orthodontique — première année | AP |
| DCH050110 | Contention après traitement orthodontique — deuxième année | AP |
| DCH050120 | Disjonction intermaxillaire rapide (insuffisance respiratoire confirmée) | AP |
| DCH050130 | Mise en place sur l'arcade d'une dent permanente incluse — une dent | AP |
| DCH050140 | Mise en place — deux dents | AP |
| DCH050150 | Orthopédie des malformations (bec de lièvre / division palatine) — forfait annuel | AP |
| DCH050160 | Orthopédie des malformations — en période d'attente | AP |

## Section VI — Prothèse dentaire (adjointe) (`Category: Prothèse`)
| Code | Désignation | AP |
|------|-------------|----|
| DCH060010 | Prothèse adjointe — appareillage de 1 à 3 dents | AP |
| DCH060020 | Prothèse adjointe — par dent supplémentaire | AP |
| DCH060030 | Appareillage complet haut et bas | AP |
| DCH060040 | Dent prothétique contre-plaquée sur plaque base plastique (supplément) | AP |
| DCH060050 | Plaque base métallique coulée (supplément) | AP |
| DCH060060 | Dent prothétique contreplaquée/massive soudée sur plaque base métallique (supplément) | AP |
| DCH060070 | Réparation de fracture sur plaque base plastique | AP |
| DCH060080 | Dents/crochets ajoutés/remplacés sur appareil plastique — premier élément | AP |
| DCH060090 | Dents/crochets — élément suivant | AP |
| DCH060100 | Dents/crochets soudés, ajoutés/remplacés sur appareil métallique (par élément) | AP |
| DCH060110 | Réparation de fracture de la plaque base métallique | AP |
| DCH060120 | Dents/crochets remontés sur plastique après réparation | AP |
| DCH060130 | Rebasage | AP |
| DCH060140 | Prothèse avec attachement (par élément) | AP |
| DCH060150 | Remplacement de facette ou dent à tube | AP |

> ~100 acts total. `DCH020080` is followed in the source by an un-coded "chacune des suivantes: groupe molaire" line — verify against the official JORT text when adding coefficients. Chapitre VIII (Prothèse restauratrice maxillo-faciale, `MCI…` codes) is maxillo-facial, not cabinet dentistry — excluded.
