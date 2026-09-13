import { todayLocalIso } from "@/lib/format"

/**
 * Les petites phrases des écrans vides — un seul propriétaire.
 *
 * <p>Un écran vide est le seul moment où personne n'est au milieu de quelque chose : rien n'est en jeu, et le
 * logiciel n'a rien d'utile à dire. C'est la seule place du produit où une plaisanterie ne coûte rien, et
 * `ui/empty-state.tsx` dit déjà pourquoi elle rapporte — un vide est la <b>première</b> expérience de chaque écran
 * dans un cabinet fraîchement installé.</p>
 *
 * <h4>Les quatre règles, dans l'ordre d'importance</h4>
 * <ol>
 *   <li><b>Une vraie expression française, ou rien.</b> « Nickel chrome », « la journée est pliée », « pas de
 *       nouvelles, bonnes nouvelles », « se tourner les pouces », « rester en plan », « parole d'arracheur de
 *       dents », « rien à se mettre sous la dent », « tu peux te brosser », « une santé de fer », « pas un chat »,
 *       « du pain sur la planche », « se la couler douce », « calme plat », « boucher un trou ». Une phrase
 *       grammaticalement correcte mais que personne ne dit — « Tout est renseigné », « Rien en suspens » — est du
 *       français d'école : correcte, et plate. C'est le piège dans lequel la première version est tombée.</li>
 *   <li><b>La phrase n'est jamais la seule ligne.</b> L'état vide continue de dire ce qui manque et quoi faire ;
 *       la plaisanterie s'ajoute au-dessus. Aucune information ne dépend d'elle.</li>
 *   <li><b>Vide ne veut pas dire « bonne nouvelle » partout.</b> Un agenda sans rendez-vous, c'est du chiffre
 *       d'affaires en moins — donc la phrase y célèbre la journée qu'on récupère, jamais le vide lui-même. Là où
 *       le vide est vraiment une bonne nouvelle (plus rien à clôturer, rien au labo, aucune alerte), elle peut
 *       être franchement gaie.</li>
 *   <li><b>Jamais sur l'argent, le dossier clinique, une erreur, un refus ou un chargement.</b> Une facture, une
 *       ordonnance, un 409 et un écran qui charge ne se prêtent à aucune plaisanterie : la crédibilité du produit
 *       s'y joue en entier. Ces surfaces n'ont volontairement pas de clé ici.</li>
 * </ol>
 *
 * <p>⚠️ <b>Le tirage est figé sur la journée locale</b> (voir {@link pickJoke}), jamais sur le rendu. Une phrase
 * qui change à chaque re-rendu clignote — un refetch, une poussée temps réel — et rien n'attire l'œil comme un
 * texte qui bouge : sur « À clôturer », ouvert tous les soirs, ce serait une nuisance quotidienne. Une phrase par
 * écran et par jour se lit comme une phrase écrite, pas comme une machine à sous.</p>
 */
export type JokeSurface =
  | "visitClosure"
  | "pendingReview"
  | "unfinishedActs"
  | "notifications"
  | "agendaDay"
  | "treatments"
  | "labOrders"
  | "stock"
  | "waitingList"
  | "journal"
  | "patientFiles"
  | "firstPatients"
  | "firstProcedures"
  | "firstMedications"
  | "firstSuppliers"

export const EMPTY_JOKES: Record<JokeSurface, readonly string[]> = {
  // Le vide est ici une vraie bonne nouvelle : la journée est finie et rien ne traîne.
  visitClosure: [
    "Rideau ! 🎭 Plus une séance à clôturer.",
    "La journée est pliée 😮‍💨",
    "Nickel chrome ✨ Ni tartre, ni paperasse.",
    "Pas de pain sur la planche 🥖 Tout est fait.",
    "Pour une fois, on se tourne les pouces 👐",
    "Zéro dossier en retard 🏆 Même la paperasse a capitulé.",
    "Vous pouvez dormir sur vos deux oreilles 😴",
    "C'est dans la poche 👌 Aucune séance en attente.",
    "Journée bouclée ☕ Le café est mérité.",
    "Rien ne traîne 🧹 Pas même un plateau.",
  ],

  pendingReview: [
    "Aucun trou à boucher 🦷 Toutes les fiches sont complètes.",
    "Pas une dent qui manque au dossier 👌",
    "Rien à compléter ✍️ Même les cases qu'on oublie toujours.",
    "Vos dossiers sont irréprochables 🏆",
    "Pas une fiche à finir ☕ Profitez-en.",
  ],

  unfinishedActs: [
    "Rien ne reste en plan 🎯",
    "Personne n'est laissé le bec dans l'eau 🦆",
    "Pas un acte à moitié fait 🏁",
    "Tout est allé jusqu'au bout 💪",
    "Aucune suite à donner 😌 Le carnet est clair.",
  ],

  notifications: [
    "Pas de nouvelles, bonnes nouvelles 📭",
    "Silence radio 📻 Savourez.",
    "Rien à signaler ✨ Pas l'ombre d'une carie.",
    "Vous êtes à jour 🔔 Aucune piqûre de rappel en vue.",
    "Calme plat 😌 Même la petite souris n'a rien dit.",
    "Zéro alerte 🟢 Tout roule.",
    "Boîte vide, esprit tranquille 🧘",
    "Personne ne vous réclame 🤫",
    "On vous laisse tranquille 🙂 Pour l'instant.",
    "Rien de neuf 🌤️ Et c'est très bien comme ça.",
  ],

  // ⚠️ Un agenda vide n'est PAS une bonne nouvelle pour un cabinet : la phrase porte sur la journée gagnée.
  agendaDay: [
    "Pour une fois, c'est vous qui gardez le sourire 😁",
    "Pas un chat 🐈 Pas une molaire non plus.",
    "Aujourd'hui, on se la coule douce 🌴",
    "Même la petite souris a posé un congé ☕",
    "Le fauteuil se repose aussi 😌",
    "Calme plat ⛱️ Ça arrive, et ça fait du bien.",
    "Respirez, vous l'avez mérité 🧘",
    "L'occasion de ranger le cabinet 📚",
  ],

  // Même précaution : aucun traitement en cours, c'est aussi du travail en moins. On félicite les patients.
  treatments: [
    "Vos patients ont une santé de fer 💪",
    "Vos patients se brossent bien 🪥",
    "Personne n'a mal nulle part 😁 Rare, mais ça arrive.",
    "Que des dents en pleine forme ✨",
    "Le champ est libre 🌿",
  ],

  labOrders: [
    "Pour une fois, ce n'est pas vous qui attendez ⏳",
    "Le labo est à jour 🎉 Ça méritait d'être noté.",
    "Aucune couronne en transit 📦",
    "Rien à récupérer 🚚 Tout est arrivé.",
    "Le labo souffle un peu 😌",
  ],

  // ⚠️ Cet écran dit « aucun article ENREGISTRÉ », pas « rien à commander » : le placard est vide dans l'app,
  // pas dans le cabinet. Une phrase sur les stocks bien remplis y serait un contresens.
  stock: [
    "Le placard est vide 🧤 On commence par les gants.",
    "L'inventaire part de zéro 📦",
    "Pas même une compresse à compter 🩹",
    "L'armoire attend d'être garnie 🧴",
  ],

  waitingList: [
    "Personne n'attend 📖 Les magazines peuvent vieillir tranquilles.",
    "Salle d'attente déserte 🪑 Pas un chat.",
    "Tout le monde passe à l'heure ⏱️ Si, si.",
    "La file est vide 🙌",
    "Personne sur la liste 😌 Ça roule.",
  ],

  journal: [
    "Personne n'a touché à rien 🤥 Parole d'arracheur de dents.",
    "Circulez, il n'y a rien à voir 🚧",
    "Aucune trace, aucun soupçon 🕵️",
    "Journal vierge 📖",
  ],

  patientFiles: [
    "Rien à se mettre sous la dent 🗂️",
    "Pas même une radio floue 🖼️",
    "Le tiroir est encore neuf 📁",
    "Dossier vierge 📎 Ça ne saurait tarder.",
  ],

  // Premier lancement — la seule fois où ces écrans seront vus vides. On accueille, on ne constate pas.
  firstPatients: [
    "Votre tout premier patient vous attend 🚀",
    "Le cabinet ouvre ses portes 🎉 À vous de jouer.",
    "Page blanche, sourire neuf ✨",
    "C'est ici que tout commence 🦷",
  ],

  firstProcedures: [
    "Commencez par le détartrage 🪥 C'est le pain quotidien.",
    "Un catalogue tout neuf ✨ À vous de fixer vos tarifs.",
    "Vos actes, vos prix, vos règles 💼",
  ],

  firstMedications: [
    "Même pas un antalgique pour la forme 💊",
    "L'armoire à pharmacie est vide 🧴",
    "Votre première ordonnance commence ici ✍️",
  ],

  firstSuppliers: [
    "Le labo attend votre appel 📞",
    "Le premier bon de commande n'attend que vous 🚚",
    "On commence par le plus fidèle 📇",
  ],
}

/**
 * La phrase du jour pour un écran, tirée de façon stable.
 *
 * <p>Déterministe à partir de `seed`, que l'appelant fabrique avec la <b>journée locale tunisienne</b> — jamais
 * `Math.random()`, qui rendrait une phrase différente à chaque rendu, ni `new Date()` directement, qui décale d'un
 * jour pendant la première heure de chaque journée tunisienne (`todayLocalIso` existe pour ça).</p>
 */
export function pickJoke(surface: JokeSurface, seed: string = todayLocalIso()): string {
  const lines = EMPTY_JOKES[surface]
  let hash = 5381
  const material = `${surface}:${seed}`
  for (let i = 0; i < material.length; i += 1) hash = ((hash * 33) ^ material.charCodeAt(i)) >>> 0
  return lines[hash % lines.length]
}
