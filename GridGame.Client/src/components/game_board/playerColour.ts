//One colour per seat - maps allow up to 8 players (see MapsController), and two seats sharing a colour would be ambiguous
const PALETTE_SIZE = 8

/** The CSS colour for a player's tiles (see Board.css), for anything that has to match them - swatches, legends */
export const playerColour = (player: number) => `var(--player-${((player - 1) % PALETTE_SIZE) + 1})`

/** `background` value for a swatch: the seat's gradient when it has one, else its plain colour */
export const playerSwatch = (player: number) => `var(--player-${((player - 1) % PALETTE_SIZE) + 1}-fill, ${playerColour(player)})`

/** Tile fill layer for a seat with a gradient (see seatColours.ts); `none` otherwise */
export const playerFill = (player: number) => `var(--player-${((player - 1) % PALETTE_SIZE) + 1}-fill, none)`
