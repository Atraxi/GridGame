import type { GridGame } from "../../model/GridGame";
import Tile from "./Tile";

export default function Board({ gameState, onTileClicked }: { gameState: GridGame, onTileClicked: (x: number, y: number) => () => Promise<any> | undefined }) {
	return (
		<>
			Board (TODO)
			<table>
			{gameState.gameBoard.map((row, rowIndex) => 
				<tr>
					{row.map((tile, tileIndex) => {
						return <Tile key={`${rowIndex}.${tileIndex}`} tileValue={gameState.gameBoard[rowIndex][tileIndex]} onTileClicked={onTileClicked(rowIndex, tileIndex)} />
					})}
				</tr>
			)}
			</table>
		</>
	)
}