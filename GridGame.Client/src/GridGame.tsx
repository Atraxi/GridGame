import { useEffect, useMemo, useRef, useState } from "react";
import Board, { type JumpChoice, type MarkedCell } from "./components/game_board/Board";
import GameAnalysis from "./components/GameAnalysis";
import MoveHistory from "./components/MoveHistory";
import PlayerLegend, { type PresenceEntry } from "./components/PlayerLegend";
import RematchPanel from "./components/RematchPanel";
import { playerColour } from "./components/game_board/playerColour";
import * as signalR from '@microsoft/signalr';
import { useLoaderData, useNavigate, useParams } from "react-router";
import type { GridGame } from "./model/GridGame";
import type { BoardAnalysis, FinalResults } from "./model/analysis";
import { computeStateHash } from "./model/stateHash";
import { actionFromPush, boardAfter, type GameAction, type MoveHistory as MoveHistoryResponse } from "./model/history";
import { moveHintFor, type MoveHint } from "./model/moves";
import { authorizedFetch, ensureToken, getTokenExpiryMs, isGuest, SessionExpiredError } from "./auth";
import { loginRedirectPath } from "./loaders";
import "./GridGame.css";

type TileUpdate = { x: number, y: number, newTileValue: number }
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
/** GridGameHub.GetGameState */
type Snapshot = {
    gameBoard: number[][],
    currentPlayerNumber: number,
    actionsRemainingInTurn: number,
    turnNumber: number,
    isGameOver: boolean,
    resignedPlayerNumber: number | null,
    rematchGameId: number | null,
    stateHash: number,
    informallyDecidedFor: number | null,
    presence: PresenceEntry[],
    analysis: BoardAnalysis | null,
}
type MoveRejected = { x: number, y: number, reason: 'spectating' | 'notYourTurn' | 'unreachable' | 'gameOver' }

const REJECTION_MESSAGES: Record<MoveRejected['reason'], string> = {
    spectating: "Every seat in this game is taken - you're only watching.",
    notYourTurn: "It's not your turn.",
    unreachable: "That tile can't be reached from any of your tiles.",
    gameOver: "This game has already ended.",
}

const SHOW_OPPONENT_MOVES_KEY = 'gridgame.showOpponentMoves'

//Everything runs on free tiers that sleep when idle, and waking takes up to a minute or so - longer than SignalR's
//default reconnect policy waits (four tries over about 45 seconds). So: a few quick tries, then every 15 seconds,
//for up to ten minutes. After that it gives up until the tab is next shown (see the visibilitychange handler)
const RECONNECT_DELAYS_MS = [0, 2_000, 5_000, 10_000]
const RECONNECT_GIVE_UP_MS = 10 * 60_000
const reconnectPolicy: signalR.IRetryPolicy = {
    nextRetryDelayInMilliseconds: ({ previousRetryCount, elapsedMilliseconds }) =>
        elapsedMilliseconds > RECONNECT_GIVE_UP_MS ? null : RECONNECT_DELAYS_MS[previousRetryCount] ?? 15_000,
}

const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

//A per-browser preference, so storage being unavailable (private mode, blocked site data) just means the default
const readShowOpponentMoves = () => {
    try {
        return localStorage.getItem(SHOW_OPPONENT_MOVES_KEY) !== 'false'
    } catch {
        return true
    }
}

/** A move tapped on a touch screen, waiting for a second tap to confirm it (see onTileClicked) */
type PendingMove = { x: number, y: number, kind: MoveHint }

const playerLabel = (entry: PresenceEntry) =>
    `Player ${entry.playerNumber}${entry.userName && !entry.userName.startsWith('Guest-') ? ` (${entry.userName})` : ''}`

/** Keyed on the game id, so following a rematch to its new game starts the page (and its hub connection) afresh -
 * React Router keeps a component mounted when only a route param changes */
export default function GridGameRoute() {
    const { gameId } = useParams()
    return <GridGamePage key={gameId} />
}

function GridGamePage() {
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
    //Resigning before the board says the game is decided needs a second, deliberate step (see the resign controls)
    const [confirmingResign, setConfirmingResign] = useState(false)
    const [rematchGameId, setRematchGameId] = useState(initialState.rematchGameId ?? null)
    //Read by the rematchCreated handler, which is attached once and so can't see the latest state
    const myPlayerNumberRef = useRef<number | null>(null)
    useEffect(() => { myPlayerNumberRef.current = myPlayerNumber }, [myPlayerNumber])
    //Only ever arrives for named accounts (see GridGameHub.BroadcastStateChange) - a guest simply never gets it, so
    //there's nothing to gate client-side
    const [analysis, setAnalysis] = useState<BoardAnalysis | null>(null)
    const [showTerritory, setShowTerritory] = useState(true)
    const [showOpponentMoves, setShowOpponentMoves] = useState(readShowOpponentMoves)
    //Move history is a named-account feature, fetched the first time its panel is opened and then kept up to date
    //from the same pushes that update the board. Null until loaded, and again whenever a resync means the loaded
    //copy can no longer be trusted to line up with the board
    const [historyOpen, setHistoryOpen] = useState(false)
    const [history, setHistory] = useState<GameAction[] | null>(null)
    const [historyError, setHistoryError] = useState<string | null>(null)
    //How many actions the board on screen has had applied while stepping through the history; null shows live play
    const [historyPosition, setHistoryPosition] = useState<number | null>(null)
    const [pendingMove, setPendingMove] = useState<PendingMove | null>(null)
    //How the last press on the board was made. Touch needs a confirming second tap: fingers are imprecise, tiles on a
    //phone are small, and a move can't be taken back. Read from pointerdown, which reliably reports the pointer type
    const lastPointerType = useRef<string>('mouse')

    //The authoritative client-side copy of the game, and its hash. Kept in refs rather than derived from React state
    //because pushes have to be checked and applied strictly in arrival order, synchronously - React state updates
    //are batched and deferred, so the "current hash" read by the next push could otherwise be stale
    const modelRef = useRef<GridGame>(initialState)
    const hashRef = useRef(computeStateHash(initialState))
    //While a resync is in flight, pushes are held here rather than applied to a state that's about to be replaced -
    //any that chain on from the snapshot are replayed once it lands
    const resyncRef = useRef<{ pending: boolean, buffered: StatePush[] }>({ pending: false, buffered: [] })

    const isMyTurn = !gameState.isGameOver && myPlayerNumber != null && myPlayerNumber === gameState.currentPlayerNumber

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
            const action = actionFromPush(previous, push.updates, push.resignedPlayerNumber)
            if (action) {
                setHistory(current => current && [...current, action])
            }
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
                if (snapshot.stateHash !== hashRef.current) {
                    //Whatever was missed isn't in the loaded history either, so it's dropped and fetched afresh
                    setHistory(null)
                    setHistoryPosition(null)
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
                setRematchGameId(snapshot.rematchGameId)
                if (snapshot.analysis) {
                    setAnalysis(snapshot.analysis)
                }
                setJumpChoice(null)
                setPendingMove(null)
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
                    setPendingMove(null)
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

            //Everyone who played is pulled straight into the rematch; spectators are offered a link instead (see RematchPanel)
            hub.on('rematchCreated', ({ gameId }: { gameId: number }) => {
                setRematchGameId(gameId)
                if ((myPlayerNumberRef.current ?? 0) > 0) {
                    navigate(`/gridgame/${gameId}`)
                }
            })

            hub.on('moveRejected', (rejection: MoveRejected) => {
                setJumpChoice(null)
                setPendingMove(null)
                setStatusMessage(rejection.reason === 'spectating' && modelRef.current.isInviteOnly
                    ? "This game is invite-only - you're only watching."
                    : REJECTION_MESSAGES[rejection.reason] ?? "That move isn't allowed.")
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
                if (error && !cancelled && hub === activeConnection) {
                    //Automatic reconnection has run out of tries - start again with a whole new connection
                    setStatusMessage('Connection lost, reconnecting...')
                    recover()
                }
            })
        }

        const connect = async (): Promise<signalR.HubConnection | null> => {
            const hub = new signalR.HubConnectionBuilder()
                //Relative, so the hub is found wherever the app is served from: through the Vite proxy in
                //development, and on the app's own origin once the client is published into the API's wwwroot
                .withUrl("/GridGame", { accessTokenFactory: ensureToken })
                .withAutomaticReconnect(reconnectPolicy)
                .configureLogging(signalR.LogLevel.Debug)
                .build()

            attachHandlers(hub)

            try {
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
            } catch (error) {
                //Started but failed to register, say - don't leave it running unused
                hub.stop()
                throw error
            }
        }

        /** connect, retried for a few minutes - a first connection to a server that's asleep fails until it wakes */
        const connectPatiently = async (): Promise<signalR.HubConnection | null> => {
            const started = Date.now()
            let delay = 2_000
            while (!cancelled) {
                try {
                    const hub = await connect()
                    setStatusMessage(null)
                    return hub
                } catch (error) {
                    if (error instanceof SessionExpiredError || Date.now() - started > 5 * 60_000) {
                        throw error
                    }
                    setStatusMessage('Connecting to the game - the server may be waking up, which can take a minute...')
                    await sleep(delay)
                    delay = Math.min(delay * 2, 15_000)
                }
            }
            return null
        }

        /** Starts over with a brand new connection, once the old one is beyond saving */
        let recovering = false
        const recover = () => {
            if (recovering || cancelled) {
                return
            }
            recovering = true
            connectPatiently()
                .then(hub => {
                    if (hub) {
                        if (rotateTimer) {
                            clearTimeout(rotateTimer)
                        }
                        activeConnection = hub
                        setConnection(hub)
                        scheduleRotation(hub)
                    }
                })
                .catch(handleConnectError)
                .finally(() => { recovering = false })
        }

        //A phone that slept with the page open, or a tab left in the background, may come back to a connection that
        //gave up while it was away
        const onVisibilityChange = () => {
            if (document.visibilityState === 'visible' && activeConnection?.state === signalR.HubConnectionState.Disconnected) {
                setStatusMessage('Connection lost, reconnecting...')
                recover()
            }
        }
        document.addEventListener('visibilitychange', onVisibilityChange)

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
                const next = await connectPatiently().catch(handleConnectError)
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
                setStatusMessage("Couldn't reach the game server. It'll try again when you come back to this tab, or refresh the page to try now.")
            }
            return null
        }

        connectPatiently()
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
            document.removeEventListener('visibilitychange', onVisibilityChange)
            if (rotateTimer) {
                clearTimeout(rotateTimer)
            }
            activeConnection?.stop()
        }
    }, [])

    useEffect(() => {
        if (!historyOpen || history !== null || isGuest()) {
            return
        }
        let cancelled = false
        let retryTimer: ReturnType<typeof setTimeout> | null = null
        let attempts = 0
        const load = async () => {
            try {
                const response = await authorizedFetch(`/Games/GetMoveHistory?gameId=${initialState.id}`)
                if (!response.ok) {
                    throw new Error(`Request failed: ${response.status}`)
                }
                const loaded = (await response.json()) as MoveHistoryResponse
                if (cancelled) {
                    return
                }
                //The history is replayed backwards from this client's board, so it has to describe exactly that
                //board - a move landing while the request was in flight means asking again
                if (loaded.stateHash === hashRef.current) {
                    setHistory(loaded.actions)
                    setHistoryError(null)
                } else if (++attempts < 5) {
                    retryTimer = setTimeout(load, 1000)
                } else {
                    setHistoryError("The history couldn't be lined up with the board - close and reopen it to try again.")
                }
            } catch (error) {
                if (!cancelled) {
                    setHistoryError(`Couldn't load the move history: ${error instanceof Error ? error.message : error}`)
                }
            }
        }
        load()
        return () => {
            cancelled = true
            if (retryTimer) {
                clearTimeout(retryTimer)
            }
        }
    }, [historyOpen, history, initialState.id])

    const viewingHistory = history != null && historyPosition != null
    const shownBoard = useMemo(
        () => history != null && historyPosition != null
            ? boardAfter(gameState.gameBoard, history, historyPosition)
            : gameState.gameBoard,
        [gameState.gameBoard, history, historyPosition])
    const historyMarks = useMemo((): MarkedCell[] | undefined => {
        const action = history != null && historyPosition != null && historyPosition > 0 ? history[historyPosition - 1] : null
        if (!action || action.kind === 'resign') {
            return undefined
        }
        const marks: MarkedCell[] = [{ x: action.x, y: action.y, mark: 'landing' }]
        if (action.originX != null && action.originY != null) {
            marks.push({ x: action.originX, y: action.originY, mark: 'origin' })
        }
        return marks
    }, [history, historyPosition])

    const changeHistoryPosition = (position: number | null) => {
        setJumpChoice(null)
        setPendingMove(null)
        setHistoryPosition(position)
    }

    const toggleHistory = (open: boolean) => {
        setHistoryOpen(open)
        if (!open) {
            setHistoryPosition(null)
        }
    }

    const onTileClicked = (x: number, y: number) => () => {
        //A past board is only for looking at
        if (!connection || gameState.isGameOver || viewingHistory) {
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
        //A tap only selects a legal move; tapping the same tile again (or the confirm bar's button) makes it. Anything
        //else goes straight to the server, which says why it isn't allowed
        const kind = moveHintFor(gameState.gameBoard, myPlayerNumber, x, y)
        if (lastPointerType.current === 'touch' && kind && (pendingMove?.x !== x || pendingMove?.y !== y)) {
            setStatusMessage(null)
            setPendingMove({ x, y, kind })
            return
        }
        return makeMove(x, y)
    }

    const makeMove = (x: number, y: number) => {
        setPendingMove(null)
        return connection?.invoke("requestMove", x, y)
    }

    //Escape backs out of choosing a jump origin or a tapped move, same as clicking anywhere else on the board
    useEffect(() => {
        if (!jumpChoice && !pendingMove) {
            return
        }
        const onKeyDown = (event: KeyboardEvent) => {
            if (event.key === 'Escape') {
                setJumpChoice(null)
                setPendingMove(null)
            }
        }
        window.addEventListener('keydown', onKeyDown)
        return () => window.removeEventListener('keydown', onKeyDown)
    }, [jumpChoice, pendingMove])

    const toggleOpponentMoves = (show: boolean) => {
        setShowOpponentMoves(show)
        try {
            localStorage.setItem(SHOW_OPPONENT_MOVES_KEY, String(show))
        } catch {
            //Only a remembered preference - fine to lose
        }
    }

    const lockSeat = () => {
        connection?.invoke("lockSeat").catch(() => setStatusMessage("Couldn't lock your seat - check your connection and try again."))
    }

    const sendResign = () => {
        setConfirmingResign(false)
        connection?.invoke("resign").catch(() => setStatusMessage("Couldn't resign - check your connection and try again."))
    }

    //Once the board says the game is decided against you, conceding is a single confirm
    const resign = () => {
        if (window.confirm('Resign and end the game for everyone? Scores are settled as the board stands.')) {
            sendResign()
        }
    }

    const presenceNotices = presence.filter(entry =>
        entry.playerNumber !== myPlayerNumber && entry.status !== 'connected' && entry.status !== 'open')
    const isSeated = myPlayerNumber != null && myPlayerNumber > 0
    //Offered up front to anyone seated other than the player it's decided for. Anyone seated can still resign at any
    //other time, but only through the resign controls' extra warning step
    const resignOffered = !gameState.isGameOver && isSeated &&
        informallyDecidedFor != null && informallyDecidedFor !== myPlayerNumber
    const opponentMovesShown = isSeated && showOpponentMoves && !gameState.isGameOver
    const mySeatLocked = presence.some(entry => entry.playerNumber === myPlayerNumber && entry.locked)
    const pendingMarks: MarkedCell[] | undefined = pendingMove ? [{ x: pendingMove.x, y: pendingMove.y, mark: 'landing' }] : undefined

    return (
        <div className={`game-page${pendingMove ? ' game-page--confirming' : ''}`}>
            <h1 className="game-page__title">{gameState.name}</h1>
            <p>
                Tap or click an unclaimed or enemy tile next to one of yours to spawn onto it, or two tiles away
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
            <PlayerLegend
                presence={presence}
                myPlayerNumber={myPlayerNumber}
                currentPlayerNumber={gameState.currentPlayerNumber}
                resignedPlayerNumber={gameState.resignedPlayerNumber}
                isGameOver={gameState.isGameOver}
            />
            {isSeated && !gameState.isGameOver && (
                <div className="seat-panel">
                    <span>
                        <span className="seat-panel__swatch" style={{ background: playerColour(myPlayerNumber) }} />
                        You're player {myPlayerNumber}.
                    </span>
                    {mySeatLocked
                        ? <span>This seat is locked to your account.</span>
                        : isGuest()
                            ? <span className="seat-panel__hint">Create a named account to lock this seat to you.</span>
                            : <>
                                <button type="button" onClick={lockSeat}>Lock my seat</button>
                                <span className="seat-panel__hint">
                                    Keeps player {myPlayerNumber} yours whenever this game is reopened, so nobody else can take it.
                                </span>
                            </>}
                    {!resignOffered && !confirmingResign && (
                        <button type="button" className="seat-panel__resign" onClick={() => setConfirmingResign(true)}>Resign...</button>
                    )}
                </div>
            )}
            {confirmingResign && isSeated && !gameState.isGameOver && !resignOffered && (
                <div className="resign-confirm" role="alert">
                    <p>
                        <strong>{informallyDecidedFor === myPlayerNumber
                            ? "The board suggests you're winning."
                            : "This game isn't decided yet."}</strong> Resigning ends it for everyone right now, and
                        scores the board as it stands - contested tiles go to nobody. It can't be undone.
                    </p>
                    <div className="resign-confirm__buttons">
                        <button type="button" onClick={() => setConfirmingResign(false)}>Keep playing</button>
                        <button type="button" className="resign-confirm__go" onClick={sendResign}>Resign anyway</button>
                    </div>
                </div>
            )}
            {myPlayerNumber === 0 && !gameState.isGameOver && (
                <p className="seat-panel">
                    You're watching - {gameState.isInviteOnly ? 'this game is invite-only.' : 'every seat is taken.'}
                </p>
            )}
            {presenceNotices.map(entry => (
                <p key={entry.playerNumber} className="presence-notice">
                    {entry.status === 'disconnected'
                        ? `${playerLabel(entry)} has disconnected.`
                        : entry.status === 'away'
                            ? `${playerLabel(entry)} isn't here right now - this seat is kept for them.`
                            : `${playerLabel(entry)} isn't responding - they may have lost their connection.`}
                </p>
            ))}
            {gameState.isGameOver && <FinalResultsSummary results={finalResults} />}
            {gameState.isGameOver && (
                <RematchPanel
                    connection={connection}
                    isSeated={isSeated}
                    rematchGameId={rematchGameId}
                    mapId={initialState.mapId ?? null}
                    playerCount={gameState.playerCount}
                />
            )}
            {resignOffered && (
                <p className="resign-offer">
                    The board suggests this game is decided in player {informallyDecidedFor}'s favour.{' '}
                    <button type="button" onClick={resign}>Resign</button>
                </p>
            )}
            {(isMyTurn || opponentMovesShown) && !jumpChoice && !viewingHistory && <p className="move-legend">
                {isMyTurn && <>
                    <span><span className="move-legend__swatch move-legend__swatch--spawn" />You can spawn</span>
                    <span><span className="move-legend__swatch move-legend__swatch--jump" />You can jump</span>
                </>}
                {opponentMovesShown && <>
                    <span><span className="move-legend__swatch move-legend__swatch--opponent-spawn" />Opponent can spawn</span>
                    <span><span className="move-legend__swatch move-legend__swatch--opponent-jump" />Opponent can jump</span>
                </>}
            </p>}
            {statusMessage && <p>{statusMessage}</p>}
            {isSeated && !gameState.isGameOver && (
                <p className="territory-toggle">
                    <label>
                        <input type="checkbox" checked={showOpponentMoves} onChange={event => toggleOpponentMoves(event.target.checked)} />
                        Show where opponents could move next (rings in their colour: dotted to spawn, solid to jump - jumps can cross walls)
                    </label>
                </p>
            )}
            {analysis && !gameState.isGameOver && (
                <p className="territory-toggle">
                    <label>
                        <input type="checkbox" checked={showTerritory} onChange={event => setShowTerritory(event.target.checked)} />
                        Show territory (solid: only that player can reach it; striped: they could still be cut off)
                    </label>
                </p>
            )}
            {viewingHistory && (
                <div className="history-banner">
                    <p>
                        <strong>Replaying the move history</strong> - this is the board after action {historyPosition} of
                        {' '}{history.length}. Moves can't be made until you go back to the live board.
                    </p>
                    <button type="button" onClick={() => changeHistoryPosition(null)}>Back to live</button>
                </div>
            )}
            {jumpChoice && !viewingHistory && (
                <div className="jump-choice" role="alert">
                    <p>
                        <strong>Pick the tile to jump from.</strong> {jumpChoice.origins.length} of your tiles can
                        jump there - the arrows show each one. The tile you jump from dies.
                    </p>
                    <button type="button" onClick={() => setJumpChoice(null)}>Cancel</button>
                </div>
            )}
            <div onPointerDownCapture={event => { lastPointerType.current = event.pointerType }}>
                <Board
                    board={shownBoard}
                    playerCount={gameState.playerCount}
                    movesFor={isMyTurn && !viewingHistory ? myPlayerNumber : null}
                    opponentsOf={opponentMovesShown && !viewingHistory ? myPlayerNumber : null}
                    jumpChoice={viewingHistory ? null : jumpChoice}
                    //The analysis describes the live board, so it would be misleading over a past one
                    analysis={showTerritory && !gameState.isGameOver && !viewingHistory ? analysis : null}
                    historyMarks={viewingHistory ? historyMarks : pendingMarks}
                    onTileClicked={onTileClicked}
                />
            </div>
            {/* Pinned to the bottom of the screen rather than placed above the board, so it never shifts the tile
                that has to be tapped a second time */}
            {pendingMove && !jumpChoice && (
                <div className="move-confirm" role="alert">
                    <p>
                        {pendingMove.kind === 'spawn' ? 'Spawn here?' : 'Jump here? The tile you jump from dies.'}
                        {' '}Tap it again to confirm.
                    </p>
                    <div className="move-confirm__buttons">
                        <button type="button" onClick={() => setPendingMove(null)}>Cancel</button>
                        <button type="button" className="move-confirm__go" onClick={() => makeMove(pendingMove.x, pendingMove.y)}>
                            {pendingMove.kind === 'spawn' ? 'Spawn' : 'Jump'}
                        </button>
                    </div>
                </div>
            )}
            {isGuest()
                ? <p className="score-prediction">Create a named account to see and replay the move history.</p>
                : <details className="history-panel" open={historyOpen} onToggle={event => toggleHistory(event.currentTarget.open)}>
                    <summary>Move history</summary>
                    {history
                        ? <MoveHistory
                            actions={history}
                            position={historyPosition}
                            onPositionChange={changeHistoryPosition}
                            currentTurnNumber={gameState.turnNumber}
                        />
                        : <p className="score-prediction">{historyError ?? 'Loading the move history...'}</p>}
                </details>}
            {!gameState.isGameOver && (analysis
                ? <GameAnalysis analysis={analysis} myPlayerNumber={myPlayerNumber} />
                : isGuest() && <p className="score-prediction">Create a named account to see live board analysis.</p>)}
            {gameState.isGameOver && finalResults && <GameAnalysis analysis={finalResults.analysis} myPlayerNumber={myPlayerNumber} />}
        </div>
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
