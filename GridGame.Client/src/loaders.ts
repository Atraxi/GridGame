import { redirect, type LoaderFunctionArgs } from 'react-router'
import { authorizedFetch, SessionExpiredError } from './auth'
import type { MapDetails } from './model/MapSummary'

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

/** What the map editor opens with - see CreateMapPage */
export type MapEditorSource =
	| { mode: 'new', map: null }
	| { mode: 'edit' | 'clone', map: MapDetails }

/** /maps/create, optionally ?from=<mapId> to start from a copy of an existing published map (or one of your own) */
export async function loadNewMap({ request }: LoaderFunctionArgs): Promise<MapEditorSource> {
	const from = new URL(request.url).searchParams.get('from')
	return from
		? { mode: 'clone', map: await loadJson<MapDetails>(`/Maps/Get?mapId=${encodeURIComponent(from)}`) }
		: { mode: 'new', map: null }
}

/** /maps/:mapId/edit. Only the map's designer can edit it - anyone else is sent to make a copy instead */
export async function loadMapToEdit({ params }: LoaderFunctionArgs): Promise<MapEditorSource> {
	const map = await loadJson<MapDetails>(`/Maps/Get?mapId=${encodeURIComponent(params.mapId ?? '')}`)
	if (!map.isMine) {
		throw redirect(`/maps/create?from=${map.id}`)
	}
	return { mode: 'edit', map }
}
