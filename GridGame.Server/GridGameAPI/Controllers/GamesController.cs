using GridGameAPI.ActiveGames;
using GridGameAPI.Database;
using GridGameAPI.Model;
using GridGameAPI.UtilityInfrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GridGameAPI.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class GamesController(GameSessionManager _sessionManager, GameContext _context) : ControllerBase
    {
        private const int PAGE_SIZE = 20;

        [HttpGet]
        public IEnumerable<GameSummary> GetSummaries(int page)
        {
            return _context.GridGames
                .Skip((page - 1) * PAGE_SIZE)
                .Select(game => new GameSummary
                {
                    Id = game.Id,
                    Name = game.Name,
                    CreationDate = game.CreationDate,
                    IsGameOver = game.IsGameOver,
                    //TODO it would be nice to include dynamic stuff like turnNumber, but because of GameSessionManager it gets messy and hard to embed in an IQueryable. A nice to have for later
                })
                .Take(PAGE_SIZE);
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

        //Hosting a game is a privilege of permanent named accounts, same as designing maps. Games are always hosted
        //from a saved Map, which supplies the board, player count, and actions/turn - not posted freeform by the client
        [Authorize(Policy = AuthPolicy.NamedAccountOnly)]
        [HttpPost]
        public async Task<int> CreateFromMap(CreateGameFromMapRequest request)
        {
            var map = await _context.Maps.SingleAsync(map => map.Id == request.MapId);

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
            return newGame.Id;
        }
    }
}
