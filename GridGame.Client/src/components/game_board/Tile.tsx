export default function Tile({tileValue, onTileClicked}: {tileValue: number, onTileClicked: () => Promise<any> | undefined}) {
	return (
		<td onClick={onTileClicked}>
			Tile (TODO) {tileValue}
		</td>
	)
}