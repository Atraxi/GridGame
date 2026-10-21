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
				GridGame is a turn-based game of territory. Spread across the board, cut your rivals off, and hold the most
				ground when nobody can reach anybody any more.
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
					Players take turns in order. On your turn you get a fixed number of actions - three on most maps, though
					each map sets its own - and every action is either a spawn or a jump. When your actions are used up, play
					passes to the next player. A player with no legal move left is skipped.
				</p>
			</section>

			<section id="spawn">
				<h2>Spawning</h2>
				<p>
					Claim any empty tile touching one of yours, in any of the eight directions: across, up, down or
					diagonally. On your turn, every tile you could spawn onto is marked with a +.
				</p>
				<RuleClip clip={clip('Spawn')} />
			</section>

			<section id="jump">
				<h2>Jumping</h2>
				<p>
					Move exactly two tiles in a straight line from one of your tiles - across, up, down or diagonally. What's
					in between doesn't matter: dead ground, enemy tiles and empty tiles can all be jumped. The catch is that
					the tile you jump from dies. On your turn, every tile you could jump to is marked with an arc.
				</p>
				<p>
					If more than one of your tiles could make the same jump, you're asked which one to use: arrows point from
					each candidate to the landing tile, and the rest of the board dims until you pick.
				</p>
				<RuleClip clip={clip('Jump')} />
			</section>

			<section id="attack">
				<h2>Attacking</h2>
				<p>
					Spawning or jumping onto an enemy tile kills it. It turns into dead ground - it doesn't become yours - and
					dead ground can never be claimed again. Jumping onto an enemy still costs you the tile you jumped from.
				</p>
				<RuleClip clip={clip('Attack')} />
			</section>

			<section id="walls">
				<h2>Walls</h2>
				<p>
					A jump only covers two tiles, so two dead or enemy tiles in a row stop everything: no spawn reaches past
					them and no jump clears them. A single tile isn't a wall - it can be jumped. You can still attack enemy
					tiles in a wall, but killing them only turns the wall into dead ground; it never gets you through.
				</p>
				<RuleClip clip={clip('Walls')} />
			</section>

			<section id="ending">
				<h2>How a game ends</h2>
				<p>
					The game is over once no player can reach any other. Every empty tile left can then be reached by one
					player at most, so the board is settled. Each player scores the tiles they hold plus the empty tiles only
					they can reach. The highest score wins, and a tie is shared.
				</p>
				<RuleClip clip={clip('Winning')} />
			</section>

			<section id="resigning">
				<h2>Resigning</h2>
				<p>
					When the board shows the game is decided in another player's favour - their likely score beats the best
					anyone else could still manage - you're offered the chance to resign. Resigning ends the game for
					everyone, scored as the board stands: tiles more than one player could still reach count for nobody, and
					the player who resigned can't win.
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
					Rings in an opponent's colour show where they could move next: dotted for a spawn, solid for a jump.
					Watch the solid ones - jumps are the moves that cross walls. You can turn these off on the game page.
				</p>
				<p className="move-legend help-page__legend">
					<span><span className="move-legend__swatch move-legend__swatch--opponent-spawn" />Opponent can spawn</span>
					<span><span className="move-legend__swatch move-legend__swatch--opponent-jump" />Opponent can jump</span>
				</p>
				<p>
					With a named account, empty ground is also tinted to show who it's heading for. From left to right below:
					one of your tiles, ground only you can reach, ground only you can reach but where you could still be cut
					off, ground nobody can reach any more, and dead ground.
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
					Named accounts also get a table of live statistics under the board - what each player holds, what they're
					projected to finish with, and the most they could still reach - and a move history that replays the game
					one action at a time.
				</p>
			</section>

			<section id="touch">
				<h2>Phones and tablets</h2>
				<p>
					Tap a tile once to pick it, and a bar at the bottom of the screen asks you to confirm. Tap the same tile
					again, or press the bar's button, to make the move - tap anywhere else to pick a different tile instead.
					Moves can't be taken back, so this guards against a slip of the finger. Big boards shrink to fit the
					screen; pinch to zoom in on one.
				</p>
			</section>

			<section id="accounts">
				<h2>Seats and accounts</h2>
				<p>
					Opening a game with a free seat takes it, and you play as that seat's player. Seats are only held while
					someone is in the game: once everyone has left, a seat that isn't locked goes to whoever opens the game
					first next time. Press <strong>Lock my seat</strong> to tie yours to your account for good.
				</p>
				<p>
					You can join games as a guest. A free named account also lets you host games, design maps, lock your
					seat, see the live statistics, final scores and move history, and keep a list of the players you've played
					with.
				</p>
				<p>
					When hosting, choose <strong>Only players I pick</strong> to keep the seats for named players you've played
					with before. Anyone else can still watch. You can tidy that list up on your{' '}
					<Link to="/account">account page</Link>.
				</p>
			</section>

			<p className="help-page__back"><Link to="/">Back to the games</Link></p>
		</article>
	)
}
