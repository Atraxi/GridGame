import type { CSSProperties } from "react";
import type { GridGame } from "../../model/GridGame";
import { moveHintFor } from "../../model/moves";
import Tile, { UNCLAIMED, type Territory } from "./Tile";
import { CellInsight, type BoardAnalysis } from "../../model/analysis";
import "./Board.css";

type JumpChoice = { x: number, y: number, origins: { x: number, y: number }[] }

/** Only meaningful for unclaimed tiles - owned tiles already show their owner */
function territoryAt(analysis: BoardAnalysis, row: number, column: number): Territory | undefined {
	const player = analysis.claimant[row]?.[column] ?? 0
	switch (analysis.insight[row]?.[column]) {
		case CellInsight.UncontestedSecure: return { kind: 'secure', player }
		case CellInsight.UncontestedAtRisk: return { kind: 'at-risk', player }
		case CellInsight.Unreachable: return { kind: 'unreachable' }
		default: return undefined
	}
}

export default function Board({ gameState, myPlayerNumber, jumpChoice, analysis, onTileClicked }: {
	gameState: GridGame,
	myPlayerNumber: number | null,
	jumpChoice: JumpChoice | null,
	/** Shown as a territory overlay on unclaimed tiles when present */
	analysis?: BoardAnalysis | null,
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
							territory={analysis ? territoryAt(analysis, rowIndex, columnIndex) : undefined}
							onTileClicked={onTileClicked(rowIndex, columnIndex)}
						/>
					)
				})
			)}
		</div>
	)
}
