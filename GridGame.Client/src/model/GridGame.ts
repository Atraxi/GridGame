import type { Player } from "./Player"

export type GridGame = {
	id: number
	creationDate: string
	name: string
	turnNumber: number,
	players: Player[],
	gameBoard: number[][],
}