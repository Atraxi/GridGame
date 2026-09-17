import { useEffect, useState } from "react";
import Board from "./components/game_board/Board";
import * as signalR from '@microsoft/signalr';
import { useLoaderData } from "react-router";
import type { GridGame } from "./model/GridGame";
import { authorizedFetch, ensureToken, getTokenExpiryMs, isGuest } from "./auth";
import "./GridGame.css";

type TileUpdate = { x: number, y: number, newTileValue: number }
type JumpChoice = { x: number, y: number, origins: { x: number, y: number }[] }
type MoveApplied = {
    updates: TileUpdate[],
    currentPlayerNumber: number,
    actionsRemainingInTurn: number,
    turnNumber: number,
    isGameOver: boolean,
}
type MoveRejected = { x: number, y: number, reason: 'spectating' | 'notYourTurn' | 'unreachable' | 'gameOver' }
type ScorePrediction = { predictedScores: number[], contestedCells: number }

const REJECTION_MESSAGES: Record<MoveRejected['reason'], string> = {
    spectating: "This game's seats are full - you're only spectating.",
    notYourTurn: "It's not your turn.",
    unreachable: "That tile can't be reached from any of your tiles.",
    gameOver: "This game has already ended.",
}

export default function GridGamePage() {
	const [connection, setConnection] = useState<signalR.HubConnection | null>(null);
    const [gameState, setGameState] = useState(useLoaderData<GridGame>())
    const [myPlayerNumber, setMyPlayerNumber] = useState<number | null>(null)
    const [jumpChoice, setJumpChoice] = useState<JumpChoice | null>(null)
    const [statusMessage, setStatusMessage] = useState<string | null>(null)
    const [finalScores, setFinalScores] = useState<number[] | null>(null)
    //Only ever arrives for named accounts (see GridGameHub.BroadcastScorePrediction) - a guest simply never gets
    //this message, so there's nothing to gate client-side
    const [scorePrediction, setScorePrediction] = useState<ScorePrediction | null>(null)

    const isMyTurn = myPlayerNumber != null && myPlayerNumber === gameState.currentPlayerNumber

    //Final scores are a named-account-only feature - GridGame.FinalScores is left off every other response, so
    //this is the only place it's ever fetched from
    useEffect(() => {
        if (!gameState.isGameOver || isGuest()) {
            return
        }
        let cancelled = false
        authorizedFetch(`/Games/GetFinalScores?gameId=${gameState.id}`)
            .then(response => response.ok ? response.json() : null)
            .then(scores => { if (!cancelled) setFinalScores(scores) })
            .catch(() => { if (!cancelled) setFinalScores(null) })
        return () => { cancelled = true }
    }, [gameState.isGameOver, gameState.id])

	useEffect(() => {
        let cancelled = false
        let activeConnection: signalR.HubConnection | null = null
        let rotateTimer: ReturnType<typeof setTimeout> | null = null

        const attachHandlers = (hub: signalR.HubConnection) => {
            hub.on('tileUpdateProcessed', (result: MoveApplied) => {
                setJumpChoice(null)
                setStatusMessage(null)
                setGameState(previousState => {
                    const board = previousState.gameBoard.map(row => row.slice())
                    result.updates.forEach(({ x, y, newTileValue }) => { board[x][y] = newTileValue })
                    return {
                        ...previousState,
                        gameBoard: board,
                        currentPlayerNumber: result.currentPlayerNumber,
                        actionsRemainingInTurn: result.actionsRemainingInTurn,
                        turnNumber: result.turnNumber,
                        isGameOver: result.isGameOver,
                    }
                })
            })

            hub.on('scorePredictionUpdated', (prediction: ScorePrediction) => {
                setScorePrediction(prediction)
            })

            hub.on('moveRejected', (rejection: MoveRejected) => {
                setJumpChoice(null)
                setStatusMessage(REJECTION_MESSAGES[rejection.reason] ?? "That move isn't allowed.")
            })

            hub.on('chooseJumpOrigin', (choice: JumpChoice) => {
                setStatusMessage(null)
                setJumpChoice(choice)
            })

            //A network blip keeps the same connection object once SignalR reconnects it, but the server has
            //forgotten this connection id entirely - it needs to be told whose game this is again
            hub.onreconnecting(() => setStatusMessage('Connection lost, reconnecting...'))
            hub.onreconnected(() => {
                setStatusMessage(null)
                hub.invoke<number>("onConnectedToGame", gameState.id).then(setMyPlayerNumber)
            })
            //SignalR calls onclose for every close, including our own intentional hub.stop() calls (the
            //StrictMode-discarded first connection in dev, or the old connection during a proactive rotation) -
            //it only passes an error when the close was actually abnormal, so that's what distinguishes a real
            //problem from a routine teardown
            hub.onclose(error => {
                if (error) {
                    setStatusMessage('Disconnected - refresh the page to reconnect.')
                }
            })
        }

        const connect = async (): Promise<signalR.HubConnection | null> => {
            const hub = new signalR.HubConnectionBuilder()
                //Relative, so the hub is found wherever the app is served from: through the Vite proxy in
                //development, and on the app's own origin once the client is published into the API's wwwroot
                .withUrl("/GridGame", { accessTokenFactory: ensureToken })
                .withAutomaticReconnect()
                .configureLogging(signalR.LogLevel.Debug)
                .build()

            attachHandlers(hub)

            await hub.start()
            if (cancelled) {
                await hub.stop()
                return null
            }

            const playerNumber = await hub.invoke<number>("onConnectedToGame", gameState.id)
            if (cancelled) {
                await hub.stop()
                return null
            }

            setMyPlayerNumber(playerNumber)
            return hub
        }

        //Proactively cycles the connection onto a fresh access token shortly before the current one expires, so a
        //long-running session doesn't outlive it and get silently rejected on its next hub call
        const scheduleRotation = async (hub: signalR.HubConnection) => {
            const token = await ensureToken()
            const delay = Math.max(getTokenExpiryMs(token) - Date.now() - 60_000, 5_000)

            rotateTimer = setTimeout(async () => {
                if (cancelled) {
                    return
                }
                //The replacement connects and registers itself with the server BEFORE the old one closes - the hub
                //tears down a game's whole session when its connection count hits zero, so a solo player must never
                //have zero connections registered in between, or they could come back reassigned a different player number
                const next = await connect()
                if (!next) {
                    return
                }
                await hub.stop()
                activeConnection = next
                setConnection(next)
                scheduleRotation(next)
            }, delay)
        }

        connect()
            .then(hub => {
                if (!hub) {
                    return
                }
                activeConnection = hub
                setConnection(hub)
                scheduleRotation(hub)
            })
            .catch(err => console.log('Error while starting connection: ' + err))

		return () => {
            cancelled = true
            if (rotateTimer) {
                clearTimeout(rotateTimer)
            }
            activeConnection?.stop()
        }
    }, [])

    const onTileClicked = (x: number, y: number) => () => {
        if (!connection || gameState.isGameOver) {
            return
        }
        if (jumpChoice) {
            const isOffered = jumpChoice.origins.some(origin => origin.x === x && origin.y === y)
            if (isOffered) {
                return connection.invoke("confirmJump", x, y, jumpChoice.x, jumpChoice.y)
            }
            //Clicking anywhere else cancels the pending choice instead of leaving it stuck
            setJumpChoice(null)
            return
        }
        if (!isMyTurn) {
            return
        }
        return connection.invoke("requestMove", x, y)
    }

	return (
		<>
			<h1>GridGame</h1>
            <p>Game name: {gameState.name}</p>
            <p>
                Click an unclaimed or enemy tile next to one of yours to spawn onto it, or two tiles away
                (in a straight or diagonal line) to jump onto it - jumping leaves the tile you came from dead.
            </p>
            <p>
                {gameState.isGameOver
                    ? "Game over - no player can reach any other anymore."
                    : isMyTurn
                        ? `Your turn - ${gameState.actionsRemainingInTurn} action${gameState.actionsRemainingInTurn === 1 ? '' : 's'} left.`
                        : `Waiting on player ${gameState.currentPlayerNumber}...`}
            </p>
            {gameState.isGameOver && (
                <p>
                    {finalScores
                        ? `Final scores: ${finalScores.map((score, index) => `Player ${index + 1}: ${score} tiles`).join(', ')}`
                        : isGuest()
                            ? "Create a named account to see final scores."
                            : "Loading final scores..."}
                </p>
            )}
            {!gameState.isGameOver && scorePrediction && (
                <p className="score-prediction">
                    If play continued as-is: {scorePrediction.predictedScores.map((score, index) => `Player ${index + 1}: ${score}`).join(', ')}
                    {scorePrediction.contestedCells > 0 && ` (${scorePrediction.contestedCells} tile${scorePrediction.contestedCells === 1 ? '' : 's'} still contested)`}
                </p>
            )}
            {isMyTurn && <p className="move-legend">
                <span><span className="move-legend__swatch move-legend__swatch--spawn" />Spawn target</span>
                <span><span className="move-legend__swatch move-legend__swatch--jump" />Jump target</span>
            </p>}
            {jumpChoice && <p>More than one tile can jump there - click the highlighted tile you want to jump from.</p>}
            {statusMessage && <p>{statusMessage}</p>}
			<Board
                gameState={gameState}
                myPlayerNumber={isMyTurn ? myPlayerNumber : null}
                jumpChoice={jumpChoice}
                onTileClicked={onTileClicked}
            />
		</>
	)
}
