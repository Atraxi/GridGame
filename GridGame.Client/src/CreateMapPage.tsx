import { useEffect, useRef, useState, type CSSProperties } from "react"
import { Link, useNavigate } from "react-router"
import Tile, { UNCLAIMED, DEAD } from "./components/game_board/Tile"
import "./components/game_board/Board.css"
import { authorizedFetch, isGuest } from "./auth"
import "./CreateMapPage.css"

const DEFAULT_WIDTH = 10
const DEFAULT_HEIGHT = 8
const MIN_SIZE = 2
const MAX_SIZE = 30
const MIN_PLAYERS = 2
const MAX_PLAYERS = 8
const MIN_ACTIONS_PER_TURN = 1
const MAX_ACTIONS_PER_TURN = 10

const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max)

const makeBoard = (height: number, width: number): number[][] =>
	Array.from({ length: height }, () => Array.from({ length: width }, () => UNCLAIMED))

//Keeps whatever fits when the board is resized, instead of throwing every edit away
const resizeBoard = (board: number[][], height: number, width: number): number[][] =>
	Array.from({ length: height }, (_, row) =>
		Array.from({ length: width }, (_, column) => board[row]?.[column] ?? UNCLAIMED)
	)

const paletteEntries = (playerCount: number) => [
	{ value: UNCLAIMED, label: 'Unclaimed' },
	{ value: DEAD, label: 'Dead' },
	...Array.from({ length: playerCount }, (_, index) => ({ value: index + 1, label: `Player ${index + 1}` })),
]

export default function CreateMapPage() {
	const navigate = useNavigate()
	const [name, setName] = useState('')
	const [playerCount, setPlayerCount] = useState(2)
	const [actionsPerTurn, setActionsPerTurn] = useState(3)
	const [width, setWidth] = useState(DEFAULT_WIDTH)
	const [height, setHeight] = useState(DEFAULT_HEIGHT)
	const [board, setBoard] = useState(() => makeBoard(DEFAULT_HEIGHT, DEFAULT_WIDTH))
	const [error, setError] = useState<string | null>(null)
	const [saving, setSaving] = useState(false)
	const [selectedTile, setSelectedTile] = useState(1)
	//A ref, not state - toggled on every pointer move while painting, and doesn't need to trigger a re-render
	const isPainting = useRef(false)

	//Catches a mouseup that happens after the pointer has been dragged off the board entirely
	useEffect(() => {
		const stopPainting = () => { isPainting.current = false }
		window.addEventListener('mouseup', stopPainting)
		return () => window.removeEventListener('mouseup', stopPainting)
	}, [])

	if (isGuest()) {
		return (
			<section>
				<h1>Create a map</h1>
				<p>You need a named account to design maps. <Link to="/login">Create one</Link>.</p>
			</section>
		)
	}

	const updateSize = (newHeight: number, newWidth: number) => {
		setHeight(newHeight)
		setWidth(newWidth)
		setBoard(previous => resizeBoard(previous, newHeight, newWidth))
	}

	const updatePlayerCount = (newCount: number) => {
		setPlayerCount(newCount)
		//A tile assigned to a player number that no longer exists would be a broken map, so it reverts to unclaimed
		setBoard(previous => previous.map(row => row.map(cell => cell > newCount ? UNCLAIMED : cell)))
		setSelectedTile(previous => previous > newCount ? UNCLAIMED : previous)
	}

	const ownerAt = (row: number, column: number) => board[row]?.[column] ?? UNCLAIMED

	const paintTile = (row: number, column: number) => {
		setBoard(previous => previous.map((r, rowIndex) =>
			rowIndex === row
				? r.map((cell, columnIndex) => columnIndex === column ? selectedTile : cell)
				: r
		))
	}

	const onTileClicked = (row: number, column: number) => () => {
		paintTile(row, column)
		return undefined
	}

	const onTileEntered = (row: number, column: number) => () => {
		if (isPainting.current) {
			paintTile(row, column)
		}
	}

	const submit = () => {
		setSaving(true)
		setError(null)
		authorizedFetch('/Maps/Create', {
			method: 'POST',
			body: JSON.stringify({ name, playerCount, actionsPerTurn, startingBoard: board }),
		})
			.then(async response => {
				if (!response.ok) {
					throw new Error(await response.text())
				}
				navigate('/account')
			})
			.catch(err => setError(err.message))
			.finally(() => setSaving(false))
	}

	return (
		<section className="map-editor">
			<h1>Create a map</h1>

			<label className="map-editor__field map-editor__field--name">
				Map name
				<input value={name} onChange={event => setName(event.target.value)} />
			</label>

			<div className="map-editor__settings">
				<label className="map-editor__field">
					Players
					<input
						type="number" min={MIN_PLAYERS} max={MAX_PLAYERS} value={playerCount}
						onChange={event => updatePlayerCount(clamp(Number(event.target.value), MIN_PLAYERS, MAX_PLAYERS))}
					/>
				</label>
				<label className="map-editor__field">
					Actions per turn
					<input
						type="number" min={MIN_ACTIONS_PER_TURN} max={MAX_ACTIONS_PER_TURN} value={actionsPerTurn}
						onChange={event => setActionsPerTurn(clamp(Number(event.target.value), MIN_ACTIONS_PER_TURN, MAX_ACTIONS_PER_TURN))}
					/>
				</label>
				<label className="map-editor__field">
					Width
					<input
						type="number" min={MIN_SIZE} max={MAX_SIZE} value={width}
						onChange={event => updateSize(height, clamp(Number(event.target.value), MIN_SIZE, MAX_SIZE))}
					/>
				</label>
				<label className="map-editor__field">
					Height
					<input
						type="number" min={MIN_SIZE} max={MAX_SIZE} value={height}
						onChange={event => updateSize(clamp(Number(event.target.value), MIN_SIZE, MAX_SIZE), width)}
					/>
				</label>
			</div>

			<p>Pick a tile type from the palette, then click or drag across the board to paint it.</p>
			<p>Every player needs at least one starting tile, and every player must be reachable from every other player's territory.</p>

			<div className="map-editor__workspace">
				<div
					className="board"
					style={{ '--columns': width } as CSSProperties}
					onMouseDown={() => { isPainting.current = true }}
				>
					{board.map((row, rowIndex) =>
						row.map((owner, columnIndex) => (
							<Tile
								key={`${rowIndex}.${columnIndex}`}
								owner={owner}
								edges={{
									top: ownerAt(rowIndex - 1, columnIndex) !== owner,
									right: ownerAt(rowIndex, columnIndex + 1) !== owner,
									bottom: ownerAt(rowIndex + 1, columnIndex) !== owner,
									left: ownerAt(rowIndex, columnIndex - 1) !== owner,
								}}
								onTileClicked={onTileClicked(rowIndex, columnIndex)}
								onTileEntered={onTileEntered(rowIndex, columnIndex)}
							/>
						))
					)}
				</div>

				<div className="map-editor__palette">
					{paletteEntries(playerCount).map(({ value, label }) => (
						<button
							key={value}
							type="button"
							className={`map-editor__swatch${value === selectedTile ? ' map-editor__swatch--selected' : ''}`}
							onClick={() => setSelectedTile(value)}
						>
							<Tile owner={value} edges={{ top: true, right: true, bottom: true, left: true }} onTileClicked={() => undefined} />
							{label}
						</button>
					))}
				</div>
			</div>

			<button onClick={submit} disabled={saving || !name.trim()}>
				{saving ? 'Saving...' : 'Save map'}
			</button>
			{error && <p className="map-editor__error">{error}</p>}
		</section>
	)
}
