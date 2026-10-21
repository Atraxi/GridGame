using GridGameAPI.ActiveGames;
using GridGameAPI.Database;
using GridGameAPI.Model;
using GridGameAPI.UtilityInfrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GridGameAPI.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class GamesController(GameSessionManager _sessionManager, GameContext _context, GameListVersion _listVersion) : ControllerBase
    {
        private const int PAGE_SIZE = 20;

        /// <summary>Newest first. Polled by the home page while it's open, so a request carrying the current ETag is
        /// answered 304 without a database query (see GameListVersion)</summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<GameSummary>>> GetSummaries(int page)
        {
            page = Math.Max(page, 1);
            var etag = _listVersion.ETagFor(page);
            Response.Headers.ETag = etag;
            Response.Headers.CacheControl = "no-cache";
            //Cloudflare weakens a strong ETag (W/"...") when it compresses the response, so that's how it can come back
            if (Request.Headers.IfNoneMatch.Any(candidate => candidate?.Replace("W/", "") == etag))
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }

            var summaries = await _context.GridGames
                .OrderByDescending(game => game.CreationDate)
                .ThenByDescending(game => game.Id)
                .Skip((page - 1) * PAGE_SIZE)
                .Take(PAGE_SIZE)
                .Select(game => new GameSummary
                {
                    Id = game.Id,
                    Name = game.Name,
                    CreationDate = game.CreationDate,
                    TurnNumber = game.TurnNumber,
                    IsGameOver = game.IsGameOver,
                })
                .ToListAsync();

            //A live game's database row can lag by up to a round (see GridGameHub.ApplyMove), so its session wins
            foreach (var summary in summaries)
            {
                if (_sessionManager.GetByGameId(summary.Id)?.GridGame is { } live)
                {
                    summary.TurnNumber = live.TurnNumber;
                    summary.IsGameOver = live.IsGameOver;
                }
            }
            return summaries;
        }

        [Authorize(Policy = AuthPolicy.AnyUser)]
        [HttpGet]
        public async Task<ActionResult<GridGame>> GetGame(int gameId)
        {
            var game = _sessionManager.GetByGameId(gameId)?.GridGame ?? await _context.GridGames.SingleOrDefaultAsync(game => game.Id == gameId);
            return game is null ? NotFound("No game with that id exists.") : game;
        }

        //Final scores (and other meta score stats) are a named-account-only feature - GridGame.FinalScores is
        //JsonIgnore'd everywhere else, so this is the only way to actually read it
        [Authorize(Policy = AuthPolicy.NamedAccountOnly)]
        [HttpGet]
        public async Task<ActionResult> GetFinalScores(int gameId)
        {
            var game = _sessionManager.GetByGameId(gameId)?.GridGame ?? await _context.GridGames.SingleOrDefaultAsync(g => g.Id == gameId);
            if (game is null)
            {
                return NotFound("No game with that id exists.");
            }
            if (!game.IsGameOver || game.FinalScores is null)
            {
                return NotFound("This game hasn't ended yet.");
            }

            //The breakdown behind the scores is recomputed from the final board rather than stored - it's a pure
            //function of it. After a natural finish it just confirms the scores; after a resignation it's what shows
            //how much was still undecided (contested/at-risk cells) when the game was called
            var analysis = BoardAnalysis.Compute(game.GameBoard, game.PlayerCount);
            var best = Enumerable.Range(1, game.PlayerCount)
                .Where(player => player != game.ResignedPlayerNumber)
                .Select(player => game.FinalScores[player - 1])
                .DefaultIfEmpty(0)
                .Max();
            return Ok(new
            {
                scores = game.FinalScores,
                resignedPlayerNumber = game.ResignedPlayerNumber,
                //Highest score among those who didn't resign - a resigner concedes regardless of their score. Ties
                //list every tied player
                winners = Enumerable.Range(1, game.PlayerCount)
                    .Where(player => player != game.ResignedPlayerNumber && game.FinalScores[player - 1] == best),
                analysis = BoardAnalysis.ToWire(analysis),
            });
        }

        /// <summary>Every action taken so far, oldest first, with the state hash of the board it leads up to - the
        /// client steps backwards from its own copy of the board, so it checks that hash matches what it holds before
        /// trusting the two to line up. A named-account feature, like the board analysis; fetched on demand rather than
        /// pushed, since SignalR traffic is the scarcer budget (Azure SignalR's free tier bills messages per 2 KB)</summary>
        [Authorize(Policy = AuthPolicy.NamedAccountOnly)]
        [HttpGet]
        public async Task<ActionResult> GetMoveHistory(int gameId)
        {
            //A live session is the source of truth and is mutated under its MoveLock, so the log and hash are read
            //together under it. Otherwise the database copy is current - sessions persist before they close
            if (_sessionManager.GetByGameId(gameId) is { } session)
            {
                await session.MoveLock.WaitAsync();
                try
                {
                    return Ok(new
                    {
                        stateHash = session.StateHash,
                        actions = session.GridGame.MoveLog.Select(action => action.ToWire()).ToList(),
                    });
                }
                finally
                {
                    session.MoveLock.Release();
                }
            }

            var game = await _context.GridGames.AsNoTracking().SingleOrDefaultAsync(game => game.Id == gameId);
            if (game is null)
            {
                return NotFound("No game with that id exists.");
            }
            return Ok(new
            {
                stateHash = StateHash.Compute(game),
                actions = game.MoveLog.Select(action => action.ToWire()).ToList(),
            });
        }

        //Hosting a game is a privilege of permanent named accounts, same as designing maps. Games are always hosted
        //from a saved Map, which supplies the board, player count, and actions/turn - not posted freeform by the client
        [Authorize(Policy = AuthPolicy.NamedAccountOnly)]
        [HttpPost]
        public async Task<ActionResult<int>> CreateFromMap(CreateGameFromMapRequest request)
        {
            var map = await _context.Maps.SingleOrDefaultAsync(map => map.Id == request.MapId);
            //A draft hasn't passed the playability checks (see MapsController.Validate) - hosting one could start a game
            //some player can never move in. Someone else's draft is reported as missing, same as MapsController.Get
            if (map is null || (map.IsDraft && map.DesignedByPlayerId != User.FindFirstValue(ClaimTypes.NameIdentifier)))
            {
                return NotFound("No map with that id exists.");
            }
            if (map.IsDraft)
            {
                return BadRequest("Publish this map before hosting a game from it.");
            }

            var newGame = new GridGame
            {
                CreationDate = DateTime.Now,
                Name = request.Name,
                TurnNumber = 0,
                PlayerCount = map.PlayerCount,
                ActionsPerTurn = map.ActionsPerTurn,
                CurrentPlayerNumber = 1,
                ActionsRemainingInTurn = map.ActionsPerTurn,
                Players = [],
                //Cloned so that playing the game can never mutate the map's own saved template
                GameBoard = (int[,])map.StartingBoard.Clone(),
            };

            _context.GridGames.Add(newGame);
            await _context.SaveChangesAsync();
            _listVersion.Bump();
            return newGame.Id;
        }
    }
}
