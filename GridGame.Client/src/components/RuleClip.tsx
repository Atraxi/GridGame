import { useEffect, useRef, useState } from "react"
import Board from "./game_board/Board"
import type { Clip } from "../model/tutorial"
import { prefersReducedMotion, usePageVisible } from "./clipPlayback"
import "./RuleClip.css"

//The pictures only ever show players 1-4, and nothing in them depends on the real count
const PLAYER_COUNT = 4

/** One rule taken a step at a time, with the mover's legal moves highlighted and a caption per step. Plays while it's
 * on screen and loops; with reduced motion it waits to be stepped through by hand */
export default function RuleClip({ clip }: { clip: Clip }) {
	const [frameIndex, setFrameIndex] = useState(0)
	const [playing, setPlaying] = useState(() => !prefersReducedMotion())
	const [onScreen, setOnScreen] = useState(false)
	const pageVisible = usePageVisible()
	const ref = useRef<HTMLDivElement>(null)
	const frame = clip.frames[frameIndex]
	const last = clip.frames.length - 1

	useEffect(() => {
		const element = ref.current
		if (!element) {
			return
		}
		const observer = new IntersectionObserver(([entry]) => setOnScreen(entry.isIntersecting), { threshold: 0.4 })
		observer.observe(element)
		return () => observer.disconnect()
	}, [])

	useEffect(() => {
		if (!playing || !onScreen || !pageVisible) {
			return
		}
		//The last frame holds a little longer before the clip loops, so the outcome can be read
		const timer = setTimeout(
			() => setFrameIndex(index => index >= last ? 0 : index + 1),
			frameIndex >= last ? frame.holdMs + 1500 : frame.holdMs)
		return () => clearTimeout(timer)
	}, [playing, onScreen, pageVisible, frameIndex, frame.holdMs, last])

	return (
		<div className="rule-clip" ref={ref}>
			<Board
				board={frame.board}
				playerCount={PLAYER_COUNT}
				movesFor={frame.movesFor ?? null}
				jumpChoice={frame.jumpChoice ?? null}
				analysis={frame.territory}
				historyMarks={frame.marks}
			/>
			<p className="rule-clip__caption" aria-live={playing ? 'off' : 'polite'}>{frame.caption}</p>
			<div className="rule-clip__controls">
				<button type="button" onClick={() => setPlaying(current => !current)}>
					{playing ? 'Pause' : 'Play'}
				</button>
				<span className="rule-clip__step">Step {frameIndex + 1} of {clip.frames.length}</span>
				<button type="button" onClick={() => setFrameIndex(index => index >= last ? 0 : index + 1)}>
					{frameIndex >= last ? 'Restart' : 'Next step'}
				</button>
			</div>
		</div>
	)
}
