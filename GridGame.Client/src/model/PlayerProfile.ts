export type PlayerProfile = {
	userName: string
	gamesPlayed: number
	gamesWon: number
	gamesLost: number
	colourPrimary: string | null
	colourSecondary: string | null
	useDefaultColours: boolean
	defaultPalette: { primary: string, secondary: string | null }[] | null
}
