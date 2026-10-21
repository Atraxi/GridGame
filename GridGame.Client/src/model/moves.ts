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
	if (player <= 0 || owner === undefined || owner === DEAD || owner === player) {
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

/** What an opponent could do to a tile with a single action. `player` is null when more than one opponent can */
export type OpponentMove = { kind: MoveHint, player: number | null }

/** Every tile some opponent of `me` could spawn or jump onto with their next single action - the same rules as
 * moveHintFor, from each opponent's side. A spawn outranks a jump, since it costs the opponent no tile */
export function opponentMoves(board: number[][], me: number, playerCount: number): (OpponentMove | undefined)[][] {
	const opponents = Array.from({ length: playerCount }, (_, index) => index + 1).filter(player => player !== me)
	return board.map((row, rowIndex) => row.map((_, columnIndex) => {
		let result: OpponentMove | undefined
		for (const opponent of opponents) {
			const hint = moveHintFor(board, opponent, rowIndex, columnIndex)
			if (!hint) {
				continue
			}
			if (!result || (hint === 'spawn' && result.kind === 'jump')) {
				result = { kind: hint, player: opponent }
			} else if (hint === result.kind) {
				result = { kind: hint, player: null }
			}
		}
		return result
	}))
}
