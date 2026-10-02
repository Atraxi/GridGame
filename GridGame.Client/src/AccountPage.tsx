import { useState } from "react"
import { Link, useLoaderData, useNavigate } from "react-router"
import type { PlayerProfile } from "./model/PlayerProfile"
import type { MapSummary } from "./model/MapSummary"
import { isGuest, logout, logoutEverywhere } from "./auth"
import "./AccountPage.css"

export default function AccountPage() {
	const { profile, maps } = useLoaderData<{ profile: PlayerProfile, maps: MapSummary[] }>()
	const guest = isGuest()
	const navigate = useNavigate()
	const [error, setError] = useState<string | null>(null)

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
					{maps.map(map => <li key={map.id}>{map.name}</li>)}
				</ul>}

			{!guest && <>
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
