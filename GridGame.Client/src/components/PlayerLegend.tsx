import { displayName } from "../model/Player"
import { playerColour } from "./game_board/playerColour"
import "./PlayerLegend.css"

/** GridGameHub's presence entry for one seat */
export type PresenceEntry = {
	playerNumber: number,
	userName: string | null,
	/** 'away' is a locked seat whose holder hasn't been in since the game was last reopened */
	status: 'connected' | 'unresponsive' | 'disconnected' | 'away' | 'open',
	/** Held for good by its owner's account (see GridGameHub.LockSeat) */
	locked: boolean,
}

const STATUS_LABELS: Record<PresenceEntry['status'], string> = {
	connected: '',
	unresponsive: 'not responding',
	disconnected: 'disconnected',
	away: 'away',
	open: 'open seat',
}

/** Which colour is whose: one chip per seat, with who holds it and whether they're here */
export default function PlayerLegend({ presence, myPlayerNumber, currentPlayerNumber, resignedPlayerNumber, isGameOver }: {
	presence: PresenceEntry[],
	myPlayerNumber: number | null,
	currentPlayerNumber: number,
	resignedPlayerNumber: number | null | undefined,
	isGameOver: boolean,
}) {
	if (presence.length === 0) {
		return null
	}
	return (
		<ul className="player-legend" aria-label="Players">
			{presence.map(entry => {
				const isCurrent = !isGameOver && entry.playerNumber === currentPlayerNumber
				const name = displayName(entry.userName)
				const notes = [
					entry.playerNumber === resignedPlayerNumber ? 'resigned' : null,
					//A finished game hands out no new seats, so an empty one there is just nobody, not an invitation
					isGameOver && entry.status === 'open' ? null : STATUS_LABELS[entry.status],
					entry.locked ? 'seat locked' : null,
				].filter(Boolean)
				return (
					<li
						key={entry.playerNumber}
						className={`player-legend__seat${isCurrent ? ' player-legend__seat--current' : ''}${entry.status === 'open' ? ' player-legend__seat--open' : ''}`}
						aria-current={isCurrent ? 'step' : undefined}
					>
						<span className="player-legend__swatch" style={{ background: playerColour(entry.playerNumber) }} />
						<span className="player-legend__text">
							<span className="player-legend__name">
								P{entry.playerNumber}{name && ` ${name}`}
								{entry.playerNumber === myPlayerNumber && ' (you)'}
							</span>
							{(isCurrent || notes.length > 0) && (
								<span className="player-legend__notes">
									{[isCurrent ? 'to move' : null, ...notes].filter(Boolean).join(' · ')}
								</span>
							)}
						</span>
					</li>
				)
			})}
		</ul>
	)
}
