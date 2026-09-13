"use client"

import { useEffect, useState } from "react"

import { pickJoke, type JokeSurface } from "@/lib/jokes"

/**
 * La phrase du jour d'un écran vide — passée au `joke` d'un {@link EmptyState}, jamais à son `description`.
 *
 * <p>Elle se place <b>entre le titre et la description</b> et se lit plus grand que les deux : mise en bas et en
 * gris clair, elle était manquée par tout le monde, ce qui est la seule façon de rater complètement le but. Ce
 * n'est pour autant jamais la seule ligne — l'écran continue de dire ce qui manque et quoi faire juste dessous,
 * et rien de ce que le lecteur doit savoir ne passe par ici.</p>
 *
 * <p>⚠️ <b>Le tirage a lieu après le montage</b>, d'où l'état à `null` au premier rendu. La page est prérendue au
 * build : une phrase tirée sur la date du jour y serait figée sur le jour du build et différerait de celle que le
 * navigateur calcule, ce que React 19 traite en erreur d'hydratation. En pratique rien ne clignote — un état vide
 * n'apparaît qu'une fois la lecture revenue, donc bien après le montage.</p>
 */
export function EmptyJoke({ surface }: { surface: JokeSurface }) {
  const [line, setLine] = useState<string | null>(null)

  useEffect(() => setLine(pickJoke(surface)), [surface])

  if (!line) return null

  return <p className="text-base font-semibold text-foreground sm:text-lg">{line}</p>
}
