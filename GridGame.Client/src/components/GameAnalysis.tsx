import type { BoardAnalysis, PlayerAnalysis, VictoryStatus } from "../model/analysis"
import "./GameAnalysis.css"

const STATUS_LABELS: Record<VictoryStatus, string> = {
	None: '-',
	Leading: 'Leading',
	Decided: 'Decided',
	Locked: 'Locked in',
}

type Row = { label: string, hint: string, value: (player: PlayerAnalysis) => number | string, emphasis?: boolean }

const ROWS: Row[] = [
	{ label: 'Owned', hint: 'Tiles held right now', value: p => p.owned },
	{ label: '- untouchable', hint: 'Owned tiles no opponent can ever get within spawn/jump range of', value: p => p.ownedUntouchable },
	{ label: '- threatened', hint: 'Owned tiles an opponent could still get into range of and kill', value: p => p.ownedThreatened },
	{ label: 'Uncontested, secure', hint: "Unclaimed tiles only this player can reach, by a route nobody can interfere with", value: p => p.uncontestedSecure },
	{ label: 'Uncontested, at risk', hint: "Unclaimed tiles only this player can reach, but only through threatened tiles or contested ground - they could be cut off", value: p => p.uncontestedAtRisk },
	{ label: 'Contested in reach', hint: 'Unclaimed tiles this player and at least one opponent can both reach - a race', value: p => p.contestedReachable },
	{ label: 'Floor', hint: 'Untouchable + secure: kept no matter what anyone else does', value: p => p.floor, emphasis: true },
	{ label: 'Projected', hint: 'Owned + all uncontested: the score if the game ended now, treating threats on both sides as cancelling out', value: p => p.projected, emphasis: true },
	{ label: 'Ceiling', hint: 'Owned + everything reachable, contested included: the most this player could finish with', value: p => p.ceiling, emphasis: true },
	{ label: 'Status', hint: 'Leading: best projection. Decided: projection beats every opponent\'s ceiling (only threats could swing it). Locked in: floor beats every opponent\'s ceiling', value: p => STATUS_LABELS[p.status] },
]

const percent = (part: number, whole: number) => whole === 0 ? '-' : `${Math.round(part / whole * 100)}%`

/** Indicative end-game statistics. Deliberately a table of evidence rather than a verdict: exact victory detection
 * isn't practical (and some players prefer to play on regardless), so players read these and decide for themselves */
export default function GameAnalysis({ analysis, myPlayerNumber }: { analysis: BoardAnalysis, myPlayerNumber: number | null }) {
	const players = analysis.players.filter(player => player.isActive || player.owned > 0)
	const scoreable = analysis.totalCells - analysis.deadCells

	return (
		<section className="game-analysis">
			<div className="game-analysis__scroll">
				<table>
					<thead>
						<tr>
							<th scope="col"></th>
							{players.map(player => (
								<th scope="col" key={player.playerNumber}>
									<span className="game-analysis__swatch" style={{ background: `var(--player-${((player.playerNumber - 1) % 6) + 1})` }} />
									P{player.playerNumber}{player.playerNumber === myPlayerNumber && ' (you)'}
								</th>
							))}
						</tr>
					</thead>
					<tbody>
						{ROWS.map(row => (
							<tr key={row.label} className={row.emphasis ? 'game-analysis__emphasis' : undefined}>
								<th scope="row" title={row.hint}>{row.label}</th>
								{players.map(player => <td key={player.playerNumber}>{row.value(player)}</td>)}
							</tr>
						))}
					</tbody>
				</table>
			</div>
			<dl className="game-analysis__summary">
				<div><dt>Contested</dt><dd>{analysis.contestedCells} ({percent(analysis.contestedCells, analysis.unclaimedCells)} of unclaimed)</dd></div>
				<div><dt>Unreachable</dt><dd>{analysis.unreachableCells}</dd></div>
				<div><dt>Unclaimed</dt><dd>{analysis.unclaimedCells} of {scoreable} live tiles</dd></div>
				<div><dt>Settled</dt><dd title="Tiles whose final owner (or lack of one) can no longer change">{percent(analysis.settledCells, analysis.totalCells)} of the board</dd></div>
			</dl>
			<details className="game-analysis__definitions">
				<summary>What do these mean?</summary>
				<dl>
					{ROWS.map(row => <div key={row.label}><dt>{row.label.replace(/^- /, 'Owned, ')}</dt><dd>{row.hint}</dd></div>)}
				</dl>
				<p className="game-analysis__note">
					Indicative only. A jump kills the tile it leaves from, so a tile whose only ways out are jumps in
					different directions can really only take one of them - these stats count every way out, which can
					overstate reach in narrow corridors. Kill-for-kill trades are also ignored.
				</p>
			</details>
		</section>
	)
}
