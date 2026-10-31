import { isRouteErrorResponse, Link, Navigate, useRouteError } from "react-router"
import { SessionExpiredError } from "./auth"
import { ServerUnavailableError } from "./serverWake"
import { loginRedirectPath } from "./loaders"
import "./RouteError.css"

/** Replaces React Router's default developer-facing error page. Sits inside the Layout route, so the site nav stays
 * usable around it */
export default function RouteError() {
	const error = useRouteError()

	if (error instanceof SessionExpiredError) {
		return <Navigate to={loginRedirectPath()} replace />
	}

	let title = 'Something went wrong'
	let detail = "Something went wrong. Try again."
	if (error instanceof ServerUnavailableError) {
		title = "Can't reach the server"
		detail = error.message
	} else if (isRouteErrorResponse(error)) {
		if (error.status === 404) {
			title = 'Not found'
			detail = typeof error.data === 'string' && error.data ? error.data : "That page or game doesn't exist."
		} else if (error.status === 401 || error.status === 403) {
			title = "You don't have access to this"
			detail = 'This needs a named account - create one or log in.'
		} else if (error.status >= 500) {
			detail = 'Server error. It may be waking up - try again shortly.'
		}
	}

	return (
		<section className="route-error">
			<h1>{title}</h1>
			<p>{detail}</p>
			<p className="route-error__actions">
				<button type="button" onClick={() => window.location.reload()}>Try again</button>
				<Link to="/">Back to games</Link>
			</p>
			{import.meta.env.DEV && error instanceof Error && <pre className="route-error__detail">{error.stack ?? error.message}</pre>}
		</section>
	)
}
