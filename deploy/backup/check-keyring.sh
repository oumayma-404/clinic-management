#!/bin/sh
# FR-3.9 — refuse a restore whose backup belongs to a key ring this deployment cannot read.
#
#   check-keyring.sh <keyring-stamp-from-the-backup> [<live-marker>]
#
# Without this, restoring a dump against the wrong ring produces a practice whose second factors, reminder
# credentials and calendar tokens are ALL silently undecryptable — discovered days later, when nobody can sign
# in and the working ring has already been overwritten. Nothing about a dump says which ring it belongs to,
# which is why the backup carries a stamp and this compares it.
#
# ⚠️ It compares the backup's ACTIVE generation against every generation the live ring can READ, not against
# the live active one. Those are different questions: the framework rolls keys on its own, so a ring that has
# rolled since the dump was taken is still perfectly able to read it, and an equality check would refuse a
# restore that was never in danger. Refusing correct restores is how a safety check gets switched off.
#
# ⚠️ An `unknown` stamp is REFUSED rather than waved through. It means the API had written no marker when the
# backup ran, so nothing can prove the rings match — and the failure being guarded against is silent, so the
# default has to be the loud one. Override deliberately, having checked by hand, with --i-have-verified.
#
# Exit codes: 0 = the ring can read this backup · 1 = could not run · 2 = mismatch, do NOT restore.
set -eu

STAMP="${1:-}"
MARKER="${2:-/keyring-marker/generation}"

if [ -z "${STAMP}" ]; then
	echo "Usage: check-keyring.sh <keyring-NNN.txt from the backup> [live marker file]" >&2
	exit 1
fi

if [ ! -r "${STAMP}" ]; then
	echo "REFUS : le fichier d'estampille « ${STAMP} » est illisible." >&2
	exit 1
fi

if [ ! -r "${MARKER}" ]; then
	echo "REFUS : le marqueur du trousseau actuel « ${MARKER} » est illisible." >&2
	echo "        Démarrez l'API une fois pour qu'elle l'écrive, puis relancez cette vérification." >&2
	exit 1
fi

BACKUP_ACTIVE="$(grep '^active=' "${STAMP}" | head -n 1 | cut -d= -f2- | tr -d '[:space:]')"
LIVE_ACTIVE="$(grep '^active=' "${MARKER}" | head -n 1 | cut -d= -f2- | tr -d '[:space:]')"

if [ -z "${BACKUP_ACTIVE}" ] || [ "${BACKUP_ACTIVE}" = "unknown" ]; then
	if [ "${3:-}" = "--i-have-verified" ]; then
		echo "AVERTISSEMENT : estampille inconnue, acceptée explicitement par l'opérateur."
		exit 0
	fi
	echo "REFUS : cette sauvegarde ne porte aucune génération de trousseau (« unknown »)." >&2
	echo "        Rien ne permet de prouver que le trousseau actuel sait la relire. Si vous avez vérifié" >&2
	echo "        vous-même, relancez avec --i-have-verified." >&2
	exit 2
fi

if grep -q "^readable=${BACKUP_ACTIVE}$" "${MARKER}" || [ "${BACKUP_ACTIVE}" = "${LIVE_ACTIVE}" ]; then
	echo "OK : le trousseau actuel sait relire cette sauvegarde (génération ${BACKUP_ACTIVE})."
	exit 0
fi

echo "REFUS : générations de trousseau incompatibles." >&2
echo "        Sauvegarde  : ${BACKUP_ACTIVE}" >&2
echo "        Trousseau   : ${LIVE_ACTIVE:-inconnue} (lisibles : $(grep -c '^readable=' "${MARKER}" || echo 0))" >&2
echo "        Restaurer ainsi rendrait ILLISIBLES tous les seconds facteurs, identifiants de rappel et" >&2
echo "        jetons Google des cabinets restaurés — sans erreur visible. Restaurez d'abord le trousseau" >&2
echo "        correspondant (deploy/KEY-CUSTODY.md), puis recommencez." >&2
exit 2
