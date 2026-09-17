import { useEffect, useState } from "react"
import { Link, useNavigate } from "react-router"
import { authorizedFetch } from "./auth"
import type { MapSummary } from "./model/MapSummary"
import "./StartGameDialog.css"

export default function StartGameDialog({ onClose }: { onClose: () => void }) {
	const navigate = useNavigate()
	const [maps, setMaps] = useState<MapSummary[] | null>(null)
	const [selectedMapId, setSelectedMapId] = useState<number | null>(null)
	const [name, setName] = useState('')
	const [error, setError] = useState<string | null>(null)
	const [starting, setStarting] = useState(false)

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
			body: JSON.stringify({ mapId: selectedMapId, name }),
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
			<div className="dialog" onClick={event => event.stopPropagation()}>
				<h2>Start a new game</h2>

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
					<label className="dialog__field">
						Game name
						<input value={name} onChange={event => setName(event.target.value)} />
					</label>
					<div className="dialog__actions">
						<button onClick={onClose}>Cancel</button>
						<button onClick={submit} disabled={starting || !name.trim()}>
							{starting ? 'Starting...' : 'Start game'}
						</button>
					</div>
				</>}
				{error && <p className="dialog__error">{error}</p>}
			</div>
		</div>
	)
}
