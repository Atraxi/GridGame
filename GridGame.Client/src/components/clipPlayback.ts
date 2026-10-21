import { useEffect, useState } from "react"

//Shared by the home page carousel and the How to play page's clips

export const prefersReducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false

/** Whether the tab is showing - clips stop advancing while it's hidden */
export function usePageVisible() {
	const [visible, setVisible] = useState(() => document.visibilityState === 'visible')
	useEffect(() => {
		const onVisibilityChange = () => setVisible(document.visibilityState === 'visible')
		document.addEventListener('visibilitychange', onVisibilityChange)
		return () => document.removeEventListener('visibilitychange', onVisibilityChange)
	}, [])
	return visible
}
