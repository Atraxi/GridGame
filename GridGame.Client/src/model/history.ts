import type { GridGame } from "./GridGame"
import { UNCLAIMED } from "../components/game_board/Tile"

/** Mirrors GameAction.ToWire on the server - one action in a game's move history */
export type GameAction = {
	turnNumber: number,
	player: number,
	kind: 'spawn' | 'jump' | 'resign',
	/** Where the action landed (unused for a resignation) */
	x: number,
	y: number,
	/** What (x, y) held before the action landed there: unclaimed, or the number of the enemy whose tile it killed */
	before: number,
	originX: number | null,
	originY: number | null,
}

/** GamesController.GetMoveHistory. stateHash identifies the board the actions lead up to */
export type MoveHistory = { stateHash: number, actions: GameAction[] }

type PushedUpdate = { x: number, y: number, newTileValue: number }

/** The action a state push applied, worked out from the board it was applied to. Lets an already-loaded history
 * keep up with live play without fetching it again. Mirrors how GridGameHub.ApplyMove/Resign record actions: a
 * spawn pushes [landing], a jump [origin, landing], and a resignation no tile changes at all */
export function actionFromPush(previous: GridGame, updates: PushedUpdate[], resignedPlayerNumber: number | null): GameAction | null {
	if (updates.length === 0) {
		return resignedPlayerNumber != null && previous.resignedPlayerNumber == null
			? { turnNumber: previous.turnNumber, player: resignedPlayerNumber, kind: 'resign', x: -1, y: -1, before: UNCLAIMED, originX: null, originY: null }
			: null
	}
	const landing = updates[updates.length - 1]
	const origin = updates.length > 1 ? updates[0] : null
	return {
		turnNumber: previous.turnNumber,
		player: previous.currentPlayerNumber,
		kind: origin ? 'jump' : 'spawn',
		x: landing.x,
		y: landing.y,
		before: previous.gameBoard[landing.x]?.[landing.y] ?? UNCLAIMED,
		originX: origin?.x ?? null,
		originY: origin?.y ?? null,
	}
}

/** The board as it stood after the first `position` actions, worked backwards from the current board by undoing
 * each later action in turn. Backwards rather than forwards from the map's starting board, because the map may have
 * been edited since this game was hosted from it */
export function boardAfter(current: number[][], actions: GameAction[], position: number): number[][] {
	const board = current.map(row => row.slice())
	for (let index = actions.length - 1; index >= position; index--) {
		const action = actions[index]
		if (action.kind === 'resign') {
			continue
		}
		board[action.x][action.y] = action.before
		if (action.originX != null && action.originY != null) {
			//A jump always leaves from one of the mover's own tiles
			board[action.originX][action.originY] = action.player
		}
	}
	return board
}
