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
            .configureLogging(signalR.LogLevel.Information)
            .build()

		setConnection(connection)

        connection.start()
            .then(() => console.log('Connection started'))
            .catch(err => console.log('Error while starting connection: ' + err))

        connection.on('broadcastMessage', (receivedMessage) => {
					console.log("SignalR broadcast message recieved")
					console.log(receivedMessage)
          //TODO handle message  
					//doSomething(receivedMessage)
        });

				return () => {
            if (connection) {
                connection.stop();
            }
        };
    }, [])

    sendMovePerformed: (x: number, y: number) => {
        connection?.invoke("movePerformed", {x: x, y: y})
    }

	return (
		<>
			<h1>GridGame</h1>
            <p>Game name: {gameState.name}</p>
			<Board gameState={gameState} />
		</>
	)
}