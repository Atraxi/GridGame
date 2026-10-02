//Mirrors BoardAnalysis.cs - see there for exactly how each category is derived

/** Values of BoardAnalysis.insight[x][y] (CellInsight on the server) */
export const CellInsight = {
	None: 0,
	OwnedUntouchable: 1,
	OwnedThreatened: 2,
	Contested: 3,
	Unreachable: 4,
	UncontestedSecure: 5,
	UncontestedAtRisk: 6,
} as const

export type VictoryStatus = 'None' | 'Leading' | 'Decided' | 'Locked'

export type PlayerAnalysis = {
	playerNumber: number,
	isActive: boolean,
	owned: number,
	ownedUntouchable: number,
	ownedThreatened: number,
	uncontestedSecure: number,
	uncontestedAtRisk: number,
	contestedReachable: number,
	floor: number,
	projected: number,
	ceiling: number,
	status: VictoryStatus,
}

export type BoardAnalysis = {
	insight: number[][],
	/** For uncontested cells, the only player able to reach them; for owned cells, the owner; else 0 */
	claimant: number[][],
	players: PlayerAnalysis[],
	totalCells: number,
	deadCells: number,
	unclaimedCells: number,
	contestedCells: number,
	unreachableCells: number,
	settledCells: number,
	informallyDecidedFor: number | null,
}

export type FinalResults = {
	scores: number[],
	resignedPlayerNumber: number | null,
	winners: number[],
	analysis: BoardAnalysis,
}
