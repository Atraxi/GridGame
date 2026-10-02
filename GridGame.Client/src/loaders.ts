import { redirect } from 'react-router'
import { authorizedFetch, SessionExpiredError } from './auth'

/** Where to send someone whose session expired, remembering where they were so logging back in returns them there */
export const loginRedirectPath = (returnTo = window.location.pathname + window.location.search) =>
	`/login?expired=1&returnTo=${encodeURIComponent(returnTo)}`

/** For route loaders: fetches JSON, turning an expired session into a redirect to the login page and any other
 * failure into a thrown Response, which React Router hands to the route's ErrorBoundary (see RouteError) with its
 * status intact - rather than returning the error body as if it were the page's data */
export async function loadJson<T>(url: string): Promise<T> {
	let response: Response
	try {
		response = await authorizedFetch(url)
	} catch (error) {
		if (error instanceof SessionExpiredError) {
			throw redirect(loginRedirectPath())
		}
		throw error
	}
	if (!response.ok) {
		throw response
	}
	return response.json()
}
