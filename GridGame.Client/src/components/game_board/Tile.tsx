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

export default function Tile({ owner, edges, moveHint, onTileClicked, onTileEntered }: {
	owner: number, edges: Edges, moveHint?: MoveHint,
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
	}

	if (moveHint) {
		classes.push(`tile--${moveHint}`)
	}

	return (
		<button
			className={classes.join(' ')}
			style={isClaimed ? { '--player': `var(--player-${((owner - 1) % PALETTE_SIZE) + 1})` } as CSSProperties : undefined}
			onClick={onTileClicked}
			onMouseEnter={onTileEntered}
			aria-label={[
				isClaimed ? `Tile held by player ${owner}` : owner === DEAD ? 'Dead tile' : 'Unclaimed tile',
				moveHint && MOVE_HINT_LABEL[moveHint],
			].filter(Boolean).join(', ')}
		/>
	)
}
