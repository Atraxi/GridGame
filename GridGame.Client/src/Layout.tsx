import { Link, Outlet } from "react-router"
import { useIsGuest, useUserName } from "./auth"
import { useServerWaking } from "./serverWake"
import "./Layout.css"

export default function Layout() {
	const guest = useIsGuest()
	const userName = useUserName()
	const waking = useServerWaking()

	return (
		<>
			<nav className="site-nav">
				<Link to="/" className="site-nav__brand">GridGame</Link>
				<Link to="/help" className="site-nav__link">How to play</Link>
				{guest
					? <button className="site-nav__link" disabled title="Create a named account to design maps">Create Map</button>
					: <Link to="/maps/create" className="site-nav__link">Create Map</Link>}
				<Link to={guest ? "/login" : "/account"} className="site-nav__identity">
					{guest
						? <>Playing as a guest<span className="site-nav__long"> – create account</span> / log in</>
						: <>Playing as {userName}</>}
				</Link>
			</nav>
			{waking && (
				<p className="wake-notice" role="status">
					Waking the server up - after it's been idle for a while, this can take up to a minute...
				</p>
			)}
			<Outlet />
		</>
	)
}
