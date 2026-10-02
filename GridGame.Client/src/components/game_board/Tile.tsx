import type { CSSProperties } from "react";

export const UNCLAIMED = 0
export const DEAD = -1

const PALETTE_SIZE = 6

export type Edges = { top: boolean, right: boolean, bottom: boolean, left: boolean }

const SIDES = ['top', 'right', 'bottom', 'left'] as const

//A corner is convex, and so gets rounded off, when both of the edges meeting there are part of the boundary
const CORNERS = [
	['tl', 'top', 'left'],
	['tr', 'top', 'right'],
	['br', 'bottom', 'right'],
	['bl', 'bottom', 'left'],
] as const

export type MoveHint = 'spawn' | 'jump' | 'jump-origin'

const MOVE_HINT_LABEL: Record<MoveHint, string> = {
	spawn: 'can spawn here',
	jump: 'can jump here',
	'jump-origin': 'jump from here',
}

/** Overlay for an unclaimed tile, from the board analysis */
export type Territory =
	| { kind: 'secure' | 'at-risk', player: number }
	| { kind: 'unreachable' }

function territoryLabel(territory: Territory) {
	switch (territory.kind) {
		case 'secure': return `only player ${territory.player} can reach it`
		case 'at-risk': return `only player ${territory.player} can reach it, but they could still be cut off from it`
		case 'unreachable': return 'nobody can reach it'
	}
}

const playerColour = (player: number) => `var(--player-${((player - 1) % PALETTE_SIZE) + 1})`

export default function Tile({ owner, edges, moveHint, territory, onTileClicked, onTileEntered }: {
	owner: number, edges: Edges, moveHint?: MoveHint, territory?: Territory,
	onTileClicked: () => Promise<any> | undefined,
	//Lets a caller support drag-painting (e.g. the map editor) - only fires while the pointer is already held down over the board
	onTileEntered?: () => void,
}) {
	const isClaimed = owner > UNCLAIMED
	const classes = ['tile']

	if (isClaimed) {
		classes.push(...SIDES.filter(side => edges[side]).map(side => `edge-${side}`))
		classes.push(...CORNERS.filter(([, a, b]) => edges[a] && edges[b]).map(([corner]) => `corner-${corner}`))
	} else {
		classes.push(owner === DEAD ? 'tile--dead' : 'tile--unclaimed')
		if (territory && owner !== DEAD) {
			classes.push(`tile--${territory.kind === 'unreachable' ? 'unreachable' : `territory-${territory.kind}`}`)
		}
	}
	const colourPlayer = isClaimed ? owner : territory && 'player' in territory ? territory.player : null

	if (moveHint) {
		classes.push(`tile--${moveHint}`)
	}

	return (
		<button
			className={classes.join(' ')}
			style={colourPlayer != null ? { '--player': playerColour(colourPlayer) } as CSSProperties : undefined}
			onClick={onTileClicked}
			onMouseEnter={onTileEntered}
			aria-label={[
				isClaimed ? `Tile held by player ${owner}` : owner === DEAD ? 'Dead tile' : 'Unclaimed tile',
				!isClaimed && territory && territoryLabel(territory),
				moveHint && MOVE_HINT_LABEL[moveHint],
			].filter(Boolean).join(', ')}
		/>
	)
}
