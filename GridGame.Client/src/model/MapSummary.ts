export type MapSummary = {
	id: number
	name: string
	creationDate: string
	playerCount: number
	actionsPerTurn: number
	designedByUserName: string
	isDraft: boolean
}

/** MapsController.Get - a whole map, board included, for the editor */
export type MapDetails = {
	id: number
	name: string
	playerCount: number
	actionsPerTurn: number
	startingBoard: number[][]
	isDraft: boolean
	designedByUserName: string | null
	isMine: boolean
}
