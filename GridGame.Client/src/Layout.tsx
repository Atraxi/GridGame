import { Link, Outlet } from "react-router"
import { useIsGuest, useUserName } from "./auth"
import "./Layout.css"

export default function Layout() {
	const guest = useIsGuest()
	const userName = useUserName()

	return (
		<>
			<nav className="site-nav">
				<Link to="/" className="site-nav__brand">GridGame</Link>
				{guest
					? <button className="site-nav__link" disabled title="Create a named account to design maps">Create Map</button>
					: <Link to="/maps/create" className="site-nav__link">Create Map</Link>}
				<Link to={guest ? "/login" : "/account"} className="site-nav__identity">
					{guest ? "Playing as a guest – create account / log in" : `Playing as ${userName}`}
				</Link>
			</nav>
			<Outlet />
		</>
	)
}
