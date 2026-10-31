import type { CSSProperties } from "react"

import { fillFor, STOCK_COLOURS, type PaletteSlot } from "../../model/palette"

export type SeatColour = PaletteSlot
type ChosenColours = { playerNumber: number, status: string, colourPrimary: string | null, colourSecondary: string | null }

export const DEFAULT_COLOURS = STOCK_COLOURS

/** Okabe-Ito: eight colours picked to stay apart for the common kinds of colour blindness. Offered as presets in the
 * account colour picker */
export const COLOUR_BLIND_SAFE = ['#e69f00', '#56b4e9', '#009e73', '#f0e442', '#0072b2', '#d55e00', '#cc79a7', '#000000']

//Two chosen colours closer than this (straight-line distance in RGB, 0-441) are treated as the same colour
const CLASH_DISTANCE = 90

const rgb = (hex: string) => [1, 3, 5].map(start => parseInt(hex.slice(start, start + 2), 16))
const distance = (a: string, b: string) => {
	const [ar, ag, ab] = rgb(a)
	const [br, bg, bb] = rgb(b)
	return Math.hypot(ar - br, ag - bg, ab - bb)
}

/** What colour each seat is drawn in. A player's chosen colour goes with them into whichever seat they take. A seat
 * without one - or any seat, with `useDefaults` - is drawn in the viewer's own default for that seat (`palette`). A
 * chosen colour too close to another seat's is a clash: it is reported in `clashed`, and drawn in the viewer's default
 * instead when they've said to (`switchClashes`) - which hits the later seat, since earlier ones are placed first */
export function resolveSeatColours(seats: ChosenColours[], useDefaults: boolean, palette: PaletteSlot[], switchClashes: boolean | null) {
	const colours: Record<number, SeatColour> = {}
	const clashed: number[] = []
	const standard = (seat: number) => palette[(seat - 1) % palette.length]
	const wants = (seat: ChosenColours) => !useDefaults && seat.status !== 'open' && seat.colourPrimary !== null

	//Seats without a choice are placed first, so a chosen colour is checked against every other seat's real colour
	for (const seat of seats.filter(seat => !wants(seat))) {
		colours[seat.playerNumber] = standard(seat.playerNumber)
	}
	for (const seat of seats.filter(wants)) {
		const taken = Object.values(colours).map(colour => colour.primary)
		const clash = taken.some(other => distance(other, seat.colourPrimary!) < CLASH_DISTANCE)
		if (clash) {
			clashed.push(seat.playerNumber)
		}
		colours[seat.playerNumber] = clash && switchClashes
			? standard(seat.playerNumber)
			: { primary: seat.colourPrimary!, secondary: seat.colourSecondary }
	}
	return { colours, clashed }
}

/** CSS variables that recolour the board and everything keyed off playerColour() for one game */
export function seatColourStyle(colours: Record<number, SeatColour>): CSSProperties {
	const style: Record<string, string> = {}
	for (const [seat, colour] of Object.entries(colours)) {
		const slot = (Number(seat) - 1) % DEFAULT_COLOURS.length + 1
		style[`--player-${slot}`] = colour.primary
		style[`--player-${slot}-fill`] = fillFor(colour)
	}
	return style as CSSProperties
}
