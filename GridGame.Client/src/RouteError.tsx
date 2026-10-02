import { isRouteErrorResponse, Link, Navigate, useRouteError } from "react-router"
import { SessionExpiredError } from "./auth"
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
	let detail = "An unexpected error occurred. Trying again usually sorts it out."
	if (isRouteErrorResponse(error)) {
		if (error.status === 404) {
			title = 'Not found'
			detail = typeof error.data === 'string' && error.data ? error.data : "That page or game doesn't exist."
		} else if (error.status === 401 || error.status === 403) {
			title = "You don't have access to this"
			detail = 'Some features need a named account - create one or log in from the link in the header.'
		} else if (error.status >= 500) {
			detail = 'The server had a problem handling that request. It may be waking up - try again in a moment.'
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
