//Must stay byte-for-byte identical to StateHash.cs on the server: FNV-1a (32-bit) over a fixed sequence of int32s,
//each fed in as 4 little-endian bytes. Every state push carries the server's hash, so the client can tell it missed or
//misapplied one and resync instead of silently drifting
export type HashableState = {
	turnNumber: number,
	currentPlayerNumber: number,
	actionsRemainingInTurn: number,
	isGameOver: boolean,
	gameBoard: number[][],
}

const OFFSET_BASIS = 2166136261
const PRIME = 16777619

function mix(hash: number, value: number): number {
	for (let shift = 0; shift < 32; shift += 8) {
		hash ^= (value >>> shift) & 0xff
		hash = Math.imul(hash, PRIME)
	}
	return hash
}

export function computeStateHash(state: HashableState): number {
	const rows = state.gameBoard.length
	const columns = state.gameBoard[0]?.length ?? 0

	let hash = OFFSET_BASIS
	hash = mix(hash, state.turnNumber)
	hash = mix(hash, state.currentPlayerNumber)
	hash = mix(hash, state.actionsRemainingInTurn)
	hash = mix(hash, state.isGameOver ? 1 : 0)
	hash = mix(hash, rows)
	hash = mix(hash, columns)
	for (const row of state.gameBoard) {
		for (const cell of row) {
			hash = mix(hash, cell)
		}
	}
	//Math.imul works in signed 32-bit; the server's hash is a uint
	return hash >>> 0
}
