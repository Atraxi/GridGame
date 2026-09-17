import type { CSSProperties } from "react";
import type { GridGame } from "../../model/GridGame";
import { moveHintFor } from "../../model/moves";
import Tile, { UNCLAIMED } from "./Tile";
import "./Board.css";

type JumpChoice = { x: number, y: number, origins: { x: number, y: number }[] }

export default function Board({ gameState, myPlayerNumber, jumpChoice, onTileClicked }: {
	gameState: GridGame,
	myPlayerNumber: number | null,
	jumpChoice: JumpChoice | null,
	onTileClicked: (x: number, y: number) => () => Promise<any> | undefined
}) {
	const board = gameState.gameBoard
	const ownerAt = (row: number, column: number) => board[row]?.[column] ?? UNCLAIMED

	return (
		<div className="board" style={{ '--columns': board[0].length } as CSSProperties}>
			{board.map((row, rowIndex) =>
				row.map((owner, columnIndex) => {
					const isJumpOrigin = jumpChoice?.origins.some(origin => origin.x === rowIndex && origin.y === columnIndex) ?? false
					const moveHint = myPlayerNumber == null || jumpChoice
						? undefined
						: moveHintFor(board, myPlayerNumber, rowIndex, columnIndex)

					return (
						<Tile
							key={`${rowIndex}.${columnIndex}`}
							owner={owner}
							//A blob is outlined only where it meets a different owner, so each tile contributes just its own share of that boundary
							edges={{
								top: ownerAt(rowIndex - 1, columnIndex) !== owner,
								right: ownerAt(rowIndex, columnIndex + 1) !== owner,
								bottom: ownerAt(rowIndex + 1, columnIndex) !== owner,
								left: ownerAt(rowIndex, columnIndex - 1) !== owner,
							}}
							moveHint={isJumpOrigin ? 'jump-origin' : moveHint}
							onTileClicked={onTileClicked(rowIndex, columnIndex)}
						/>
					)
				})
			)}
		</div>
	)
}
