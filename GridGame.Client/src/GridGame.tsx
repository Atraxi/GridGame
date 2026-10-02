import { useEffect, useRef, useState } from "react";
import Board from "./components/game_board/Board";
import GameAnalysis from "./components/GameAnalysis";
import * as signalR from '@microsoft/signalr';
import { useLoaderData, useNavigate } from "react-router";
import type { GridGame } from "./model/GridGame";
import type { BoardAnalysis, FinalResults } from "./model/analysis";
import { computeStateHash } from "./model/stateHash";
import { authorizedFetch, ensureToken, getTokenExpiryMs, isGuest, SessionExpiredError } from "./auth";
import { loginRedirectPath } from "./loaders";
import "./GridGame.css";

type TileUpdate = { x: number, y: number, newTileValue: number }
type JumpChoice = { x: number, y: number, origins: { x: number, y: number }[] }
/** One state change, pushed to everyone in the game. The hashes let a client check it's applying this on top of
 * the same state the server did (see StateHash.cs), and requiresAck marks a turn end the server wants confirmed */
type StatePush = {
    updates: TileUpdate[],
    currentPlayerNumber: number,
    actionsRemainingInTurn: number,
    turnNumber: number,
    isGameOver: boolean,
    resignedPlayerNumber: number | null,
    previousStateHash: number,
    stateHash: number,
    requiresAck: boolean,
    informallyDecidedFor: number | null,
}
type PresenceEntry = { playerNumber: number, userName: string | null, status: 'connected' | 'unresponsive' | 'disconnected' | 'open' }
/** GridGameHub.GetGameState */
type Snapshot = {
    gameBoard: number[][],
    currentPlayerNumber: number,
    actionsRemainingInTurn: number,
    turnNumber: number,
    isGameOver: boolean,
    resignedPlayerNumber: number | null,
    stateHash: number,
    informallyDecidedFor: number | null,
    presence: PresenceEntry[],
    analysis: BoardAnalysis | null,
}
type MoveRejected = { x: number, y: number, reason: 'spectating' | 'notYourTurn' | 'unreachable' | 'gameOver' }

const REJECTION_MESSAGES: Record<MoveRejected['reason'], string> = {
    spectating: "This game's seats are full - you're only spectating.",
    notYourTurn: "It's not your turn.",
    unreachable: "That tile can't be reached from any of your tiles.",
    gameOver: "This game has already ended.",
}

const playerLabel = (entry: PresenceEntry) =>
    `Player ${entry.playerNumber}${entry.userName && !entry.userName.startsWith('Guest-') ? ` (${entry.userName})` : ''}`

export default function GridGamePage() {
    const initialState = useLoaderData<GridGame>()
    const navigate = useNavigate()
    const [connection, setConnection] = useState<signalR.HubConnection | null>(null)
    const [gameState, setGameState] = useState(initialState)
    const [myPlayerNumber, setMyPlayerNumber] = useState<number | null>(null)
    const [jumpChoice, setJumpChoice] = useState<JumpChoice | null>(null)
    const [statusMessage, setStatusMessage] = useState<string | null>(null)
    const [finalResults, setFinalResults] = useState<FinalResults | null>(null)
    const [presence, setPresence] = useState<PresenceEntry[]>([])
    const [informallyDecidedFor, setInformallyDecidedFor] = useState<number | null>(null)
    //Only ever arrives for named accounts (see GridGameHub.BroadcastStateChange) - a guest simply never gets it, so
    //there's nothing to gate client-side
    const [analysis, setAnalysis] = useState<BoardAnalysis | null>(null)
    const [showTerritory, setShowTerritory] = useState(true)

    //The authoritative client-side copy of the game, and its hash. Kept in refs rather than derived from React state
    //because pushes have to be checked and applied strictly in arrival order, synchronously - React state updates
    //are batched and deferred, so the "current hash" read by the next push could otherwise be stale
    const modelRef = useRef<GridGame>(initialState)
    const hashRef = useRef(computeStateHash(initialState))
    //While a resync is in flight, pushes are held here rather than applied to a state that's about to be replaced -
    //any that chain on from the snapshot are replayed once it lands
    const resyncRef = useRef<{ pending: boolean, buffered: StatePush[] }>({ pending: false, buffered: [] })

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
            .then(results => { if (!cancelled) setFinalResults(results) })
            .catch(() => { if (!cancelled) setFinalResults(null) })
        return () => { cancelled = true }
    }, [gameState.isGameOver, gameState.id])

    //Once deployed behind Azure SignalR, moves never reach the server as incoming HTTP requests, and the free App
    //Service plan unloads an app after ~20 minutes without one - so a long game would be cut off mid-play. A cheap
    //periodic request while the page is open keeps it loaded (see /healthz in Program.cs)
    useEffect(() => {
        const keepAlive = setInterval(() => { fetch('/healthz').catch(() => {}) }, 5 * 60 * 1000)
        return () => clearInterval(keepAlive)
    }, [])

    useEffect(() => {
        let cancelled = false
        let activeConnection: signalR.HubConnection | null = null
        let rotateTimer: ReturnType<typeof setTimeout> | null = null

        const commit = (next: GridGame) => {
            modelRef.current = next
            hashRef.current = computeStateHash(next)
            setGameState(next)
        }

        /** Applies a push if it follows on from exactly the state we have. 'duplicate' is normal during token
         * rotation, when two connections are briefly both registered and each receives every push */
        const applyPush = (push: StatePush): 'applied' | 'duplicate' | 'outOfStep' => {
            if (push.stateHash === hashRef.current) {
                return 'duplicate'
            }
            if (push.previousStateHash !== hashRef.current) {
                return 'outOfStep'
            }
            const previous = modelRef.current
            const board = previous.gameBoard.map(row => row.slice())
            push.updates.forEach(({ x, y, newTileValue }) => { board[x][y] = newTileValue })
            const next: GridGame = {
                ...previous,
                gameBoard: board,
                currentPlayerNumber: push.currentPlayerNumber,
                actionsRemainingInTurn: push.actionsRemainingInTurn,
                turnNumber: push.turnNumber,
                isGameOver: push.isGameOver,
                resignedPlayerNumber: push.resignedPlayerNumber,
            }
            if (computeStateHash(next) !== push.stateHash) {
                return 'outOfStep'
            }
            commit(next)
            setInformallyDecidedFor(push.informallyDecidedFor)
            return 'applied'
        }

        const acknowledge = (hub: signalR.HubConnection) =>
            hub.invoke("acknowledgeState", hashRef.current).catch(() => {})

        /** Replaces local state with a fresh server snapshot, then confirms it. Run on every (re)connect, and
         * whenever a push shows this client has fallen out of step */
        const resync = async (hub: signalR.HubConnection) => {
            if (resyncRef.current.pending) {
                return
            }
            resyncRef.current = { pending: true, buffered: [] }
            try {
                const snapshot = await hub.invoke<Snapshot | null>("getGameState")
                if (!snapshot || cancelled) {
                    return
                }
                commit({
                    ...modelRef.current,
                    gameBoard: snapshot.gameBoard,
                    currentPlayerNumber: snapshot.currentPlayerNumber,
                    actionsRemainingInTurn: snapshot.actionsRemainingInTurn,
                    turnNumber: snapshot.turnNumber,
                    isGameOver: snapshot.isGameOver,
                    resignedPlayerNumber: snapshot.resignedPlayerNumber,
                })
                if (hashRef.current !== snapshot.stateHash) {
                    console.warn('State hash mismatch straight after a resync - client and server hashing have diverged')
                }
                setInformallyDecidedFor(snapshot.informallyDecidedFor)
                setPresence(snapshot.presence)
                if (snapshot.analysis) {
                    setAnalysis(snapshot.analysis)
                }
                setJumpChoice(null)
                //Pushes sent before the snapshot was taken no longer chain on and are skipped; ones sent after it do
                resyncRef.current.buffered.forEach(push => applyPush(push))
                await acknowledge(hub)
            } catch (error) {
                console.warn('Resync failed', error)
            } finally {
                resyncRef.current = { pending: false, buffered: [] }
            }
        }

        const attachHandlers = (hub: signalR.HubConnection) => {
            hub.on('tileUpdateProcessed', (push: StatePush) => {
                if (resyncRef.current.pending) {
                    resyncRef.current.buffered.push(push)
                    return
                }
                const result = applyPush(push)
                if (result === 'outOfStep') {
                    //Missed or misapplied something - the snapshot covers this push too
                    resync(hub)
                    return
                }
                if (result === 'applied') {
                    setJumpChoice(null)
                    setStatusMessage(null)
                }
                if (push.requiresAck) {
                    acknowledge(hub)
                }
            })

            //The server hasn't heard back about a turn-ending push (see SessionMonitor) - maybe it never arrived
            hub.on('stateAckRequested', ({ stateHash }: { stateHash: number }) => {
                if (resyncRef.current.pending) {
                    return
                }
                if (stateHash === hashRef.current) {
                    acknowledge(hub)
                } else {
                    resync(hub)
                }
            })

            hub.on('boardAnalysisUpdated', (update: BoardAnalysis) => {
                setAnalysis(update)
            })

            hub.on('presenceUpdated', (update: PresenceEntry[]) => {
                setPresence(update)
            })

            hub.on('moveRejected', (rejection: MoveRejected) => {
                setJumpChoice(null)
                setStatusMessage(REJECTION_MESSAGES[rejection.reason] ?? "That move isn't allowed.")
            })

            hub.on('chooseJumpOrigin', (choice: JumpChoice) => {
                setStatusMessage(null)
                setJumpChoice(choice)
            })

            //A network blip (e.g. a phone locking) keeps the same connection object once SignalR reconnects it, but
            //the server has forgotten this connection id entirely - it needs to be told whose game this is again,
            //and anything pushed while disconnected was lost, so the state has to be re-fetched too
            hub.onreconnecting(() => setStatusMessage('Connection lost, reconnecting...'))
            hub.onreconnected(async () => {
                setStatusMessage(null)
                setMyPlayerNumber(await hub.invoke<number>("onConnectedToGame", initialState.id))
                await resync(hub)
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

            const playerNumber = await hub.invoke<number>("onConnectedToGame", initialState.id)
            if (cancelled) {
                await hub.stop()
                return null
            }

            setMyPlayerNumber(playerNumber)
            //The loader's copy of the game may already be a few moves old by the time the hub is connected
            await resync(hub)
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
                const next = await connect().catch(handleConnectError)
                if (!next) {
                    return
                }
                await hub.stop()
                activeConnection = next
                setConnection(next)
                scheduleRotation(next)
            }, delay)
        }

        const handleConnectError = (error: unknown) => {
            if (error instanceof SessionExpiredError) {
                navigate(loginRedirectPath(), { replace: true })
            } else {
                console.log('Error while starting connection: ' + error)
                setStatusMessage("Couldn't connect to the game - refresh the page to try again.")
            }
            return null
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
            .catch(handleConnectError)

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

    const resign = () => {
        if (connection && window.confirm('Resign and end the game for everyone? Scores are settled as the board stands.')) {
            connection.invoke("resign").catch(() => setStatusMessage("Couldn't resign - check your connection and try again."))
        }
    }

    const presenceNotices = presence.filter(entry =>
        entry.playerNumber !== myPlayerNumber && (entry.status === 'disconnected' || entry.status === 'unresponsive'))
    //Offered to anyone seated other than the player it's decided for - only a hint, the server allows it any time
    const canResign = !gameState.isGameOver && myPlayerNumber != null && myPlayerNumber > 0 &&
        informallyDecidedFor != null && informallyDecidedFor !== myPlayerNumber

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
                    ? gameState.resignedPlayerNumber
                        ? `Game over - player ${gameState.resignedPlayerNumber} resigned.`
                        : "Game over - no player can reach any other anymore."
                    : isMyTurn
                        ? `Your turn - ${gameState.actionsRemainingInTurn} action${gameState.actionsRemainingInTurn === 1 ? '' : 's'} left.`
                        : `Waiting on player ${gameState.currentPlayerNumber}...`}
            </p>
            {presenceNotices.map(entry => (
                <p key={entry.playerNumber} className="presence-notice">
                    {entry.status === 'disconnected'
                        ? `${playerLabel(entry)} has disconnected.`
                        : `${playerLabel(entry)} isn't responding - they may have lost their connection.`}
                </p>
            ))}
            {gameState.isGameOver && <FinalResultsSummary results={finalResults} />}
            {canResign && (
                <p className="resign-offer">
                    The board suggests this game is decided in player {informallyDecidedFor}'s favour.{' '}
                    <button type="button" onClick={resign}>Resign</button>
                </p>
            )}
            {isMyTurn && <p className="move-legend">
                <span><span className="move-legend__swatch move-legend__swatch--spawn" />Spawn target</span>
                <span><span className="move-legend__swatch move-legend__swatch--jump" />Jump target</span>
            </p>}
            {jumpChoice && <p>More than one tile can jump there - click the highlighted tile you want to jump from.</p>}
            {statusMessage && <p>{statusMessage}</p>}
            {analysis && !gameState.isGameOver && (
                <p className="territory-toggle">
                    <label>
                        <input type="checkbox" checked={showTerritory} onChange={event => setShowTerritory(event.target.checked)} />
                        Show territory (solid: only that player can reach it; striped: they could still be cut off)
                    </label>
                </p>
            )}
            <Board
                gameState={gameState}
                myPlayerNumber={isMyTurn ? myPlayerNumber : null}
                jumpChoice={jumpChoice}
                analysis={showTerritory && !gameState.isGameOver ? analysis : null}
                onTileClicked={onTileClicked}
            />
            {!gameState.isGameOver && (analysis
                ? <GameAnalysis analysis={analysis} myPlayerNumber={myPlayerNumber} />
                : isGuest() && <p className="score-prediction">Create a named account to see live board analysis.</p>)}
            {gameState.isGameOver && finalResults && <GameAnalysis analysis={finalResults.analysis} myPlayerNumber={myPlayerNumber} />}
        </>
    )
}

function FinalResultsSummary({ results }: { results: FinalResults | null }) {
    if (!results) {
        return <p>{isGuest() ? "Create a named account to see final scores." : "Loading final scores..."}</p>
    }
    const { analysis } = results
    const atRisk = analysis.players.reduce((sum, player) => sum + player.uncontestedAtRisk, 0)
    return (
        <>
            <p>
                {results.winners.length === 1
                    ? `Player ${results.winners[0]} wins. `
                    : results.winners.length > 1 ? `Tied between players ${results.winners.join(' and ')}. ` : ''}
                Final scores: {results.scores.map((score, index) => `Player ${index + 1}: ${score}`).join(', ')}
            </p>
            {(analysis.contestedCells > 0 || atRisk > 0) && (
                <p className="score-prediction">
                    Scored as owned tiles plus uncontested territory. Still undecided when the game ended:
                    {' '}{analysis.contestedCells} contested tile{analysis.contestedCells === 1 ? '' : 's'} (scored for nobody)
                    {atRisk > 0 && `, and ${atRisk} uncontested tile${atRisk === 1 ? '' : 's'} whose owner could still have been cut off (scored for them)`}.
                </p>
            )}
        </>
    )
}
