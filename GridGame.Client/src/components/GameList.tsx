import { useEffect, useState } from 'react'
import { Link, useLoaderData } from 'react-router'

type GameSummary = {
	id: number
	creationDate: string
	name: string
	turnNumber: number
}

export default function GameList() {
	const [page, setPage] = useState(1)
	const [gamesList, setGamesList] = useState(useLoaderData<GameSummary[]>())
	const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

	useEffect(() => {
    fetch(`/Games/GetSummaries?page=${page}`)
      .then(async response => {
        if (!response.ok) {
          throw new Error(`Request failed: ${response.status}`)
        }
        const data = (await response.json()) as GameSummary[]
        setGamesList(data)
      })
      .catch(err => setError(err.message))
      .finally(() => setLoading(false))
  }, [page])

	if (loading) {
    return <p>Loading open games...</p>
  }

  if (error) {
    return <p>Error: {error}</p>
  }

	return (
		<>
			<table>
				<thead>
					<tr>
						<th>Created on</th>
						<th>Name</th>
						<th>Turn count</th>
						<th></th>
					</tr>
				</thead>
				<tbody>
					{gamesList.map(gameSummary => (
						<tr key={gameSummary.id} >
							<td>{gameSummary.creationDate}</td>
              <td>{gameSummary.name}</td>
              <td>{gameSummary.turnNumber}</td>
							<td><Link to={`/gridgame/${gameSummary.id}`}>Join Game</Link></td>
						</tr>
					))}
				</tbody>
			</table>
			<button
				className="counter"
				onClick={() => setPage(page => Math.max(0, page - 1))}
			>
				Previous page
			</button>
			<button
				className="counter"
				onClick={() => setPage(page => page + 1)}
			>
				Next page
			</button>
		</>
	)
}