using GridGameAPI.Database;
using GridGameAPI.Model;
using GridGameAPI.UtilityInfrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GridGameAPI.ActiveGames.SignalRHubs
{
    [Authorize(Policy = AuthPolicy.AnyUser)]
    public class GridGameHub(GameSessionManager _sessionManager, GameContext _gameContext) : Hub
    {
        /// <returns>The player number the caller owns tiles as, or 0 if the game's seats are already full and they're only spectating</returns>
        public int OnConnectedToGame(int gameId)
        {
            var session = _sessionManager.GetByGameIdOrCreate(gameId, _ => {
                var game = _gameContext.GridGames
                    .AsNoTracking()
                    .Single(game => game.Id == gameId);
                _gameContext.Entry(game);
                return new GameSession
                {
                    GridGame = game,
                };
            });
            session.Connections.Add(Context.ConnectionId, Context.UserIdentifier!);
            if (Context.User?.FindFirst(Constants.AccountTypeClaim)?.Value == Constants.NamedAccountType)
            {
                session.NamedAccountConnections.Add(Context.ConnectionId);
            }
            if (!session.PlayerNumbers.ContainsKey(Context.UserIdentifier!) &&
                session.PlayerNumbers.Count < session.GridGame.PlayerCount)
            {
                session.PlayerNumbers[Context.UserIdentifier!] = session.PlayerNumbers.Count + 1;
            }
            return session.PlayerNumbers.GetValueOrDefault(Context.UserIdentifier!, 0);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            if (session is null)
            {
                //Disconnected (or was never fully connected) before ever calling OnConnectedToGame - nothing to clean up
                return;
            }
            session.Connections.Remove(Context.ConnectionId);
            session.NamedAccountConnections.Remove(Context.ConnectionId);
            if (session.Connections.Count == 0)
            {
                _sessionManager.Remove(session.GridGame.Id);
                await PersistGame(session.GridGame);
            }
        }

        /// <summary>
        /// Requests a move onto (x, y): a spawn if any owned tile is orthogonally/diagonally adjacent, otherwise a
        /// jump from an owned tile 2 tiles away (which then dies). If more than one tile could be jumped from, the
        /// move isn't applied yet - the caller is asked to disambiguate via ConfirmJump instead.
        /// </summary>
        public async Task RequestMove(int x, int y)
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            if (session is null)
            {
                return;
            }

            //Held across the whole validate-and-apply body, not just the mutation - otherwise two concurrent
            //requests can both pass validation (e.g. both read the same ActionsRemainingInTurn) before either
            //applies, and the second then mutates a board whose state it never actually validated against
            await session.MoveLock.WaitAsync();
            try
            {
                var game = session.GridGame;
                var board = game.GameBoard;

                if (game.IsGameOver)
                {
                    await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "gameOver" });
                    return;
                }
                if (!session.PlayerNumbers.TryGetValue(Context.UserIdentifier!, out var player))
                {
                    await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "spectating" });
                    return;
                }
                if (player != game.CurrentPlayerNumber)
                {
                    await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "notYourTurn" });
                    return;
                }
                if (!GridGameMoves.InBounds(board, x, y) || board[x, y] == TileState.Dead || board[x, y] == player)
                {
                    await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "unreachable" });
                    return;
                }

                //Landing on empty ground claims it; landing on an enemy tile kills it outright rather than capturing it
                var landingValue = board[x, y] == TileState.Unclaimed ? player : TileState.Dead;

                if (GridGameMoves.CanSpawnTo(board, player, x, y))
                {
                    await ApplyMove(session, [(x, y, landingValue)]);
                    return;
                }

                var jumpOrigins = GridGameMoves.JumpOriginsFor(board, player, x, y);
                switch (jumpOrigins.Count)
                {
                    case 0:
                        await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "unreachable" });
                        break;
                    case 1:
                        await ApplyMove(session, [(jumpOrigins[0].X, jumpOrigins[0].Y, TileState.Dead), (x, y, landingValue)]);
                        break;
                    default:
                        await Clients.Caller.SendAsync("chooseJumpOrigin", new
                        {
                            x,
                            y,
                            origins = jumpOrigins.Select(origin => new { x = origin.X, y = origin.Y })
                        });
                        break;
                }
            }
            finally
            {
                session.MoveLock.Release();
            }
        }

        /// <summary>Completes a jump once the caller has picked which of several eligible tiles to jump from</summary>
        public async Task ConfirmJump(int originX, int originY, int x, int y)
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            if (session is null)
            {
                return;
            }

            await session.MoveLock.WaitAsync();
            try
            {
                var game = session.GridGame;
                var board = game.GameBoard;

                if (game.IsGameOver)
                {
                    await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "gameOver" });
                    return;
                }
                if (!session.PlayerNumbers.TryGetValue(Context.UserIdentifier!, out var player))
                {
                    await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "spectating" });
                    return;
                }
                if (player != game.CurrentPlayerNumber)
                {
                    await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "notYourTurn" });
                    return;
                }
                if (!GridGameMoves.InBounds(board, x, y) || board[x, y] == TileState.Dead || board[x, y] == player ||
                    !GridGameMoves.IsValidJump(board, player, originX, originY, x, y))
                {
                    await Clients.Caller.SendAsync("moveRejected", new { x, y, reason = "unreachable" });
                    return;
                }

                //Landing on empty ground claims it; landing on an enemy tile kills it outright rather than capturing it
                var landingValue = board[x, y] == TileState.Unclaimed ? player : TileState.Dead;
                await ApplyMove(session, [(originX, originY, TileState.Dead), (x, y, landingValue)]);
            }
            finally
            {
                session.MoveLock.Release();
            }
        }

        /// <summary>Writes the given (x, y, newTileValue) tuples to the board, advances the turn (skipping past any
        /// player with no legal move left, e.g. eliminated or walled in), checks for game-over, persists to the DB
        /// once per completed round (rather than every move - the in-memory session is otherwise the sole source of
        /// truth while a game is live), and broadcasts all of it. Callers (RequestMove/ConfirmJump) already hold
        /// session.MoveLock for their whole validate-and-apply body, so this doesn't acquire it itself -
        /// SemaphoreSlim isn't reentrant, so doing both would deadlock</summary>
        private async Task ApplyMove(GameSession session, (int X, int Y, int NewValue)[] updates)
        {
            var game = session.GridGame;
            foreach (var (x, y, newValue) in updates)
            {
                game.GameBoard[x, y] = newValue;
            }

            game.ActionsRemainingInTurn--;

            if (!game.IsGameOver)
            {
                if (GameEndAnalysis.IsGameOver(game.GameBoard, game.PlayerCount))
                {
                    await EndGame(game);
                }
                else
                {
                    await BroadcastScorePrediction(session);

                    if (game.ActionsRemainingInTurn <= 0)
                    {
                        var nextPlayer = game.CurrentPlayerNumber;
                        var foundMover = false;
                        //True once the search below has wrapped past the last player back to the first, i.e. a full
                        //round has completed - tracked by candidate value rather than "landed on player 1", since a
                        //player skipped for having no legal move (eliminated, walled in) might mean nobody ever lands
                        //on exactly 1 again
                        var completedRound = false;
                        for (var attempt = 0; attempt < game.PlayerCount; attempt++)
                        {
                            var candidate = nextPlayer % game.PlayerCount + 1;
                            if (candidate < nextPlayer)
                            {
                                completedRound = true;
                            }
                            nextPlayer = candidate;
                            if (GridGameMoves.HasAnyLegalMove(game.GameBoard, nextPlayer))
                            {
                                foundMover = true;
                                break;
                            }
                        }

                        if (foundMover)
                        {
                            game.CurrentPlayerNumber = nextPlayer;
                            game.ActionsRemainingInTurn = game.ActionsPerTurn;
                            game.TurnNumber++;

                            if (completedRound)
                            {
                                await PersistGame(game);
                            }
                        }
                        else
                        {
                            //Contradicts IsGameOver being false above (a player in contact with another always has at
                            //least one legal move) - treated as game over defensively rather than stalling regardless
                            await EndGame(game);
                        }
                    }
                }
            }

            await Clients.Clients(session.Connections.Keys).SendAsync("tileUpdateProcessed", new
            {
                updates = updates.Select(u => new { x = u.X, y = u.Y, newTileValue = u.NewValue }),
                currentPlayerNumber = game.CurrentPlayerNumber,
                actionsRemainingInTurn = game.ActionsRemainingInTurn,
                turnNumber = game.TurnNumber,
                isGameOver = game.IsGameOver,
            });
        }

        /// <summary>Live "if play continued naturally from here" score estimate - a named-account perk like final
        /// scores, so it's only sent to connections known to be on a named account (see OnConnectedToGame)</summary>
        private async Task BroadcastScorePrediction(GameSession session)
        {
            if (session.NamedAccountConnections.Count == 0)
            {
                return;
            }

            var prediction = ScorePrediction.Compute(session.GridGame.GameBoard, session.GridGame.PlayerCount);
            await Clients.Clients(session.NamedAccountConnections.ToList()).SendAsync("scorePredictionUpdated", new
            {
                predictedScores = prediction.PredictedScores,
                contestedCells = prediction.ContestedCells,
            });
        }

        /// <summary>Marks the game over, computes the (expensive, one-time) final score, and persists both
        /// immediately rather than waiting for the last connection to drop</summary>
        private async Task EndGame(GridGame game)
        {
            game.IsGameOver = true;
            game.FinalScores = GameEndAnalysis.ComputeScores(game.GameBoard, game.PlayerCount);
            await PersistGame(game);
        }

        /// <summary>Force-saves the whole entity - the in-memory session, not EF change tracking, is the source of
        /// truth while a game is live, so every field is marked Modified rather than relying on EF to have noticed
        /// what changed</summary>
        private async Task PersistGame(GridGame game)
        {
            _gameContext.Attach(game);
            _gameContext.Entry(game).State = EntityState.Modified;
            await _gameContext.SaveChangesAsync();
        }
    }
}
