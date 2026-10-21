import { useEffect, useRef, useState, type CSSProperties, type PointerEvent } from "react"
import { Link, useBlocker, useLoaderData, useLocation, useNavigate } from "react-router"
import Tile, { UNCLAIMED, DEAD } from "./components/game_board/Tile"
import "./components/game_board/Board.css"
import { authorizedFetch, isGuest } from "./auth"
import type { MapEditorSource } from "./loaders"
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

type Design = { name: string, playerCount: number, actionsPerTurn: number, board: number[][] }

/** What the editor starts from: a blank board, one of the player's own maps to edit, or any published map to copy */
function initialDesign(source: MapEditorSource): Design {
	if (!source.map) {
		return { name: '', playerCount: 2, actionsPerTurn: 3, board: makeBoard(DEFAULT_HEIGHT, DEFAULT_WIDTH) }
	}
	return {
		name: source.mode === 'clone' ? `${source.map.name} (copy)` : source.map.name,
		playerCount: source.map.playerCount,
		actionsPerTurn: source.map.actionsPerTurn,
		board: source.map.startingBoard,
	}
}

//Compared rather than tracked edit by edit, so undoing a change by hand also clears the unsaved-changes warning
const snapshotOf = (design: Design) => JSON.stringify(design)

/** Keyed on the URL so that moving between maps (or from a blank board to a clone) starts the editor afresh rather
 * than carrying over the previous map's state - React Router keeps a component mounted across a search change */
export default function MapEditorRoute() {
	const location = useLocation()
	return <CreateMapPage key={location.pathname + location.search} />
}

function CreateMapPage() {
	const source = useLoaderData<MapEditorSource>()
	const navigate = useNavigate()
	const location = useLocation()
	const [initial] = useState(() => initialDesign(source))
	//Only an existing map being edited has an id - a clone saves as a brand new map
	const mapId = source.mode === 'edit' ? source.map.id : null
	const [name, setName] = useState(initial.name)
	const [playerCount, setPlayerCount] = useState(initial.playerCount)
	const [actionsPerTurn, setActionsPerTurn] = useState(initial.actionsPerTurn)
	const [width, setWidth] = useState(initial.board[0]?.length ?? DEFAULT_WIDTH)
	const [height, setHeight] = useState(initial.board.length)
	const [board, setBoard] = useState(initial.board)
	//A new map or a clone starts life as a draft until it's published
	const [isDraft, setIsDraft] = useState(source.mode === 'edit' ? source.map.isDraft : true)
	const [savedSnapshot, setSavedSnapshot] = useState(() => source.mode === 'edit' ? snapshotOf(initial) : null)
	const [error, setError] = useState<string | null>(null)
	//Carried across the redirect from a new map's first save to its edit page, which remounts the editor
	const [notice, setNotice] = useState<string | null>((location.state as { notice?: string } | null)?.notice ?? null)
	const [saving, setSaving] = useState(false)
	const [selectedTile, setSelectedTile] = useState(1)
	//Refs, not state - read on every pointer move while painting, and don't need to trigger a re-render. The pointer
	//id keeps a second finger from painting over the first, and the last tile painted saves re-painting it on every
	//move within it
	const paintingPointer = useRef<number | null>(null)
	const lastPainted = useRef<string | null>(null)

	//A blank board counts as unsaved only once something has been changed on it
	const dirty = snapshotOf({ name, playerCount, actionsPerTurn, board }) !== (savedSnapshot ?? snapshotOf(initial))
	//Read by the navigation blocker, which is consulted at navigation time - after a save, that's before the
	//re-render that would otherwise have told it the changes are saved
	const dirtyRef = useRef(dirty)
	useEffect(() => { dirtyRef.current = dirty }, [dirty])

	//Catches the pointer being released after it has been dragged off the board entirely
	useEffect(() => {
		const stopPainting = () => { paintingPointer.current = null }
		window.addEventListener('pointerup', stopPainting)
		window.addEventListener('pointercancel', stopPainting)
		return () => {
			window.removeEventListener('pointerup', stopPainting)
			window.removeEventListener('pointercancel', stopPainting)
		}
	}, [])

	//Closing the tab or reloading with unsaved changes gets the browser's own "leave site?" prompt...
	useEffect(() => {
		if (!dirty) {
			return
		}
		const warn = (event: BeforeUnloadEvent) => event.preventDefault()
		window.addEventListener('beforeunload', warn)
		return () => window.removeEventListener('beforeunload', warn)
	}, [dirty])

	//...and navigating away within the app gets an equivalent one
	const blocker = useBlocker(({ currentLocation, nextLocation }) =>
		dirtyRef.current && currentLocation.pathname + currentLocation.search !== nextLocation.pathname + nextLocation.search)
	useEffect(() => {
		if (blocker.state === 'blocked') {
			if (window.confirm('This map has unsaved changes. Leave without saving them?')) {
				blocker.proceed()
			} else {
				blocker.reset()
			}
		}
	}, [blocker])

	if (isGuest()) {
		return (
			<section>
				<h1>Create a map</h1>
				<p>You need a named account to design maps. <Link to="/login">Create one</Link>.</p>
			</section>
		)
	}

	const maxSize = Math.max(MAX_SIZE, initial.board.length, initial.board[0]?.length ?? 0)

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

	//Still wired up for the keyboard: pointers paint on pointerdown, below
	const onTileClicked = (row: number, column: number) => () => {
		paintTile(row, column)
		return undefined
	}

	//Hit-tested rather than relying on each tile's own enter events: a touch keeps sending its events to the element
	//it started on, so a finger dragged across the board would otherwise only ever paint the first tile
	const paintUnderPointer = (event: PointerEvent<HTMLElement>) => {
		const tile = document.elementFromPoint(event.clientX, event.clientY)?.closest<HTMLElement>('[data-row]')
		if (!tile || !event.currentTarget.contains(tile)) {
			return
		}
		const key = `${tile.dataset.row}.${tile.dataset.column}`
		if (key !== lastPainted.current) {
			lastPainted.current = key
			paintTile(Number(tile.dataset.row), Number(tile.dataset.column))
		}
	}

	/** Drafts skip the server's playability checks, so an unfinished map can be saved and come back to later */
	const save = (asDraft: boolean) => {
		const design = { name, playerCount, actionsPerTurn, board }
		setSaving(true)
		setError(null)
		setNotice(null)
		authorizedFetch(mapId == null ? '/Maps/Create' : `/Maps/Update?mapId=${mapId}`, {
			method: 'POST',
			body: JSON.stringify({ name, playerCount, actionsPerTurn, startingBoard: board, isDraft: asDraft }),
		})
			.then(async response => {
				if (!response.ok) {
					throw new Error(await response.text())
				}
				const savedId = (await response.json()) as number
				dirtyRef.current = false
				setSavedSnapshot(snapshotOf(design))
				setIsDraft(asDraft)
				if (!asDraft) {
					navigate('/account')
				} else if (mapId == null) {
					//From here on, saving updates this draft rather than creating another one
					navigate(`/maps/${savedId}/edit`, { replace: true, state: { notice: 'Draft saved.' } })
				} else {
					setNotice('Draft saved.')
				}
			})
			.catch(err => setError(err.message))
			.finally(() => setSaving(false))
	}

	const heading = source.mode === 'edit' ? 'Edit map' : source.mode === 'clone' ? 'Clone a map' : 'Create a map'

	return (
		<section className="map-editor">
			<h1>{heading}</h1>
			{source.mode === 'clone' && (
				<p className="map-editor__note">
					Starting from "{source.map.name}"{source.map.designedByUserName && ` by ${source.map.designedByUserName}`}.
					Saving creates a new map of your own - the original isn't changed.
				</p>
			)}
			{source.mode === 'edit' && !isDraft && (
				<p className="map-editor__note">
					This map is published. Games already hosted from it keep the board they started with.
				</p>
			)}

			<label className="map-editor__field map-editor__field--name">
				Map name
				<input value={name} maxLength={100} onChange={event => setName(event.target.value)} />
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
						type="number" min={MIN_SIZE} max={maxSize} value={width}
						onChange={event => updateSize(height, clamp(Number(event.target.value), MIN_SIZE, maxSize))}
					/>
				</label>
				<label className="map-editor__field">
					Height
					<input
						type="number" min={MIN_SIZE} max={maxSize} value={height}
						onChange={event => updateSize(clamp(Number(event.target.value), MIN_SIZE, maxSize), width)}
					/>
				</label>
			</div>

			<p>Pick a tile type from the palette, then tap, click or drag across the board to paint it.</p>
			<p>
				To publish, every player needs at least one starting tile, and every player must be reachable from every
				other player's territory. Not finished? Save it as a draft and come back to it from your account page.
			</p>

			<div className="map-editor__workspace">
				<div className="board-frame">
					<div
						className="board"
						style={{ '--columns': width } as CSSProperties}
						onPointerDown={event => {
							if (event.button !== 0 || paintingPointer.current != null) {
								return
							}
							paintingPointer.current = event.pointerId
							lastPainted.current = null
							paintUnderPointer(event)
						}}
						onPointerMove={event => {
							if (paintingPointer.current === event.pointerId) {
								paintUnderPointer(event)
							}
						}}
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
									cell={{ row: rowIndex, column: columnIndex }}
									onTileClicked={onTileClicked(rowIndex, columnIndex)}
								/>
							))
						)}
					</div>
				</div>

				<div className="map-editor__palette">
					{paletteEntries(playerCount).map(({ value, label }) => (
						<button
							key={value}
							type="button"
							className={`map-editor__swatch${value === selectedTile ? ' map-editor__swatch--selected' : ''}`}
							onClick={() => setSelectedTile(value)}
						>
							<Tile owner={value} edges={{ top: true, right: true, bottom: true, left: true }} />
							{label}
						</button>
					))}
				</div>
			</div>

			<div className="map-editor__actions">
				<button type="button" onClick={() => save(true)} disabled={saving}>
					{isDraft ? 'Save draft' : 'Unpublish and save as draft'}
				</button>
				<button type="button" onClick={() => save(false)} disabled={saving || !name.trim()}>
					{saving ? 'Saving...' : isDraft ? 'Publish map' : 'Save changes'}
				</button>
				<span className="map-editor__status" aria-live="polite">
					{dirty ? 'Unsaved changes' : notice}
				</span>
			</div>
			{error && <p className="map-editor__error">{error}</p>}
		</section>
	)
}
