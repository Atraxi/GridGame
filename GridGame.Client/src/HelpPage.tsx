import { Link } from "react-router"
import Board from "./components/game_board/Board"
import RuleClip from "./components/RuleClip"
import { CellInsight } from "./model/analysis"
import { RULE_CLIPS } from "./model/tutorial"
import "./GridGame.css"
import "./HelpPage.css"

const clip = (title: string) => RULE_CLIPS.find(candidate => candidate.title === title)!

//A row of real board tiles, drawn by the same code as the game, so the key always matches what's on the board:
//a player 1 tile, ground only they can reach (secure, then at risk), ground nobody can reach, and dead ground
const TERRITORY_KEY = {
	board: [[1, 0, 0, 0, -1]],
	analysis: {
		insight: [[CellInsight.None, CellInsight.UncontestedSecure, CellInsight.UncontestedAtRisk, CellInsight.Unreachable, CellInsight.None]],
		claimant: [[1, 1, 1, 0, 0]],
	},
}

/** The full rules, a section at a time, each basic rule with a step-by-step clip. The home page carousel only skims them */
export default function HelpPage() {
	return (
		<article className="help-page">
			<h1>How to play</h1>
			<p className="help-page__lead">
				A turn-based territory game. Spread out, cut your rivals off, and hold the most ground.
			</p>

			<nav className="help-page__contents" aria-label="On this page">
				<a href="#turns">Turns</a>
				<a href="#spawn">Spawning</a>
				<a href="#jump">Jumping</a>
				<a href="#attack">Attacking</a>
				<a href="#walls">Walls</a>
				<a href="#ending">How a game ends</a>
				<a href="#resigning">Resigning</a>
				<a href="#reading">Reading the board</a>
				<a href="#touch">Phones and tablets</a>
				<a href="#accounts">Seats and accounts</a>
			</nav>

			<section id="turns">
				<h2>Turns</h2>
				<p>
					Players take turns. Each turn you get a set number of actions (usually three), each a spawn or a jump. A player with no legal move is skipped.
				</p>
			</section>

			<section id="spawn">
				<h2>Spawning</h2>
				<p>
					Claim any empty tile touching one of yours, diagonals included. Spawnable tiles are marked +.
				</p>
				<RuleClip clip={clip('Spawn')} />
			</section>

			<section id="jump">
				<h2>Jumping</h2>
				<p>
					Move exactly two tiles in a straight line, diagonals included. Whatever is in between can be jumped. The catch: the tile you jump from dies. Jumpable tiles are marked with an arc.
				</p>
				<p>
					If several of your tiles can make the same jump, you pick which one.
				</p>
				<RuleClip clip={clip('Jump')} />
			</section>

			<section id="attack">
				<h2>Attacking</h2>
				<p>
					Spawning or jumping onto an enemy tile kills it. It becomes dead ground, which can never be claimed. A jump still costs you the tile you left.
				</p>
				<RuleClip clip={clip('Attack')} />
			</section>

			<section id="walls">
				<h2>Walls</h2>
				<p>
					Two dead or enemy tiles in a row block everything: no spawn passes and no jump clears them. A single tile can be jumped. Killing enemy wall tiles only turns them into dead ground.
				</p>
				<RuleClip clip={clip('Walls')} />
			</section>

			<section id="ending">
				<h2>How a game ends</h2>
				<p>
					The game ends when no player can reach any other. Each player scores their tiles plus the empty tiles only they can reach. Highest score wins; ties are shared.
				</p>
				<RuleClip clip={clip('Winning')} />
			</section>

			<section id="resigning">
				<h2>Resigning</h2>
				<p>
					When another player's likely score beats the best anyone else could reach, you're offered a resignation. It ends the game for everyone, scored as it stands (contested tiles count for nobody). The resigner can't win.
				</p>
			</section>

			<section id="reading">
				<h2>Reading the board</h2>
				<p>On your turn, your own moves are marked:</p>
				<p className="move-legend help-page__legend">
					<span><span className="move-legend__swatch move-legend__swatch--spawn" />You can spawn</span>
					<span><span className="move-legend__swatch move-legend__swatch--jump" />You can jump</span>
				</p>
				<p>
					Rings in an opponent's colour show their next possible moves: dotted for a spawn, solid for a jump (jumps cross walls). Toggle them on the game page.
				</p>
				<p className="move-legend help-page__legend">
					<span><span className="move-legend__swatch move-legend__swatch--opponent-spawn" />Opponent can spawn</span>
					<span><span className="move-legend__swatch move-legend__swatch--opponent-jump" />Opponent can jump</span>
				</p>
				<p>
					Empty ground is also tinted by who can reach it. Left to right: your tile, only you can reach, only you but could be cut off, nobody can reach, dead.
				</p>
				<div className="help-page__key">
					<Board
						board={TERRITORY_KEY.board}
						playerCount={1}
						movesFor={null}
						jumpChoice={null}
						analysis={TERRITORY_KEY.analysis}
					/>
				</div>
				<p>
					Named accounts also get live statistics and a move history that replays the game.
				</p>
			</section>

			<section id="touch">
				<h2>Phones and tablets</h2>
				<p>
					Tap a tile to pick it, then tap it again (or press the bar at the bottom) to confirm. Moves can't be undone. Pinch to zoom big boards.
				</p>
			</section>

			<section id="accounts">
				<h2>Seats and accounts</h2>
				<p>
					Opening a game with a free seat takes it. Once everyone leaves, unlocked seats go to whoever opens the game next. Press <strong>Lock my seat</strong> to keep yours.
				</p>
				<p>
					Guests can join games. A named account can also host, design maps, lock seats, and see statistics, final scores and history.
				</p>
				<p>
					Hosting <strong>Only players I pick</strong> reserves seats for past opponents; others can still watch. Manage that list on your{' '}
					<Link to="/account">account page</Link>.
				</p>
			</section>

			<p className="help-page__back"><Link to="/">Back to the games</Link></p>
		</article>
	)
}
