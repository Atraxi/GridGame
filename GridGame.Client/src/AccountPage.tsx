import { useState } from "react"
import { Link, useLoaderData, useNavigate } from "react-router"
import type { PlayerProfile } from "./model/PlayerProfile"
import type { MapSummary } from "./model/MapSummary"
import type { PastOpponent } from "./model/PastOpponent"
import { authorizedFetch, isGuest, logout, logoutEverywhere } from "./auth"
import { COLOUR_BLIND_SAFE, DEFAULT_COLOURS } from "./components/game_board/seatColours"
import GameList from "./components/GameList"
import { getViewerPalette, setViewerPalette, STOCK_COLOURS, type PaletteSlot } from "./model/palette"
import "./AccountPage.css"

const formatDate = (iso: string) => {
	const date = new Date(iso)
	return Number.isNaN(date.getTime()) ? iso : date.toLocaleDateString(undefined, { dateStyle: 'medium' })
}

export default function AccountPage() {
	const loaded = useLoaderData<{ profile: PlayerProfile, maps: MapSummary[], opponents: PastOpponent[] }>()
	const { profile, maps } = loaded
	const guest = isGuest()
	const navigate = useNavigate()
	const [error, setError] = useState<string | null>(null)
	const [opponents, setOpponents] = useState(loaded.opponents)
	const [opponentsError, setOpponentsError] = useState<string | null>(null)

	const removeOpponent = (opponent: PastOpponent) => {
		setOpponentsError(null)
		authorizedFetch('/Users/RemoveOpponent', { method: 'POST', body: JSON.stringify({ opponentId: opponent.id }) })
			.then(response => {
				if (!response.ok) {
					throw new Error(`Request failed: ${response.status}`)
				}
				setOpponents(current => current.filter(candidate => candidate.id !== opponent.id))
			})
			.catch(err => setOpponentsError(`Couldn't remove ${opponent.userName}: ${err.message}`))
	}

	const [primary, setPrimary] = useState(profile.colourPrimary)
	const [secondary, setSecondary] = useState(profile.colourSecondary)
	const [useDefaults, setUseDefaults] = useState(profile.useDefaultColours)
	const [colourStatus, setColourStatus] = useState<string | null>(null)
	const [slots, setSlots] = useState<PaletteSlot[]>(profile.defaultPalette ?? getViewerPalette())

	//null puts the stock palette back
	const savePalette = (next: PaletteSlot[] | null) => {
		setSlots(next ?? STOCK_COLOURS.map(primary => ({ primary, secondary: null })))
		setViewerPalette(next)
		setColourStatus(null)
		authorizedFetch('/Users/SetDefaultPalette', { method: 'POST', body: JSON.stringify({ slots: next }) })
			.then(async response => setColourStatus(response.ok ? 'Saved.' : await response.text()))
			.catch(err => setColourStatus(`Couldn't save: ${err.message}`))
	}

	const saveColours = (next: { primary: string | null, secondary: string | null, useDefaults: boolean }) => {
		setPrimary(next.primary)
		setSecondary(next.primary ? next.secondary : null)
		setUseDefaults(next.useDefaults)
		setColourStatus(null)
		authorizedFetch('/Users/SetColours', {
			method: 'POST',
			body: JSON.stringify({ primary: next.primary, secondary: next.primary ? next.secondary : null, useDefaultColours: next.useDefaults }),
		})
			.then(async response => setColourStatus(response.ok ? 'Saved.' : await response.text()))
			.catch(err => setColourStatus(`Couldn't save: ${err.message}`))
	}

	const [mergeUserName, setMergeUserName] = useState('')
	const [mergePassword, setMergePassword] = useState('')
	const [mergeStatus, setMergeStatus] = useState<string | null>(null)

	const mergeAccount = () => {
		if (!window.confirm(`Merge "${mergeUserName}" into ${profile.userName}? That account is deleted and its user name freed. This can't be undone.`)) {
			return
		}
		setMergeStatus(null)
		authorizedFetch('/Users/MergeAccount', { method: 'POST', body: JSON.stringify({ userName: mergeUserName, password: mergePassword }) })
			.then(async response => {
				setMergeStatus(response.ok ? `Merged - "${mergeUserName}" is now part of this account.` : await response.text())
				if (response.ok) {
					setMergePassword('')
					setMergeUserName('')
				}
			})
			.catch(err => setMergeStatus(`Couldn't merge: ${err.message}`))
	}

	const signOut = (everywhere: boolean) => {
		(everywhere ? logoutEverywhere() : logout())
			.then(() => navigate('/'))
			.catch(err => setError(err.message))
	}

	return (
		<section className="account-page">
			<h1>Account</h1>
			<p>Playing as {profile.userName}{guest && ' (guest)'}</p>
			{guest && <p><Link to="/login">Create an account</Link> to keep progress and design maps.</p>}

			<h2>Stats</h2>
			<ul className="account-page__stats">
				<li>Games played: {profile.gamesPlayed}</li>
				<li>Games won: {profile.gamesWon}</li>
				<li>Games lost: {profile.gamesLost}</li>
			</ul>

			<h2>Your colour</h2>
			<p className="account-page__hint">
				Replaces the default colour of whichever seat you take, for everyone in the game. A second colour adds a centre-to-edge gradient.
			</p>
			<div className="account-page__colours">
				{primary === null
					? <button type="button" onClick={() => saveColours({ primary: DEFAULT_COLOURS[1], secondary: null, useDefaults })}>Choose a colour</button>
					: <>
						<label>Colour <input type="color" value={primary} onChange={event => saveColours({ primary: event.target.value, secondary, useDefaults })} /></label>
						<label>
							<input type="checkbox" checked={secondary !== null}
								onChange={event => saveColours({ primary, secondary: event.target.checked ? '#ffffff' : null, useDefaults })} />
							Gradient
						</label>
						{secondary !== null && (
							<label>Centre colour <input type="color" value={secondary} onChange={event => saveColours({ primary, secondary: event.target.value, useDefaults })} /></label>
						)}
						<button type="button" onClick={() => saveColours({ primary: null, secondary: null, useDefaults })}>Use the seat's default</button>
					</>}
			</div>
			<label className="account-page__colours">
				<input type="checkbox" checked={useDefaults} onChange={event => saveColours({ primary, secondary, useDefaults: event.target.checked })} />
				Ignore other players' colours - always show my default colours (switchable per game)
			</label>

			<h2>Default colours</h2>
			<p className="account-page__hint">
				The colour you see each seat in, everywhere including the tutorials. Only you see these.
			</p>
			{slots.map((slot, index) => (
				<div key={index} className="account-page__colours">
					<span>P{index + 1}</span>
					<input type="color" aria-label={`Player ${index + 1} colour`} value={slot.primary} onChange={event => savePalette(slots.map((old, at) => at === index ? { ...old, primary: event.target.value } : old))} />
					<label>
						<input type="checkbox" checked={slot.secondary !== null}
							onChange={event => savePalette(slots.map((old, at) => at === index ? { ...old, secondary: event.target.checked ? '#ffffff' : null } : old))} />
						Gradient
					</label>
					{slot.secondary !== null && (
						<input type="color" aria-label={`Player ${index + 1} centre colour`} value={slot.secondary} onChange={event => savePalette(slots.map((old, at) => at === index ? { ...old, secondary: event.target.value } : old))} />
					)}
				</div>
			))}
			<div className="account-page__colours">
				<button type="button" onClick={() => savePalette(COLOUR_BLIND_SAFE.map(primary => ({ primary, secondary: null })))}>Colour-blind-safe set</button>
				<button type="button" onClick={() => savePalette(null)}>Reset to stock</button>
			</div>
			{colourStatus && <p className="account-page__hint">{colourStatus}</p>}

			<h2>Finished games</h2>
			<GameList finished />

			<h2>Maps you've designed</h2>
			{maps.length === 0
				? <p>No maps yet.</p>
				: <ul className="account-page__maps">
					{maps.map(map => (
						<li key={map.id}>
							<span className="account-page__map-name">
								{map.name}
								{map.isDraft && <span className="account-page__badge" title="Private until published">Draft</span>}
							</span>
							<span className="account-page__map-links">
								<Link to={`/maps/${map.id}/edit`}>Edit</Link>
								<Link to={`/maps/create?from=${map.id}`}>Clone</Link>
							</span>
						</li>
					))}
				</ul>}

			{!guest && <>
				<h2>Players you've played with</h2>
				<p className="account-page__hint">
					Named players who've moved in a game with you. You can invite them to invite-only games.
				</p>
				{opponents.length === 0
					? <p>Nobody yet.</p>
					: <ul className="account-page__opponents">
						{opponents.map(opponent => (
							<li key={opponent.id}>
								<span className="account-page__opponent-name">
									{opponent.userName}
									<span className="account-page__meta">Last played {formatDate(opponent.lastPlayedAt)}</span>
								</span>
								<button
									type="button"
									onClick={() => removeOpponent(opponent)}
									title="Removes them from your list only"
								>
									Remove
								</button>
							</li>
						))}
					</ul>}
				{opponentsError && <p className="account-page__error">{opponentsError}</p>}

				<h2>Merge another account into this one</h2>
				<p className="account-page__hint">
					Enter another named account's details to bring its maps, seats, invites, opponents and stats here. That account is then deleted.
				</p>
				<p className="account-page__colours">
					<input placeholder="Other account's user name" value={mergeUserName} onChange={event => setMergeUserName(event.target.value)} autoComplete="off" />
					<input type="password" placeholder="Its password" value={mergePassword} onChange={event => setMergePassword(event.target.value)} autoComplete="off" />
					<button type="button" disabled={!mergeUserName.trim() || !mergePassword} onClick={mergeAccount}>Merge</button>
				</p>
				{mergeStatus && <p className="account-page__hint">{mergeStatus}</p>}

				<h2>Sign out</h2>
				<p className="account-page__actions">
					<button type="button" onClick={() => signOut(false)}>Log out</button>
					<button type="button" onClick={() => signOut(true)} title="Signs out every device and browser using this account">
						Log out everywhere
					</button>
				</p>
				{error && <p>{error}</p>}
			</>}
		</section>
	)
}
