import { useEffect, useState } from "react"

/** GameClock.Wire on the server. Times are as of the moment the push was sent */
export type Clock = {
	mode: 'Bank' | 'PerTurn' | 'PerTurnSkip',
	baseMs: number,
	incrementMs: number,
	/** Bank mode: each player's bank as of the start of the current turn (index 0 = player 1) */
	remainingMs: number[] | null,
	/** How far into the current turn the server was; null until the first turn has ended (the opening turn is untimed) */
	elapsedMs: number | null,
}

/** A clock together with when this client got it, which is what its elapsed time counts on from */
export type ClockReading = { clock: Clock, receivedAt: number }

export const formatMs = (ms: number) => {
	const seconds = Math.max(Math.ceil(ms / 1000), 0)
	return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`
}

/** Time left per player right now, re-evaluated four times a second while a clock is running. A player not on the move
 * keeps what they had; null means nothing to show for them (per-turn mode only times whoever is to move) */
export function useTimeLeft(reading: ClockReading | null, currentPlayer: number, running: boolean, playerCount: number) {
	const [now, setNow] = useState(() => performance.now())
	const ticking = running && reading?.clock.elapsedMs != null
	useEffect(() => {
		if (!ticking) {
			return
		}
		const timer = setInterval(() => setNow(performance.now()), 250)
		return () => clearInterval(timer)
	}, [ticking])

	if (!reading) {
		return null
	}
	const { clock, receivedAt } = reading
	const elapsed = clock.elapsedMs == null ? null : clock.elapsedMs + (ticking ? Math.max(now - receivedAt, 0) : 0)
	return Array.from({ length: playerCount }, (_, index) => {
		const player = index + 1
		const isCurrent = player === currentPlayer
		if (clock.mode !== 'Bank') {
			return isCurrent ? clock.baseMs - (elapsed ?? 0) : null
		}
		return (clock.remainingMs?.[index] ?? clock.baseMs) - (isCurrent ? elapsed ?? 0 : 0)
	})
}
