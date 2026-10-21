import { useEffect, useRef, useState } from "react"
import { Link } from "react-router"
import Board from "./game_board/Board"
import { DEMO_CLIPS, SHOWCASE_CLIPS, type Clip } from "../model/tutorial"
import { prefersReducedMotion, usePageVisible } from "./clipPlayback"
import "./RulesCarousel.css"

const SLIDES: Clip[] = [...DEMO_CLIPS, ...SHOWCASE_CLIPS]

//The pictures only ever show players 1-4, and nothing in them depends on the real count
const PLAYER_COUNT = 4

/** Plays one slide's clip through while it's the slide showing, then hands over to the next slide */
function DemoPlayer({ clip, active, playing, onFinished }: { clip: Clip, active: boolean, playing: boolean, onFinished: () => void }) {
	const [frameIndex, setFrameIndex] = useState(0)
	const pageVisible = usePageVisible()
	const frame = clip.frames[frameIndex]
	const last = clip.frames.length - 1

	//Coming back to a slide starts its clip from the top. Done while rendering rather than in an effect, so the clip
	//never flashes the frame it was left on first
	const [wasActive, setWasActive] = useState(active)
	if (active !== wasActive) {
		setWasActive(active)
		if (active) {
			setFrameIndex(0)
		}
	}

	//Read through a ref, so a new callback from the parent doesn't restart the current frame's timer
	const onFinishedRef = useRef(onFinished)
	useEffect(() => { onFinishedRef.current = onFinished }, [onFinished])

	useEffect(() => {
		if (!active || !playing || !pageVisible) {
			return
		}
		const timer = setTimeout(
			() => frameIndex >= last ? onFinishedRef.current() : setFrameIndex(frameIndex + 1),
			frame.holdMs)
		return () => clearTimeout(timer)
	}, [active, playing, pageVisible, frameIndex, frame.holdMs, last])

	return (
		<Board
			board={frame.board}
			playerCount={PLAYER_COUNT}
			movesFor={null}
			jumpChoice={null}
			analysis={frame.territory}
			historyMarks={frame.marks}
		/>
	)
}

/** The home page's carousel: a few seconds' demo of each basic rule, then a couple of whole games in fast-forward,
 * moving on to the next slide as each finishes. A row of scroll-snapped slides, so a swipe moves between them natively
 * on a touch screen; the buttons and dots do the same for a mouse or keyboard. The full rules, taken slowly, are on
 * the How to play page */
export default function RulesCarousel() {
	const trackRef = useRef<HTMLDivElement>(null)
	const [current, setCurrent] = useState(0)
	//With reduced motion, nothing moves until asked to
	const [playing, setPlaying] = useState(() => !prefersReducedMotion())

	const goTo = (index: number) => {
		const track = trackRef.current
		if (!track) {
			return
		}
		const target = (index + SLIDES.length) % SLIDES.length
		track.scrollTo({ left: target * track.clientWidth, behavior: prefersReducedMotion() ? 'auto' : 'smooth' })
		setCurrent(target)
	}

	//Swipes and scroll-snapping move the track without going through goTo, so the current slide is read back off it
	const onScroll = () => {
		const track = trackRef.current
		if (track && track.clientWidth > 0) {
			setCurrent(Math.round(track.scrollLeft / track.clientWidth))
		}
	}

	return (
		<section className="rules-carousel" aria-roledescription="carousel" aria-label="How to play">
			<div className="rules-carousel__track" ref={trackRef} onScroll={onScroll}>
				{SLIDES.map((clip, index) => (
					<div
						key={clip.title}
						className="rules-carousel__slide"
						role="group"
						aria-roledescription="slide"
						aria-label={`${index + 1} of ${SLIDES.length}: ${clip.title}`}
						inert={index !== current}
					>
						<h3 className="rules-carousel__title">{clip.title}</h3>
						<DemoPlayer clip={clip} active={index === current} playing={playing} onFinished={() => goTo(index + 1)} />
						<p className="rules-carousel__caption">{clip.caption}</p>
					</div>
				))}
			</div>
			<div className="rules-carousel__nav">
				<button type="button" onClick={() => goTo(current - 1)} aria-label="Previous slide">‹</button>
				<div className="rules-carousel__dots">
					{SLIDES.map((clip, index) => (
						<button
							key={clip.title}
							type="button"
							className="rules-carousel__dot"
							aria-label={`Go to slide ${index + 1}: ${clip.title}`}
							aria-current={index === current}
							onClick={() => goTo(index)}
						/>
					))}
				</div>
				<button type="button" onClick={() => goTo(current + 1)} aria-label="Next slide">›</button>
			</div>
			<p className="rules-carousel__footer">
				<button type="button" className="rules-carousel__pause" onClick={() => setPlaying(current => !current)}>
					{playing ? 'Pause' : 'Play'}
				</button>
				<Link to="/help">How to play: the full rules</Link>
			</p>
		</section>
	)
}
