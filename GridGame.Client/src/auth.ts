import { useSyncExternalStore } from 'react'
import { GUEST_USER_NAME_PREFIX } from './model/Player'

const TOKEN_KEY = 'gridgame.token'
const REFRESH_TOKEN_KEY = 'gridgame.refreshToken'
const USER_NAME_KEY = 'gridgame.userName'

export const getUserName = () => localStorage.getItem(USER_NAME_KEY)

export const isGuest = () => getUserName()?.startsWith(GUEST_USER_NAME_PREFIX) ?? true

/** The stored credentials can no longer be renewed (refresh token expired, revoked, or already used) and the account
 * is a named one, so it can't just be replaced with a fresh guest - the user has to log in again, or choose to
 * carry on as a new guest. Route loaders and the game page turn this into a redirect to the login page */
export class SessionExpiredError extends Error {
	constructor() {
		super('Your session has expired. Please log in again.')
		this.name = 'SessionExpiredError'
	}
}

//localStorage writes aren't reactive on their own, so a component that stays mounted across a Promote/Login/logout
//(e.g. the persistent header layout) would otherwise never re-render to reflect it
const authChangeListeners = new Set<() => void>()
function notifyAuthChange() {
	authChangeListeners.forEach(listener => listener())
}
function subscribeToAuthChange(listener: () => void) {
	authChangeListeners.add(listener)
	return () => authChangeListeners.delete(listener)
}

//Another tab logging in/out writes the same localStorage keys - the storage event is how this tab hears about it
window.addEventListener('storage', event => {
	if (event.key === null || event.key === USER_NAME_KEY) {
		notifyAuthChange()
	}
})

/** Reactive equivalent of getUserName(), for components that stay mounted across an auth change */
export const useUserName = () => useSyncExternalStore(subscribeToAuthChange, getUserName)

/** Reactive equivalent of isGuest(), for components that stay mounted across an auth change */
export const useIsGuest = () => useUserName()?.startsWith(GUEST_USER_NAME_PREFIX) ?? true

async function storeAuth(response: Response) {
	if (!response.ok) {
		throw new Error(await response.text())
	}
	const { token, refreshToken, userName } = await response.json()
	localStorage.setItem(TOKEN_KEY, token)
	localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken)
	localStorage.setItem(USER_NAME_KEY, userName)
	notifyAuthChange()
	return userName as string
}

function clearAuth() {
	localStorage.removeItem(TOKEN_KEY)
	localStorage.removeItem(REFRESH_TOKEN_KEY)
	localStorage.removeItem(USER_NAME_KEY)
	notifyAuthChange()
}

//A JWT's expiry lives in its unencrypted middle segment - decoding it client-side lets callers act on it
//(refresh proactively, or time a SignalR reconnect) without waiting for the server to reject a request
export function getTokenExpiryMs(token: string): number {
	try {
		const { exp } = JSON.parse(atob(token.split('.')[1]))
		return typeof exp === 'number' ? exp * 1000 : Date.now()
	} catch {
		return Date.now()
	}
}

function isExpired(token: string): boolean {
	//Renew a little early so a request already in flight when the clock ticks over doesn't get a 401 anyway
	return Date.now() >= getTokenExpiryMs(token) - 30_000
}

/** Trades the stored refresh token for a new access+refresh pair. Works the same for guest and named accounts -
 * refresh tokens don't care which, only Promote/Login (which need actual credentials) do */
async function tryRefresh(): Promise<boolean> {
	const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY)
	if (!refreshToken) {
		return false
	}
	try {
		await storeAuth(await fetch('/Users/Refresh', {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ refreshToken }),
		}))
		return true
	} catch {
		return false
	}
}

async function acquireToken(staleToken: string | null): Promise<string> {
	//Another tab may have renewed the shared credentials while this one waited for the lock - if so, that renewal
	//already used up the refresh token this tab would have sent, and its result is what to use
	const current = localStorage.getItem(TOKEN_KEY)
	if (current && current !== staleToken && !isExpired(current)) {
		return current
	}

	if (!(await tryRefresh())) {
		if (!isGuest()) {
			//The refresh token is gone/expired/revoked, and a named account can't be re-authenticated without credentials
			clearAuth()
			throw new SessionExpiredError()
		}
		//No token yet, or a guest with no usable refresh token left - either way a fresh anonymous guest is the only option
		await storeAuth(await fetch('/Users/GuestAuth', { method: 'POST' }))
	}
	return localStorage.getItem(TOKEN_KEY)!
}

//A refresh token is single-use, and every tab shares the one in localStorage: if two tabs (or a REST call and the
//SignalR connection's proactive rotation) each notice the access token has expired and refresh independently, the
//loser's request arrives after the token has already been rotated and fails - which for a named account means being
//logged out, and for a guest means being swapped for a brand new one. A Web Lock serialises renewal across tabs
//(acquireToken then picks up the winner's result), and pendingAcquire collapses concurrent callers within this tab
let pendingAcquire: Promise<string> | null = null

const withRenewalLock = <T,>(work: () => Promise<T>): Promise<T> =>
	'locks' in navigator
		? navigator.locks.request('gridgame.token-renewal', work)
		: work()

/** Every visitor is authenticated, as an anonymous guest until they choose to promote that same account to a named one */
export async function ensureToken(): Promise<string> {
	const token = localStorage.getItem(TOKEN_KEY)
	if (token && !isExpired(token)) {
		return token
	}

	if (!pendingAcquire) {
		pendingAcquire = withRenewalLock(() => acquireToken(token)).finally(() => { pendingAcquire = null })
	}
	return pendingAcquire
}

export const authorizedFetch = async (url: string, init?: RequestInit) => {
	const request = async () => fetch(url, {
		...init,
		headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${await ensureToken()}`, ...init?.headers },
	})

	const response = await request()
	if (response.status !== 401) {
		return response
	}
	//ensureToken() thought the token was still good but the server disagreed (clock skew, server restart, etc.) -
	//drop it and let ensureToken() sort out a replacement for a single retry
	localStorage.removeItem(TOKEN_KEY)
	return request()
}

export const postToUsers = async (action: 'Promote' | 'Login', body: unknown) =>
	storeAuth(await authorizedFetch(`/Users/${action}`, { method: 'POST', body: JSON.stringify(body) }))

/** Signs this device out (revoking its refresh token server-side). The next request mints a fresh guest */
export async function logout() {
	const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY)
	clearAuth()
	if (refreshToken) {
		await fetch('/Users/Logout', {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ refreshToken }),
		}).catch(() => {})
	}
}

/** Revokes every device's refresh token for this account, then signs this one out too */
export async function logoutEverywhere() {
	const response = await authorizedFetch('/Users/LogoutEverywhere', { method: 'POST' })
	if (!response.ok) {
		throw new Error(await response.text() || `Request failed: ${response.status}`)
	}
	clearAuth()
}

/** Abandons whatever (expired) session is stored and carries on as a brand new anonymous guest */
export async function startNewGuestSession() {
	clearAuth()
	await ensureToken()
}
