import { useEffect, useState } from "react"
import { Link, useNavigate } from "react-router"
import { authorizedFetch } from "./auth"
import type { MapSummary } from "./model/MapSummary"
import type { PastOpponent } from "./model/PastOpponent"
import "./StartGameDialog.css"

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

	const submit = () => {
		if (selectedMapId == null) {
			return
		}
		setStarting(true)
		setError(null)
		authorizedFetch('/Games/CreateFromMap', {
			method: 'POST',
			body: JSON.stringify({ mapId: selectedMapId, name, invitedPlayerIds: inviteOnly ? [...invited] : [] }),
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
				{maps?.length === 0 && <p>No maps exist yet - <Link to="/maps/create" onClick={onClose}>create one</Link> first.</p>}

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
						Want to tweak it first?{' '}
						<Link to={`/maps/create?from=${selectedMapId}`} onClick={onClose}>Copy this map into the editor</Link>
					</p>
					<label className="dialog__field">
						Game name
						<input value={name} onChange={event => setName(event.target.value)} />
					</label>
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
							? <p className="dialog__hint">Loading the players you've played with...</p>
							: opponents.length === 0
								? <p className="dialog__hint">
									You can only invite named players you've played a game with before, and there aren't any yet.
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
						{inviteOnly && <p className="dialog__hint">Anyone else can still watch.</p>}
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
