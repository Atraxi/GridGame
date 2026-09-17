import { DEAD } from "../components/game_board/Tile"

export type MoveHint = 'spawn' | 'jump'

//Orthogonal and diagonal: the 8 directions both move types travel along
const DIRECTIONS: [number, number][] = [
	[-1, -1], [-1, 0], [-1, 1],
	[0, -1],           [0, 1],
	[1, -1],  [1, 0],  [1, 1],
]

const inBounds = (board: number[][], row: number, column: number) =>
	row >= 0 && row < board.length && column >= 0 && column < (board[row]?.length ?? 0)

//Mirrors GridGameMoves.cs so the client can preview legal moves before asking the server to apply one
export function moveHintFor(board: number[][], player: number, row: number, column: number): MoveHint | undefined {
	const owner = board[row]?.[column]
	if (owner === undefined || owner === DEAD || owner === player) {
		return undefined
	}

	if (DIRECTIONS.some(([dr, dc]) => inBounds(board, row + dr, column + dc) && board[row + dr][column + dc] === player)) {
		return 'spawn'
	}
	if (DIRECTIONS.some(([dr, dc]) => inBounds(board, row + dr * 2, column + dc * 2) && board[row + dr * 2][column + dc * 2] === player)) {
		return 'jump'
	}
	return undefined
}
