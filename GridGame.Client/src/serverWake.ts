import { useSyncExternalStore } from 'react'

//Everything runs on free tiers that go to sleep when idle: App Service unloads the app after ~20 minutes without a
//request, and the serverless database pauses after an hour without a query. Waking either takes up to a minute or so,
//and meanwhile requests fail with a gateway error (502/503/504) or don't connect at all. So instead of failing
//straight away, requests made here keep retrying for a few minutes, and the site shows a "waking up" notice while any
//of them are waiting

/** Thrown once the server still hasn't answered after waiting MAX_WAIT_MS for it to wake */
export class ServerUnavailableError extends Error {
	constructor() {
		super("The server isn't responding. It may still be waking up - try again in a moment.")
		this.name = 'ServerUnavailableError'
	}
}

const MAX_WAIT_MS = 4 * 60_000
const FIRST_DELAY_MS = 1_000
const MAX_DELAY_MS = 10_000

//Retrying a GET is always safe. Anything else is only retried when it can't have been acted on: a 503 means the app
//wasn't up to take it, and a network error means it never got as far as the server. A 502 or 504 could arrive after
//the server already did the work, and doing it twice (hosting two games, spending a refresh token twice) is worse
//than asking the user to try again
const SAFE_TO_RETRY = new Set([502, 503, 504])
const UNSAFE_TO_RETRY = new Set([503])

let waitingRequests = 0
const listeners = new Set<() => void>()
const setWaiting = (change: 1 | -1) => {
	waitingRequests += change
	listeners.forEach(listener => listener())
}

/** True while any request is waiting on the server to wake up */
export const useServerWaking = () => useSyncExternalStore(
	listener => { listeners.add(listener); return () => listeners.delete(listener) },
	() => waitingRequests > 0)

const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

/** fetch, but patient with a server that's asleep. Resolves with the first response that isn't a wake-up failure;
 * rejects with ServerUnavailableError if the server is still unreachable after a few minutes */
export async function fetchWhileWaking(input: string, init?: RequestInit): Promise<Response> {
	const method = (init?.method ?? 'GET').toUpperCase()
	const retryStatuses = method === 'GET' || method === 'HEAD' ? SAFE_TO_RETRY : UNSAFE_TO_RETRY
	const started = Date.now()
	let delay = FIRST_DELAY_MS
	let waiting = false
	try {
		while (true) {
			try {
				const response = await fetch(input, init)
				if (!retryStatuses.has(response.status)) {
					return response
				}
				if (Date.now() - started > MAX_WAIT_MS) {
					return response
				}
			} catch (error) {
				//An abort is the caller giving up, not the server being asleep
				if (init?.signal?.aborted) {
					throw error
				}
				if (Date.now() - started > MAX_WAIT_MS) {
					throw new ServerUnavailableError()
				}
			}
			if (!waiting) {
				waiting = true
				setWaiting(1)
			}
			await sleep(delay)
			delay = Math.min(delay * 2, MAX_DELAY_MS)
		}
	} finally {
		if (waiting) {
			setWaiting(-1)
		}
	}
}
