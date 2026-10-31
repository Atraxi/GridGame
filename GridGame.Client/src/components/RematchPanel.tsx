import { useEffect, useState } from "react"
import { Link } from "react-router"
import type * as signalR from '@microsoft/signalr'
import { authorizedFetch, isGuest } from "../auth"
import type { MapSummary } from "../model/MapSummary"
import type { PresenceEntry } from "./PlayerLegend"
import "./RematchPanel.css"

type RematchResult = { gameId?: number, error?: string }

/** Shown once a game is over: start a rematch (GridGameHub.Rematch), or follow one someone else has started. Starting
 * one pulls everyone seated here into it - see the rematchCreated handler in GridGame.tsx */
export default function RematchPanel({ connection, isSeated, rematchGameId, mapId, playerCount, presence }: {
	connection: signalR.HubConnection | null,
	isSeated: boolean,
	rematchGameId: number | null,
	/** The map this game was hosted from, if known - older games didn't record it */
	mapId: number | null,
	playerCount: number,
	presence: PresenceEntry[],
}) {
	const [choosing, setChoosing] = useState(false)
	const [maps, setMaps] = useState<MapSummary[] | null>(null)
	const [selectedMapId, setSelectedMapId] = useState<number | null>(null)
	const [starting, setStarting] = useState(false)
	const [error, setError] = useState<string | null>(null)

	useEffect(() => {
		if (!choosing || maps) {
			return
		}
		authorizedFetch('/Maps/GetSummaries?page=1')
			.then(response => response.ok ? response.json() : Promise.reject(new Error(`Request failed: ${response.status}`)))
			.then((data: MapSummary[]) => {
				setMaps(data)
				setSelectedMapId(data.find(map => map.id === mapId)?.id ?? data[0]?.id ?? null)
			})
			.catch(err => setError(`Couldn't load the maps: ${err.message}`))
	}, [choosing, maps, mapId])

	if (rematchGameId != null) {
		return (
			<div className="rematch-panel">
				<p>A rematch has started.</p>
				<Link className="rematch-panel__go" to={`/gridgame/${rematchGameId}`}>Go to the rematch</Link>
			</div>
		)
	}
	if (!isSeated) {
		return null
	}
	if (isGuest()) {
		return (
			<div className="rematch-panel">
				<p>
					Only named accounts can host a rematch.
					{' '}<Link to="/login">Create an account</Link>
				</p>
			</div>
		)
	}

	//The server answers with the existing rematch if someone beat us to it, and rematchCreated moves everyone across
	const start = (chosenMapId: number | null) => {
		if (!connection) {
			return
		}
		setStarting(true)
		setError(null)
		connection.invoke<RematchResult>("rematch", chosenMapId)
			.then(result => {
				if (result.error) {
					setError(result.error)
				}
			})
			.catch(() => setError("Couldn't start the rematch - check your connection and try again."))
			.finally(() => setStarting(false))
	}

	const selectedMap = maps?.find(map => map.id === selectedMapId)
	//Mirrors GridGameHub.Rematch: a map must have as many seats as this game, unless players have left - then it only
	//has to seat everyone still here
	const seatedEntries = presence.filter(entry => entry.status !== 'open')
	const online = seatedEntries.filter(entry => entry.status === 'connected' || entry.status === 'unresponsive').length
	const offline = seatedEntries.length - online
	const fits = (map: MapSummary) => map.playerCount === playerCount || (offline > 0 && map.playerCount >= online)

	return (
		<div className="rematch-panel">
			<p>Play again? Everyone here is brought along; seats go to whoever arrives first.</p>
			{!choosing
				? <div className="rematch-panel__actions">
					{mapId != null && (
						<button type="button" className="rematch-panel__go" disabled={starting || !connection} onClick={() => start(null)}>
							{starting ? 'Starting...' : 'Rematch on this map'}
						</button>
					)}
					<button type="button" onClick={() => setChoosing(true)}>
						{mapId != null ? 'Pick a different map' : 'Pick a map for a rematch'}
					</button>
				</div>
				: <>
					{maps === null && !error && <p>Loading maps...</p>}
					{maps?.length === 0 && <p>No published maps.</p>}
					{maps && maps.length > 0 && (
						<label className="rematch-panel__field">
							Map
							<select value={selectedMapId ?? undefined} onChange={event => setSelectedMapId(Number(event.target.value))}>
								{maps.map(map => (
									<option key={map.id} value={map.id}>
										{map.name} ({map.playerCount} players, by {map.designedByUserName})
									</option>
								))}
							</select>
						</label>
					)}
					{selectedMap && selectedMap.playerCount < playerCount && (
						<p className="rematch-panel__hint">
							Only {selectedMap.playerCount} seats - the first to arrive play, the rest watch.
						</p>
					)}
					<div className="rematch-panel__actions">
						<button type="button" onClick={() => setChoosing(false)}>Cancel</button>
						<button
							type="button" className="rematch-panel__go"
							disabled={starting || !connection || !selectedMap || !fits(selectedMap)}
							onClick={() => start(selectedMapId)}
						>
							{starting ? 'Starting...' : 'Start rematch'}
						</button>
					</div>
				</>}
			{error && <p className="rematch-panel__error">{error}</p>}
		</div>
	)
}
