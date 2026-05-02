import type { GridGame } from "../../model/GridGame";
import Tile from "./Tile";

export default function Board({ gameState }: { gameState: GridGame }) {
	return (
		<>
			Board (TODO)
			{gameState.gameBoard.map((row, rowIndex) => 
				row.map((tile, tileIndex) => {
					{console.log(`${rowIndex}.${tileIndex}`)}
					return <Tile key={`${rowIndex}.${tileIndex}`} />
				})
			)}
		</>
	)
}