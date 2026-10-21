import { DEAD, UNCLAIMED } from "../components/game_board/Tile"
import type { JumpChoice, MarkedCell, TerritoryMap } from "../components/game_board/Board"
import { CellInsight } from "./analysis"
import { moveHintFor } from "./moves"

//Animated boards that teach the rules: step-by-step clips with move hints for the How to play page, and quick demos
//plus showcase games for the home page's carousel. Every clip is scripted as moves rather than drawn frame by frame,
//and each move is checked against moveHintFor (which mirrors the server's GridGameMoves) as the clip is built - so a
//clip can't quietly show a move the real game wouldn't allow

/** One still of a clip. Everything but the board and caption is an optional overlay, as on the real game board */
export type ClipFrame = {
	board: number[][],
	caption: string,
	/** Highlights this player's legal moves, as the game does on their turn */
	movesFor?: number,
	jumpChoice?: JumpChoice,
	marks?: MarkedCell[],
	territory?: TerritoryMap,
	/** How long the frame stays up before the next one */
	holdMs: number,
}

/** A run of frames. A clip-wide caption is for the quick demos, whose frames carry no caption of their own */
export type Clip = { title: string, caption?: string, frames: ClipFrame[] }

type Cell = [row: number, column: number]

/** A clip's script: show a board (with a player's move hints), make a move, or show who owns what */
type Step =
	| { show: string, movesFor?: number, territory?: boolean }
	| { player: number, to: Cell, from?: Cell, say: string }

const AIM_MS = 1400
const RESULT_MS = 2000
const SHOW_MS = 3200

/** '.' unclaimed, '#' dead, a digit a player's tile */
export function parseBoard(rows: string[]): number[][] {
	return rows.map(row => [...row].map(cell => cell === '.' ? UNCLAIMED : cell === '#' ? DEAD : Number(cell)))
}

const copy = (board: number[][]) => board.map(row => row.slice())

const playersOn = (board: number[][]) => [...new Set(board.flat().filter(cell => cell > 0))].sort()

const OFFSETS: Cell[] = [[-1, -1], [-1, 0], [-1, 1], [0, -1], [0, 1], [1, -1], [1, 0], [1, 1]]

/** Every unclaimed tile the player could ever claim, one move after another, ignoring what anyone else does. Only
 * the player's own tiles and empty ground they've reached are stepping stones - an enemy tile moved onto dies, it's
 * never taken. The same naive flood fill as the server's BoardAnalysis, which is plenty for a picture. With
 * `distances` of just [1], it's the ground the player can get to by spawning alone, without jumping anything */
function reachOf(board: number[][], player: number, distances = [1, 2]): Set<string> {
	const reached = new Set<string>()
	const frontier: Cell[] = []
	board.forEach((row, r) => row.forEach((cell, c) => { if (cell === player) frontier.push([r, c]) }))
	while (frontier.length > 0) {
		const [r, c] = frontier.pop()!
		for (const [dr, dc] of OFFSETS) {
			for (const distance of distances) {
				const [tr, tc] = [r + dr * distance, c + dc * distance]
				if (board[tr]?.[tc] === UNCLAIMED && !reached.has(`${tr},${tc}`)) {
					reached.add(`${tr},${tc}`)
					frontier.push([tr, tc])
				}
			}
		}
	}
	return reached
}

/** Unclaimed ground only one player can reach is theirs (tinted their colour), ground nobody can reach is grey, and
 * anything reachable by more than one is left plain - plus each player's score: tiles held + ground only they reach */
export function territoryOf(board: number[][]): { territory: TerritoryMap, scores: Map<number, number> } {
	const players = playersOn(board)
	const reaches = players.map(player => reachOf(board, player))
	const scores = new Map(players.map(player => [player, board.flat().filter(cell => cell === player).length]))
	const insight = board.map(row => row.map(() => CellInsight.None as number))
	const claimant = board.map(row => row.map(cell => Math.max(cell, 0)))
	board.forEach((row, r) => row.forEach((cell, c) => {
		if (cell !== UNCLAIMED) {
			return
		}
		const reachedBy = players.filter((_, index) => reaches[index].has(`${r},${c}`))
		if (reachedBy.length === 0) {
			insight[r][c] = CellInsight.Unreachable
		} else if (reachedBy.length === 1) {
			insight[r][c] = CellInsight.UncontestedSecure
			claimant[r][c] = reachedBy[0]
			scores.set(reachedBy[0], scores.get(reachedBy[0])! + 1)
		} else {
			insight[r][c] = CellInsight.Contested
		}
	}))
	return { territory: { insight, claimant }, scores }
}

/** True once no player could ever move onto another's tile - when the real game ends */
function isSealed(board: number[][]): boolean {
	return playersOn(board).every(player => {
		const stones: Cell[] = [...reachOf(board, player)].map(key => key.split(',').map(Number) as Cell)
		board.forEach((row, r) => row.forEach((cell, c) => { if (cell === player) stones.push([r, c]) }))
		return stones.every(([r, c]) => OFFSETS.every(([dr, dc]) =>
			[1, 2].every(distance => {
				const target = board[r + dr * distance]?.[c + dc * distance]
				return target === undefined || target <= 0 || target === player
			})))
	})
}

/** Applies one scripted move, after checking the real rules allow it: a spawn lands next to one of the mover's tiles,
 * a jump goes exactly two tiles in a straight or diagonal line from one of them (and the origin dies). Landing on
 * empty ground claims it; landing on an enemy kills it */
function applyMove(board: number[][], player: number, [r, c]: Cell, from?: Cell): number[][] {
	const hint = moveHintFor(board, player, r, c)
	const fail = (why: string) => { throw new Error(`Tutorial move by player ${player} to ${r},${c} ${why}`) }
	if (!from && hint !== 'spawn') {
		fail(`isn't a legal spawn (${hint ?? 'unreachable'})`)
	}
	if (from) {
		const [dr, dc] = [r - from[0], c - from[1]]
		const straightTwo = [0, 2].includes(Math.abs(dr)) && [0, 2].includes(Math.abs(dc)) && (dr !== 0 || dc !== 0)
		if (hint !== 'jump' || board[from[0]]?.[from[1]] !== player || !straightTwo) {
			fail(`from ${from} isn't a legal jump (${hint ?? 'unreachable'})`)
		}
	}
	const next = copy(board)
	next[r][c] = board[r][c] === UNCLAIMED ? player : DEAD
	if (from) {
		next[from[0]][from[1]] = DEAD
	}
	return next
}

function buildClip(title: string, start: string[], steps: Step[]): Clip {
	let board = parseBoard(start)
	const frames: ClipFrame[] = []
	for (const step of steps) {
		if ('show' in step) {
			const { territory, scores } = territoryOf(board)
			const scoreLine = step.territory
				? ` ${[...scores].map(([player, score]) => `Player ${player}: ${score}`).join(' · ')}.`
				: ''
			frames.push({
				board,
				caption: step.show + scoreLine,
				movesFor: step.movesFor,
				territory: step.territory ? territory : undefined,
				holdMs: SHOW_MS,
			})
			continue
		}
		const [r, c] = step.to
		frames.push({
			board,
			caption: step.say,
			movesFor: step.player,
			jumpChoice: step.from ? { x: r, y: c, origins: [{ x: step.from[0], y: step.from[1] }] } : undefined,
			marks: step.from ? undefined : [{ x: r, y: c, mark: 'landing' }],
			holdMs: AIM_MS,
		})
		board = applyMove(board, step.player, step.to, step.from)
		const marks: MarkedCell[] = [{ x: r, y: c, mark: 'landing' }]
		if (step.from) {
			marks.push({ x: step.from[0], y: step.from[1], mark: 'origin' })
		}
		frames.push({ board, caption: step.say, marks, holdMs: RESULT_MS })
	}
	return { title, frames }
}

/** The basic rules, one clip each, taken a step at a time with the mover's legal moves highlighted - for the How to
 * play page */
export const RULE_CLIPS: Clip[] = [
	buildClip('Spawn', [
		'.....',
		'.....',
		'..1..',
		'.....',
		'.....',
	], [
		{ show: 'Your tiles are the coloured blob. A + marks every empty tile touching yours - diagonals count. (The arcs are jumps - see the next rule.)', movesFor: 1 },
		{ player: 1, to: [1, 3], say: 'Each action claims one tile. That\'s one...' },
		{ player: 1, to: [2, 3], say: '...two...' },
		{ player: 1, to: [3, 1], say: '...three, and the turn passes. Most maps give 3 actions a turn.' },
	]),
	buildClip('Jump', [
		'.#...',
		'.#...',
		'1#...',
		'.#...',
		'.#...',
	], [
		{ show: 'An arc marks a jump: exactly two tiles in a straight or diagonal line, over anything in the way.', movesFor: 1 },
		{ player: 1, from: [2, 0], to: [2, 2], say: 'Jumping clears the dead tile - but the tile you jump from dies.' },
		{ player: 1, to: [1, 3], say: 'From the new tile, carry on as usual.' },
	]),
	buildClip('Attack', [
		'.....',
		'.11..',
		'..22.',
		'...2.',
		'.....',
	], [
		{ player: 1, to: [2, 2], say: 'Move onto an enemy tile and it dies. It doesn\'t become yours - it\'s dead ground now.' },
		{ player: 1, from: [1, 1], to: [3, 3], say: 'Jumps attack too, at the cost of the tile you jump from.' },
	]),
	buildClip('Walls', [
		'1#....',
		'######',
		'######',
		'2##...',
	], [
		{ show: 'One dead or enemy tile in the way can be jumped...', movesFor: 1 },
		{ player: 1, from: [0, 0], to: [0, 2], say: '...so player 1 gets through.' },
		{ show: 'Two in a row can\'t: no spawn or jump reaches past them. Player 2 is walled in for good.', movesFor: 2 },
	]),
	buildClip('Winning', [
		'11.##..22',
		'1..##...2',
		'.112#....',
		'...##.2..',
		'.1.##....',
		'...##..2.',
	], [
		{ show: 'Player 2\'s tile is the last gap in a two-thick wall.', movesFor: 1 },
		{ player: 1, to: [2, 3], say: 'Killing it seals the wall right across the board.' },
		{ show: 'Nobody can reach anyone else now, so the game is over.' },
		{ show: 'You score the tiles you hold plus the empty ground only you can reach.', territory: true },
	]),
]

const DEMO_START_MS = 700
//About two and a half actions a second
const DEMO_ACTION_MS = 400
const DEMO_END_MS = 1800
const SHOWCASE_ACTION_MS = 100
const SHOWCASE_END_MS = 3500

/** One action in a quick demo or showcase game: a spawn, or a jump when it has somewhere to jump from */
type Move = { player: number, to: Cell, from?: Cell }

const spawn = (player: number, row: number, column: number): Move => ({ player, to: [row, column] })
const jump = (player: number, from: Cell, to: Cell): Move => ({ player, from, to })

/** A frame per action. When marked, each shows where the action landed (and for a jump, the tile left dead) - at
 * showcase speed that would only flicker, so those go unmarked */
function framesFor(start: number[][], moves: Move[], actionMs: number, marked: boolean): ClipFrame[] {
	let board = start
	return moves.map(move => {
		board = applyMove(board, move.player, move.to, move.from)
		const marks: MarkedCell[] = [{ x: move.to[0], y: move.to[1], mark: 'landing' }]
		if (move.from) {
			marks.push({ x: move.from[0], y: move.from[1], mark: 'origin' })
		}
		return { board, caption: '', marks: marked ? marks : undefined, holdMs: actionMs }
	})
}

/** A quick demo: the moves played out at a steady clip with no move hints, under one caption. Optionally finishes
 * on the territory each player ends up with */
function buildDemo(title: string, caption: string, start: string[], moves: Move[], endWithTerritory = false): Clip {
	const first = parseBoard(start)
	const frames: ClipFrame[] = [{ board: first, caption: '', holdMs: DEMO_START_MS }, ...framesFor(first, moves, DEMO_ACTION_MS, true)]
	const last = frames[frames.length - 1]
	if (endWithTerritory) {
		frames.push({ board: last.board, caption: '', territory: territoryOf(last.board).territory, holdMs: DEMO_END_MS + 1500 })
	} else {
		last.holdMs = DEMO_END_MS
	}
	return { title, caption, frames }
}

/** The home page carousel's take on the rules: each one in a few seconds, without stopping to explain */
export const DEMO_CLIPS: Clip[] = [
	buildDemo('Spawn', 'Claim any empty tile touching one of yours - diagonals count.', [
		'......',
		'......',
		'..1...',
		'......',
		'......',
		'......',
	], [
		spawn(1, 1, 3), spawn(1, 2, 3), spawn(1, 3, 2),
		spawn(1, 3, 3), spawn(1, 1, 1), spawn(1, 4, 4),
		spawn(1, 2, 1), spawn(1, 0, 2), spawn(1, 4, 1),
	]),
	buildDemo('Jump', 'Jump exactly two tiles in a straight line, over anything. The tile you leave dies.', [
		'..#...',
		'..#...',
		'.1#...',
		'..#...',
		'..#...',
	], [
		spawn(1, 1, 1), spawn(1, 3, 1),
		jump(1, [3, 1], [3, 3]), jump(1, [1, 1], [1, 3]),
		spawn(1, 2, 3), spawn(1, 2, 4), spawn(1, 0, 4),
	]),
	buildDemo('Attack', 'Move onto an enemy tile and it dies - it never becomes yours.', [
		'......',
		'.11...',
		'.1.2..',
		'...22.',
		'....2.',
		'......',
	], [
		spawn(1, 2, 3), spawn(2, 2, 2), spawn(1, 2, 2),
		jump(2, [3, 3], [1, 1]), jump(1, [1, 2], [3, 4]), spawn(2, 5, 5),
	]),
	buildDemo('Walls', 'Two dead or enemy tiles in a row make a wall that nothing crosses.', [
		'1#.....',
		'#######',
		'#######',
		'2##....',
	], [
		jump(1, [0, 0], [0, 2]), spawn(1, 0, 3), spawn(1, 0, 4), spawn(1, 0, 5), spawn(1, 0, 6),
	]),
	buildDemo('Winning', 'Once nobody can reach anyone else, you score your tiles plus the ground only you can reach.', [
		'11.##..22',
		'1..##...2',
		'.112#....',
		'...##.2..',
		'.1.##....',
		'...##..2.',
	], [
		spawn(2, 1, 7), spawn(2, 4, 6), spawn(1, 1, 1), spawn(1, 3, 1), spawn(1, 2, 3),
	], true),
]

/** Small, seeded PRNG (mulberry32), so a showcase game plays out the same on every visit */
function seededRandom(seed: number) {
	return () => {
		seed |= 0
		seed = (seed + 0x6d2b79f5) | 0
		let t = Math.imul(seed ^ (seed >>> 15), 1 | seed)
		t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
		return ((t ^ (t >>> 14)) >>> 0) / 4294967296
	}
}

/** Plays a game out the way people tend to: everyone pushes towards their nearest enemy and kills any enemy tile they
 * can reach, so the ground between them fills with dead tiles - and once that's thick enough to be a wall, they keep
 * looking for a thin spot to jump over. Not a strong player, just one whose game looks like a real one. Every option
 * gets a score and the pick is weighted towards the best, with enough chance in it that it doesn't look scripted */
function simulate(start: number[][], rounds: number, actionsPerTurn: number, seed: number): Move[] {
	const random = seededRandom(seed)
	let board = start
	const players = playersOn(board)
	const moves: Move[] = []
	for (let round = 0; round < rounds; round++) {
		for (const player of players) {
			for (let action = 0; action < actionsPerTurn; action++) {
				const enemies: Cell[] = []
				board.forEach((row, r) => row.forEach((cell, c) => { if (cell > 0 && cell !== player) enemies.push([r, c]) }))
				//Moves, not steps: a spawn covers a diagonal as readily as a straight line
				const toEnemy = (r: number, c: number) =>
					Math.min(...enemies.map(([er, ec]) => Math.max(Math.abs(r - er), Math.abs(c - ec))))
				const walkable = reachOf(board, player, [1])
				const options: (Move & { score: number })[] = []
				board.forEach((row, r) => row.forEach((cell, c) => {
					const hint = moveHintFor(board, player, r, c)
					const attack = cell > 0
					if (hint === 'spawn') {
						//Pushing forward along a solid front rather than in a thin line
						const backing = OFFSETS.filter(([dr, dc]) => board[r + dr]?.[c + dc] === player).length
						options.push({ player, to: [r, c], score: attack ? 6 : 0.3 * backing - toEnemy(r, c) })
					} else if (hint === 'jump') {
						//Of the tiles it could jump from, give up the one furthest from the fighting
						const from = OFFSETS
							.map(([dr, dc]) => [r - dr * 2, c - dc * 2] as Cell)
							.filter(([fr, fc]) => board[fr]?.[fc] === player)
							.sort((a, b) => toEnemy(...b) - toEnemy(...a))[0]
						//A jump costs a tile, so it's worth it to get past a wall - onto ground spawning could never get to -
						//but not just to spread. A kill by jumping is worth less than one by spawning, for the same reason
						const crossesWall = !attack && !walkable.has(`${r},${c}`)
						options.push({ player, to: [r, c], from, score: attack ? 0.5 : crossesWall ? 2.5 - toEnemy(r, c) : -4 - toEnemy(r, c) })
					}
				}))
				if (options.length === 0 || enemies.length === 0) {
					break
				}
				const weights = options.map(option => Math.exp(option.score * 1.5))
				let pick = random() * weights.reduce((sum, weight) => sum + weight, 0)
				const chosen = options.find((_, index) => (pick -= weights[index]) < 0) ?? options[options.length - 1]
				moves.push({ player, to: chosen.to, from: chosen.from })
				board = applyMove(board, player, chosen.to, chosen.from)
			}
		}
	}
	return moves
}

/** Moves that grow each player outwards from a seed tile until they hold every tile they do in `target`, taking turns
 * a few actions at a time. Breadth-first, so every move is a spawn next to a tile already taken */
function fillTowards(target: number[][], seeds: Cell[], actionsPerTurn: number): { start: number[][], moves: Move[] } {
	const isSeed = (r: number, c: number) => seeds.some(([sr, sc]) => sr === r && sc === c)
	const start = target.map((row, r) => row.map((cell, c) => cell > 0 && !isSeed(r, c) ? UNCLAIMED : cell))
	const queues = seeds.map(([sr, sc]) => {
		const player = target[sr][sc]
		const order: Cell[] = []
		const seen = new Set([`${sr},${sc}`])
		const frontier: Cell[] = [[sr, sc]]
		while (frontier.length > 0) {
			const [r, c] = frontier.shift()!
			for (const [dr, dc] of OFFSETS) {
				const [nr, nc] = [r + dr, c + dc]
				if (target[nr]?.[nc] === player && !seen.has(`${nr},${nc}`)) {
					seen.add(`${nr},${nc}`)
					order.push([nr, nc])
					frontier.push([nr, nc])
				}
			}
		}
		return order.map(([r, c]) => spawn(player, r, c))
	})
	const moves: Move[] = []
	while (queues.some(queue => queue.length > 0)) {
		queues.forEach(queue => moves.push(...queue.splice(0, actionsPerTurn)))
	}
	return { start, moves }
}

/** A showcase game played out fast, then held on how it ended up */
function buildShowcase(title: string, caption: string, start: number[][], moves: Move[], endWithTerritory: boolean): Clip {
	const frames: ClipFrame[] = [{ board: start, caption: '', holdMs: DEMO_START_MS }, ...framesFor(start, moves, SHOWCASE_ACTION_MS, false)]
	const last = frames[frames.length - 1]
	if (endWithTerritory) {
		frames.push({ board: last.board, caption: '', territory: territoryOf(last.board).territory, holdMs: SHOWCASE_END_MS })
	} else {
		last.holdMs = SHOWCASE_END_MS
	}
	return { title, caption, frames }
}

const skirmishStart = parseBoard([
	'11........44',
	'1..........4',
	'............',
	'.....##.....',
	'....#..#....',
	'............',
	'............',
	'....#..#....',
	'.....##.....',
	'............',
	'3..........2',
	'33........22',
])

const endgame = parseBoard([
	'1111.1##22.2222',
	'11.111##2222.22',
	'1.1#11##22#2222',
	'1111.1##222.222',
	'111111#########',
	'1.1111#########',
	'111.11##3333.33',
	'11#111##33#3333',
	'1111.1##333.333',
])
if (import.meta.env.DEV && !isSealed(endgame)) {
	throw new Error('The endgame showcase board is meant to be sealed off')
}
const carvedUp = fillTowards(endgame, [[4, 0], [0, 14], [8, 14]], 3)

/** Whole games in fast-forward, for the end of the home page carousel */
export const SHOWCASE_CLIPS: Clip[] = [
	buildShowcase('Four-way skirmish', 'Four players push at their neighbours and kill what they meet. The dead ground left behind becomes a wall they probe for gaps.',
		skirmishStart, simulate(skirmishStart, 8, 3, 1), false),
	buildShowcase('Carved up', 'Three players fill their walled-off corners. The tinted gaps left over are territory only their owner can claim.',
		carvedUp.start, carvedUp.moves, true),
]
