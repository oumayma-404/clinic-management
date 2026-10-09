#!/bin/sh
# Sourced by backup.sh and restore-drill.sh — one copy of the one conversion both need.
#
# The Data Protection ring names its files `key-<guid>.xml`; the key-ring marker (KeyRingGenerationMarker.cs)
# names the same key as Guid.ToByteArray() in lowercase hex (DataProtectionKeyGeneration.IdOf). .NET lays the
# first three groups out little-endian, so 01234567-89ab-cdef-0123-456789abcdef is 67452301ab89efcd0123456789abcdef.

# guid_to_marker_id <guid>  →  prints the marker's id for that key
guid_to_marker_id() {
	G="$(echo "$1" | tr 'A-F' 'a-f')"
	P1="$(echo "${G}" | cut -d- -f1 | sed 's/\(..\)\(..\)\(..\)\(..\)/\4\3\2\1/')"
	P2="$(echo "${G}" | cut -d- -f2 | sed 's/\(..\)\(..\)/\2\1/')"
	P3="$(echo "${G}" | cut -d- -f3 | sed 's/\(..\)\(..\)/\2\1/')"
	P4="$(echo "${G}" | cut -d- -f4)"
	P5="$(echo "${G}" | cut -d- -f5)"
	echo "${P1}${P2}${P3}${P4}${P5}"
}

# ring_holds_key <ring-dir> <marker-id>  →  exit 0 when one of the ring's key files is that key
ring_holds_key() {
	for KEY_FILE in "$1"/key-*.xml; do
		[ -f "${KEY_FILE}" ] || continue
		KEY_NAME="$(basename "${KEY_FILE}" .xml)"
		if [ "$(guid_to_marker_id "${KEY_NAME#key-}")" = "$2" ]; then
			return 0
		fi
	done
	return 1
}
