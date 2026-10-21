import { useEffect } from "react"
import type { GameAction } from "../model/history"
import { UNCLAIMED } from "./game_board/Tile"
import "./MoveHistory.css"

const PALETTE_SIZE = 6

//Non-breaking spaces keep a coordinate on one line when a narrow screen wraps the description
const NBSP = String.fromCharCode(160)
const cell = (x: number, y: number) => `row${NBSP}${x + 1},${NBSP}col${NBSP}${y + 1}`

function describe(action: GameAction) {
	if (action.kind === 'resign') {
		return 'resigned'
	}
	const kill = action.before > UNCLAIMED ? ` - killed player ${action.before}'s tile` : ''
	return action.kind === 'jump' && action.originX != null && action.originY != null
		? `jumped ${cell(action.originX, action.originY)} → ${cell(action.x, action.y)}${kill}`
		: `spawned at ${cell(action.x, action.y)}${kill}`
}

function Swatch({ player }: { player: number }) {
	return <span className="move-history__swatch" style={{ background: `var(--player-${((player - 1) % PALETTE_SIZE) + 1})` }} />
}

/**
 * The game's move history, newest first, with controls to step the board back through it.
 *
 * `position` is how many actions the board being shown has had applied: actions.length is the board as it stands,
 * 0 the board before the first recorded action, and null means following live play.
 */
export default function MoveHistory({ actions, position, onPositionChange, currentTurnNumber }: {
	actions: GameAction[],
	position: number | null,
	onPositionChange: (position: number | null) => void,
	/** The game's current turn - if there's no history at all, play may still have started before it was recorded */
	currentTurnNumber: number,
}) {
	const total = actions.length
	const viewing = position != null
	//Stepping back from live play first lands on the latest action, so every action gets its turn being highlighted
	const step = (delta: number) => {
		const from = position ?? total + (delta < 0 ? 1 : 0)
		const next = Math.min(Math.max(from + delta, 0), total)
		onPositionChange(next)
	}
	const shown = position != null && position > 0 ? actions[position - 1] : null

	//Arrow keys step through the history once it's being replayed - not before, so they keep their usual job of
	//scrolling the page otherwise
	useEffect(() => {
		if (position == null) {
			return
		}
		const onKeyDown = (event: KeyboardEvent) => {
			if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) {
				return
			}
			const delta = event.key === 'ArrowLeft' ? -1 : event.key === 'ArrowRight' ? 1 : 0
			if (delta !== 0) {
				event.preventDefault()
				onPositionChange(Math.min(Math.max(position + delta, 0), total))
			} else if (event.key === 'Escape') {
				onPositionChange(null)
			}
		}
		window.addEventListener('keydown', onKeyDown)
		return () => window.removeEventListener('keydown', onKeyDown)
	}, [position, total, onPositionChange])
	//Games already under way when history recording was added only have the actions taken since
	const startsLate = (actions[0]?.turnNumber ?? currentTurnNumber) > 0

	return (
		<section className="move-history">
			<div className="move-history__controls" role="group" aria-label="Step through the move history">
				<button type="button" onClick={() => onPositionChange(0)} disabled={total === 0 || position === 0} title="First action">⏮</button>
				<button type="button" onClick={() => step(-1)} disabled={total === 0 || position === 0} title="Back one action">◀ Back</button>
				<button type="button" onClick={() => step(1)} disabled={!viewing || position === total} title="Forward one action">Forward ▶</button>
				<button type="button" onClick={() => onPositionChange(null)} disabled={!viewing} title="Back to the live board">Live ⏭</button>
			</div>
			<p className="move-history__position" aria-live="polite">
				{!viewing
					? `${total} action${total === 1 ? '' : 's'} so far. Step back, or pick one below, to replay them on the board (arrow keys work too).`
					: position === 0
						? 'The board before the first recorded action.'
						: <>After action {position} of {total}: turn {shown!.turnNumber}, <Swatch player={shown!.player} />player {shown!.player} {describe(shown!)}.</>}
			</p>
			{startsLate && (
				<p className="move-history__note">Moves made before this game's history started being recorded aren't shown.</p>
			)}
			{total > 0 && (
				<ol className="move-history__list" reversed>
					{actions.map((action, index) => ({ action, index })).reverse().map(({ action, index }) => (
						<li key={index}>
							<button
								type="button"
								className={`move-history__item${position === index + 1 ? ' move-history__item--current' : ''}`}
								onClick={() => onPositionChange(index + 1)}
							>
								<span className="move-history__turn">Turn {action.turnNumber}</span>
								<span className="move-history__text"><Swatch player={action.player} />Player {action.player} {describe(action)}</span>
							</button>
						</li>
					))}
				</ol>
			)}
		</section>
	)
}
