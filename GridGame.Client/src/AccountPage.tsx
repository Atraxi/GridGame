import { useState } from "react"
import { Link, useLoaderData, useNavigate } from "react-router"
import type { PlayerProfile } from "./model/PlayerProfile"
import type { MapSummary } from "./model/MapSummary"
import type { PastOpponent } from "./model/PastOpponent"
import { authorizedFetch, isGuest, logout, logoutEverywhere } from "./auth"
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

	const signOut = (everywhere: boolean) => {
		(everywhere ? logoutEverywhere() : logout())
			.then(() => navigate('/'))
			.catch(err => setError(err.message))
	}

	return (
		<section className="account-page">
			<h1>Account</h1>
			<p>Playing as {profile.userName}{guest && ' (guest)'}</p>
			{guest && <p><Link to="/login">Create an account</Link> to keep your progress and unlock map creation.</p>}

			<h2>Stats</h2>
			<ul className="account-page__stats">
				<li>Games played: {profile.gamesPlayed}</li>
				<li>Games won: {profile.gamesWon}</li>
				<li>Games lost: {profile.gamesLost}</li>
			</ul>

			<h2>Maps you've designed</h2>
			{maps.length === 0
				? <p>No maps yet.</p>
				: <ul className="account-page__maps">
					{maps.map(map => (
						<li key={map.id}>
							<span className="account-page__map-name">
								{map.name}
								{map.isDraft && <span className="account-page__badge" title="Only you can see a draft, and games can't be hosted from it until it's published">Draft</span>}
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
					Anyone with a named account who has made a move in a game with you. When you host a game, you can make
					it invite-only and pick who may join from this list.
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
									title="Takes them off your list only. They're added back if you play together again"
								>
									Remove
								</button>
							</li>
						))}
					</ul>}
				{opponentsError && <p className="account-page__error">{opponentsError}</p>}

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
