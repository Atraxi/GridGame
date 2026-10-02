import type { Player } from "./Player"

export type GridGame = {
	id: number
	creationDate: string
	name: string
	turnNumber: number,
	playerCount: number,
	actionsPerTurn: number,
	currentPlayerNumber: number,
	actionsRemainingInTurn: number,
	players: Player[],
	gameBoard: number[][],
	isGameOver: boolean,
	resignedPlayerNumber?: number | null,
}