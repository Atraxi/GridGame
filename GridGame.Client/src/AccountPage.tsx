import { Link, useLoaderData } from "react-router"
import type { PlayerProfile } from "./model/PlayerProfile"
import type { MapSummary } from "./model/MapSummary"
import { isGuest } from "./auth"
import "./AccountPage.css"

export default function AccountPage() {
	const { profile, maps } = useLoaderData<{ profile: PlayerProfile, maps: MapSummary[] }>()
	const guest = isGuest()

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
		</section>
	)
}
