export const GUEST_USER_NAME_PREFIX = 'Guest-'

/** Guests' generated names are noise - to everyone else they're all just "Guest" */
export const displayName = (userName: string | null) =>
	userName == null ? null : userName.startsWith(GUEST_USER_NAME_PREFIX) ? 'Guest' : userName

export type Player = {
	id: string
	userName: string
}
