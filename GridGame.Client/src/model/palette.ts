import { useSyncExternalStore } from "react"
import { authorizedFetch } from "../auth"

export type PaletteSlot = { primary: string, secondary: string | null }

/** The stock palette, one per seat. Must match --player-1..8 in Board.css */
export const STOCK_COLOURS = ['#e05252', '#3d7dd8', '#3fa45b', '#dc9a2e', '#9b5de5', '#16a6a6', '#d64f9a', '#8d6e3f']
const STOCK_PALETTE: PaletteSlot[] = STOCK_COLOURS.map(primary => ({ primary, secondary: null }))

/** A gradient from `secondary` in the middle of each tile out to `primary` at its edges: every edge is then the same
 * plain colour, so neighbouring tiles of one player join up seamlessly whatever the shape of their territory */
export const fillFor = ({ primary, secondary }: PaletteSlot) =>
	secondary ? `radial-gradient(closest-side, ${secondary}, ${primary})` : 'none'

//What this viewer sees each seat in by default, everywhere: kept in a store and written onto :root, where every board,
//swatch, tutorial clip and the home carousel read it from. null is the stock palette
let rebound: PaletteSlot[] | null = null
const listeners = new Set<() => void>()

export const getViewerPalette = () => rebound ?? STOCK_PALETTE

export function setViewerPalette(next: PaletteSlot[] | null) {
	rebound = next
	const root = document.documentElement.style
	STOCK_PALETTE.forEach((_, index) => {
		const slot = next?.[index]
		if (slot) {
			root.setProperty(`--player-${index + 1}`, slot.primary)
			root.setProperty(`--player-${index + 1}-fill`, fillFor(slot))
		} else {
			root.removeProperty(`--player-${index + 1}`)
			root.removeProperty(`--player-${index + 1}-fill`)
		}
	})
	listeners.forEach(listener => listener())
}

export const useViewerPalette = () => useSyncExternalStore(
	listener => { listeners.add(listener); return () => { listeners.delete(listener) } },
	getViewerPalette,
)

/** Fetches the signed-in account's rebound palette. Run whenever the account changes */
export async function loadViewerPalette() {
	try {
		const response = await authorizedFetch('/Users/Me')
		setViewerPalette(response.ok ? (await response.json()).defaultPalette ?? null : null)
	} catch {
		//Keeps whatever palette was showing - the stock one is a fine default
	}
}
