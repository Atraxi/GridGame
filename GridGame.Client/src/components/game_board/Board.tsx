import { useId, useMemo, type CSSProperties } from "react";
import { moveHintFor, opponentMoves } from "../../model/moves";
import Tile, { UNCLAIMED, type HistoryMark, type Territory } from "./Tile";
import { CellInsight, type BoardAnalysis } from "../../model/analysis";
import "./Board.css";

export type JumpChoice = { x: number, y: number, origins: { x: number, y: number }[] }
export type MarkedCell = { x: number, y: number, mark: HistoryMark }

/** The parts of a BoardAnalysis the territory overlay draws from */
export type TerritoryMap = Pick<BoardAnalysis, 'insight' | 'claimant'>

/** Only meaningful for unclaimed tiles - owned tiles already show their owner */
function territoryAt(analysis: TerritoryMap, row: number, column: number): Territory | undefined {
	const player = analysis.claimant[row]?.[column] ?? 0
	switch (analysis.insight[row]?.[column]) {
		case CellInsight.UncontestedSecure: return { kind: 'secure', player }
		case CellInsight.UncontestedAtRisk: return { kind: 'at-risk', player }
		case CellInsight.Unreachable: return { kind: 'unreachable' }
		default: return undefined
	}
}

/** Arrows from each candidate jump origin to the landing tile, drawn over the grid in tile units so they follow
 * whatever size the tiles render at. A colour-independent cue: a red highlight on a red tile is easy to miss, an
 * arrow pointing at the tile you clicked isn't */
function JumpArrows({ choice, rows, columns }: { choice: JumpChoice, rows: number, columns: number }) {
	//useId output can contain characters that don't survive inside url(#...)
	const markerId = `jump-arrow-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`
	//Shortened at both ends so the arrow sits between the tiles' centres rather than covering them
	const inset = 0.32
	return (
		<svg className="board__arrows" viewBox={`0 0 ${columns} ${rows}`} preserveAspectRatio="none" aria-hidden="true">
			<defs>
				<marker id={markerId} viewBox="0 0 10 10" refX="5" refY="5" markerWidth="3.2" markerHeight="3.2" orient="auto-start-reverse">
					<path d="M 0 0 L 10 5 L 0 10 z" className="board__arrow-head" />
				</marker>
			</defs>
			{choice.origins.map(origin => {
				const fromX = origin.y + 0.5, fromY = origin.x + 0.5
				const toX = choice.y + 0.5, toY = choice.x + 0.5
				const length = Math.hypot(toX - fromX, toY - fromY)
				const ux = (toX - fromX) / length, uy = (toY - fromY) / length
				const line = {
					x1: fromX + ux * inset, y1: fromY + uy * inset,
					x2: toX - ux * inset, y2: toY - uy * inset,
				}
				return (
					<g key={`${origin.x}.${origin.y}`}>
						<line {...line} className="board__arrow-casing" />
						<line {...line} className="board__arrow-line" markerEnd={`url(#${markerId})`} />
					</g>
				)
			})}
		</svg>
	)
}

export default function Board({ board, playerCount, movesFor, opponentsOf, jumpChoice, analysis, historyMarks, onTileClicked }: {
	board: number[][],
	playerCount: number,
	/** The player whose own legal moves to highlight - only set while it's their turn */
	movesFor: number | null,
	/** Highlights what every other player could do next, from this player's point of view */
	opponentsOf?: number | null,
	jumpChoice: JumpChoice | null,
	/** Shown as a territory overlay on unclaimed tiles when present */
	analysis?: TerritoryMap | null,
	/** Tiles to pick out: while stepping through the move history, or a move waiting to be confirmed */
	historyMarks?: MarkedCell[],
	/** Leave out for a board that's only a picture - its tiles then aren't buttons */
	onTileClicked?: (x: number, y: number) => () => unknown
}) {
	const ownerAt = (row: number, column: number) => board[row]?.[column] ?? UNCLAIMED
	const opponentGrid = useMemo(
		() => opponentsOf != null && opponentsOf > 0 ? opponentMoves(board, opponentsOf, playerCount) : null,
		[board, opponentsOf, playerCount])

	return (
		//The frame is what the tile size is worked out against (see Board.css), and scrolls a board too wide to fit
		<div className="board-frame">
			<div
				className={`board${jumpChoice ? ' board--choosing' : ''}`}
				style={{ '--columns': board[0].length } as CSSProperties}
			>
				{board.map((row, rowIndex) =>
					row.map((owner, columnIndex) => {
						const isJumpOrigin = jumpChoice?.origins.some(origin => origin.x === rowIndex && origin.y === columnIndex) ?? false
						const isJumpLanding = jumpChoice != null && jumpChoice.x === rowIndex && jumpChoice.y === columnIndex
						const moveHint = isJumpOrigin ? 'jump-origin'
							: isJumpLanding ? 'jump-landing'
							: movesFor == null || jumpChoice ? undefined
							: moveHintFor(board, movesFor, rowIndex, columnIndex)
	
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
								moveHint={moveHint}
								territory={analysis ? territoryAt(analysis, rowIndex, columnIndex) : undefined}
								opponentMove={jumpChoice ? undefined : opponentGrid?.[rowIndex][columnIndex]}
								historyMark={historyMarks?.find(cell => cell.x === rowIndex && cell.y === columnIndex)?.mark}
								onTileClicked={onTileClicked?.(rowIndex, columnIndex)}
							/>
						)
					})
				)}
				{jumpChoice && <JumpArrows choice={jumpChoice} rows={board.length} columns={board[0].length} />}
			</div>
		</div>
	)
}
