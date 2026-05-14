import { useEffect, useState } from "react";
import Board from "./components/game_board/Board";
import * as signalR from '@microsoft/signalr';
import { useLoaderData } from "react-router";
import type { GridGame } from "./model/GridGame";

export default function GridGamePage() {
	const [connection, setConnection] = useState<signalR.HubConnection | null>(null);
    const [gameState, setGameState] = useState(useLoaderData<GridGame>())

	useEffect(() => {
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("https://localhost:7061/GridGame")
            .configureLogging(signalR.LogLevel.Debug)
            .build()

		setConnection(connection)

        connection.start()
            .then(() => {
                console.log('Connection started')
                connection.invoke("onConnectedToGame", gameState.id, 1 )
            })
            .catch(err => console.log('Error while starting connection: ' + err))

        connection.on('tileUpdateProcessed', (receivedMessage: {x: number, y: number, newTileValue: number}) => {
            setGameState(previousState => { 
                return {...previousState, gameBoard: previousState.gameBoard.map((row, columnIndex) => {
                    if(columnIndex == receivedMessage.x) {
                        return row.map((tile, rowIndex) => {
                            if(rowIndex == receivedMessage.y) {
                                return receivedMessage.newTileValue
                            } else {
                                return tile
                            }
                        })
                    } else {
                        return row
                    }
                })}
            })
        })

		return () => {
            if (connection) {
                connection.stop();
            }
        }
    }, [])

    let onTileClicked = (x: number, y: number) => 
        () => connection?.invoke("onTileClicked", x, y)
    

	return (
		<>
			<h1>GridGame</h1>
            <p>Game name: {gameState.name}</p>
			<Board gameState={gameState} onTileClicked={ onTileClicked } />
		</>
	)
}