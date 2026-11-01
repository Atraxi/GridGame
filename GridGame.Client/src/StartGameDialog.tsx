import { useEffect, useState } from "react"
import { Link, useNavigate } from "react-router"
import { authorizedFetch } from "./auth"
import type { MapSummary } from "./model/MapSummary"
import type { PastOpponent } from "./model/PastOpponent"
import "./StartGameDialog.css"

type TimerChoice = 'none' | 'speed' | 'bank' | 'perTurn' | 'perTurnSkip'

export default function StartGameDialog({ onClose }: { onClose: () => void }) {
	const navigate = useNavigate()
	const [maps, setMaps] = useState<MapSummary[] | null>(null)
	const [selectedMapId, setSelectedMapId] = useState<number | null>(null)
	const [name, setName] = useState('')
	const [error, setError] = useState<string | null>(null)
	const [starting, setStarting] = useState(false)
	const [inviteOnly, setInviteOnly] = useState(false)
	const [opponents, setOpponents] = useState<PastOpponent[] | null>(null)
	const [invited, setInvited] = useState<Set<string>>(new Set())
	const [timer, setTimer] = useState<TimerChoice>('none')
	const [bankMinutes, setBankMinutes] = useState(5)
	const [incrementSeconds, setIncrementSeconds] = useState(3)
	const [turnSeconds, setTurnSeconds] = useState(30)
	const [bots, setBots] = useState<string[]>([])
	const [seatBots, setSeatBots] = useState<Record<number, string>>({}) //seat index -> bot name; absent = human

	//Speed chess is just a preset of the chess clock: a short bank with a little added back each turn
	const clockSettings = () => {
		switch (timer) {
			case 'speed': return { clockMode: 'Bank', clockBaseSeconds: 180, clockIncrementSeconds: 2 }
			case 'bank': return { clockMode: 'Bank', clockBaseSeconds: Math.round(bankMinutes * 60), clockIncrementSeconds: incrementSeconds }
			case 'perTurn': return { clockMode: 'PerTurn', clockBaseSeconds: turnSeconds, clockIncrementSeconds: 0 }
			case 'perTurnSkip': return { clockMode: 'PerTurnSkip', clockBaseSeconds: turnSeconds, clockIncrementSeconds: 0 }
			default: return { clockMode: 'None', clockBaseSeconds: 0, clockIncrementSeconds: 0 }
		}
	}

	useEffect(() => {
		authorizedFetch('/Users/Opponents')
			.then(response => response.ok ? response.json() : [])
			.then((data: PastOpponent[]) => setOpponents(data))
			.catch(() => setOpponents([]))
	}, [])

	const toggleInvited = (id: string, checked: boolean) => {
		setInvited(current => {
			const next = new Set(current)
			if (checked) {
				next.add(id)
			} else {
				next.delete(id)
			}
			return next
		})
	}

	useEffect(() => {
		authorizedFetch('/Games/GetBots')
			.then(response => response.ok ? response.json() : [])
			.then((data: string[]) => setBots(data))
			.catch(() => setBots([]))
	}, [])

	useEffect(() => {
		authorizedFetch('/Maps/GetSummaries?page=1')
			.then(response => response.json())
			.then((data: MapSummary[]) => {
				setMaps(data)
				if (data.length > 0) {
					setSelectedMapId(data[0].id)
					setName(data[0].name)
				}
			})
			.catch(err => setError(err.message))
	}, [])

	const selectMap = (mapId: number) => {
		setSelectedMapId(mapId)
		const map = maps?.find(candidate => candidate.id === mapId)
		if (map) {
			setName(map.name)
		}
	}

	const seatCount = maps?.find(map => map.id === selectedMapId)?.playerCount ?? 0
	const botNames = Array.from({ length: seatCount }, (_, seat) => seatBots[seat] || null)
	const hasBots = botNames.some(name => name !== null)

	const submit = () => {
		if (selectedMapId == null) {
			return
		}
		setStarting(true)
		setError(null)
		authorizedFetch('/Games/CreateFromMap', {
			method: 'POST',
			body: JSON.stringify({ mapId: selectedMapId, name, invitedPlayerIds: inviteOnly ? [...invited] : [], botNames: hasBots ? botNames : null, ...clockSettings() }),
		})
			.then(async response => {
				if (!response.ok) {
					throw new Error(await response.text())
				}
				return response.json()
			})
			.then((gameId: number) => navigate(`/gridgame/${gameId}`))
			.catch(err => { setError(err.message); setStarting(false) })
	}

	return (
		<div className="dialog-overlay" onClick={onClose}>
			<div className="dialog" role="dialog" aria-modal="true" aria-labelledby="start-game-title" onClick={event => event.stopPropagation()}>
				<h2 id="start-game-title">Start a new game</h2>

				{maps === null && !error && <p>Loading maps...</p>}
				{maps?.length === 0 && <p>No maps yet - <Link to="/maps/create" onClick={onClose}>create one</Link>.</p>}

				{maps && maps.length > 0 && <>
					<label className="dialog__field">
						Map
						<select value={selectedMapId ?? undefined} onChange={event => selectMap(Number(event.target.value))}>
							{maps.map(map => (
								<option key={map.id} value={map.id}>
									{map.name} ({map.playerCount} players, by {map.designedByUserName})
								</option>
							))}
						</select>
					</label>
					<p className="dialog__hint">
						
						<Link to={`/maps/create?from=${selectedMapId}`} onClick={onClose}>Edit a copy of this map</Link>
					</p>
					<label className="dialog__field">
						Game name
						<input value={name} onChange={event => setName(event.target.value)} />
					</label>
					{bots.length > 0 && seatCount > 0 && <fieldset className="dialog__fieldset">
							<legend>Seats</legend>
							{botNames.map((name, seat) => (
								<label key={seat} className="dialog__field">
									Player {seat + 1}
									<select value={name ?? ''} onChange={event => setSeatBots(current => ({ ...current, [seat]: event.target.value }))}>
										<option value="">Human</option>
										{bots.map(bot => <option key={bot} value={bot}>{bot} (bot)</option>)}
									</select>
								</label>
							))}
						</fieldset>}
					<fieldset className="dialog__fieldset">
						<legend>Who can take a seat</legend>
						<label className="dialog__choice">
							<input type="radio" name="seating" checked={!inviteOnly} onChange={() => setInviteOnly(false)} />
							Anyone
						</label>
						<label className="dialog__choice">
							<input type="radio" name="seating" checked={inviteOnly} onChange={() => setInviteOnly(true)} />
							Only players I pick
						</label>
						{inviteOnly && (opponents === null
							? <p className="dialog__hint">Loading...</p>
							: opponents.length === 0
								? <p className="dialog__hint">
									No past opponents to invite yet.
								</p>
								: <div className="dialog__invitees">
									{opponents.map(opponent => (
										<label key={opponent.id} className="dialog__choice">
											<input
												type="checkbox"
												checked={invited.has(opponent.id)}
												onChange={event => toggleInvited(opponent.id, event.target.checked)}
											/>
											{opponent.userName}
										</label>
									))}
								</div>)}
						{inviteOnly && <p className="dialog__hint">Others can still watch.</p>}
					</fieldset>
					<fieldset className="dialog__fieldset">
				<legend>Timer</legend>
				<label className="dialog__field">
					<select value={timer} onChange={event => setTimer(event.target.value as TimerChoice)}>
						<option value="none">No timer</option>
						<option value="speed">Speed chess (3 min, +2s)</option>
						<option value="bank">Chess clock</option>
						<option value="perTurn">Turn limit - lose on timeout</option>
						<option value="perTurnSkip">Turn limit - skip on timeout</option>
					</select>
				</label>
				{timer === 'bank' && <>
					<label className="dialog__field">
						Minutes each
						<input type="number" min={0.5} max={60} step={0.5} value={bankMinutes} onChange={event => setBankMinutes(Number(event.target.value))} />
					</label>
					<label className="dialog__field">
						Seconds added per turn
						<input type="number" min={0} max={60} value={incrementSeconds} onChange={event => setIncrementSeconds(Number(event.target.value))} />
					</label>
				</>}
				{(timer === 'perTurn' || timer === 'perTurnSkip') && (
					<label className="dialog__field">
						Seconds per turn
						<input type="number" min={5} max={600} value={turnSeconds} onChange={event => setTurnSeconds(Number(event.target.value))} />
					</label>
				)}
				{timer !== 'none' && (
					<p className="dialog__hint">
						{timer === 'perTurnSkip' ? 'Timing out skips your turn.' : 'Timing out loses.'} Starts after the first turn.
					</p>
				)}
			</fieldset>
			<div className="dialog__actions">
						<button onClick={onClose}>Cancel</button>
						<button onClick={submit} disabled={starting || !name.trim() || (inviteOnly && invited.size === 0)}>
							{starting ? 'Starting...' : 'Start game'}
						</button>
					</div>
				</>}
				{error && <p className="dialog__error">{error}</p>}
			</div>
		</div>
	)
}
