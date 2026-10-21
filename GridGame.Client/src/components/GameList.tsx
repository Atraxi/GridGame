import { useEffect, useRef, useState } from 'react'
import { Link, useLoaderData } from 'react-router'
import type { GameSummary } from '../model/GridGameSummary'
import { authorizedFetch } from '../auth'
import './GameList.css'

const PAGE_SIZE = 20
//Each poll is one request through the Cloudflare Worker (100k/day on the free plan), but an unchanged list costs
//the server no database work (see GameListVersion.cs) - so this is about request volume, not server load
const POLL_INTERVAL_MS = 20_000

const formatDate = (iso: string) => {
	const date = new Date(iso)
	return Number.isNaN(date.getTime()) ? iso : date.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}

/** The home page's game list. Kept current by polling while the tab is visible: the server answers 304 when nothing
 * has changed since the ETag this list last saw, so a quiet poll is cheap */
export default function GameList() {
	const loaderData = useLoaderData<GameSummary[]>()
	const [page, setPage] = useState(1)
	const [gamesList, setGamesList] = useState(loaderData)
	const [error, setError] = useState<string | null>(null)
	//Each page's last-seen rows and ETag, so a 304 can restore the right rows when flicking back to a page already
	//seen. Page 1 starts out with the route loader's copy, which has no ETag yet
	const pages = useRef(new Map<number, { etag: string | null, games: GameSummary[] }>([[1, { etag: null, games: loaderData }]]))

	useEffect(() => {
		let cancelled = false
		let timer: ReturnType<typeof setTimeout> | null = null

		const poll = async () => {
			try {
				const etag = pages.current.get(page)?.etag
				const response = await authorizedFetch(`/Games/GetSummaries?page=${page}`, {
					//Bypasses the browser's own cache so a 304 reaches this code instead of being swapped for a stored copy
					cache: 'no-store',
					headers: etag ? { 'If-None-Match': etag } : undefined,
				})
				if (cancelled) {
					return
				}
				if (response.status === 304) {
					const cached = pages.current.get(page)
					if (cached) {
						setGamesList(cached.games)
					}
					setError(null)
					return
				}
				if (!response.ok) {
					throw new Error(`Request failed: ${response.status}`)
				}
				const data = (await response.json()) as GameSummary[]
				if (cancelled) {
					return
				}
				pages.current.set(page, { etag: response.headers.get('ETag'), games: data })
				setGamesList(data)
				setError(null)
			} catch (err) {
				if (!cancelled) {
					setError(err instanceof Error ? err.message : String(err))
				}
			}
		}

		const schedule = () => {
			if (timer) {
				clearTimeout(timer)
			}
			timer = null
			//A hidden tab doesn't poll at all - it catches up the moment it's shown again
			if (document.visibilityState === 'visible') {
				timer = setTimeout(async () => {
					await poll()
					if (!cancelled) {
						schedule()
					}
				}, POLL_INTERVAL_MS)
			}
		}

		const onVisibilityChange = () => {
			if (document.visibilityState === 'visible') {
				poll().then(() => { if (!cancelled) schedule() })
			} else {
				schedule()
			}
		}

		//Page 1 arrives with the route loader, so it only needs polling from here on; any other page is shown from
		//what was last seen of it (if anything) while it's fetched
		const cached = pages.current.get(page)
		if (cached) {
			setGamesList(cached.games)
		}
		if (page === 1 && cached?.etag === null) {
			schedule()
		} else {
			poll().then(() => { if (!cancelled) schedule() })
		}
		document.addEventListener('visibilitychange', onVisibilityChange)
		return () => {
			cancelled = true
			if (timer) {
				clearTimeout(timer)
			}
			document.removeEventListener('visibilitychange', onVisibilityChange)
		}
	}, [page])

	return (
		<section className="game-list">
			{error && <p className="game-list__error">Couldn't refresh the game list: {error}</p>}
			{gamesList.length === 0
				? <p>{page === 1 ? 'No games yet.' : 'No more games.'}</p>
				: <div className="game-list__scroll">
					<table>
						<thead>
							<tr>
								<th>Game</th>
								<th className="game-list__created">Created</th>
								<th>Turn</th>
								<th>Status</th>
							</tr>
						</thead>
						<tbody>
							{gamesList.map(gameSummary => (
								<tr key={gameSummary.id}>
									<td className="game-list__name">
										<Link to={`/gridgame/${gameSummary.id}`}>{gameSummary.name}</Link>
										{gameSummary.isInviteOnly && <span className="game-list__badge" title="Only invited players can take a seat - anyone can watch">Invite only</span>}
									</td>
									<td className="game-list__created">{formatDate(gameSummary.creationDate)}</td>
									<td>{gameSummary.turnNumber}</td>
									<td>{gameSummary.isGameOver ? 'Finished' : 'In progress'}</td>
								</tr>
							))}
						</tbody>
					</table>
				</div>}
			<p className="game-list__pages">
				<button className="counter" disabled={page <= 1} onClick={() => setPage(current => Math.max(1, current - 1))}>
					Previous page
				</button>
				<button className="counter" disabled={gamesList.length < PAGE_SIZE} onClick={() => setPage(current => current + 1)}>
					Next page
				</button>
			</p>
		</section>
	)
}
