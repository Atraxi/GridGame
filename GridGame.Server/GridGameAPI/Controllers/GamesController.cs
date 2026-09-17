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
        public async Task<GridGame> GetGame(int gameId)
        {
            return _sessionManager.GetByGameId(gameId)?.GridGame ?? await _context.GridGames.SingleAsync(game => game.Id == gameId);
        }

        //Final scores (and other meta score stats) are a named-account-only feature - GridGame.FinalScores is
        //JsonIgnore'd everywhere else, so this is the only way to actually read it
        [Authorize(Policy = AuthPolicy.NamedAccountOnly)]
        [HttpGet]
        public async Task<ActionResult<int[]>> GetFinalScores(int gameId)
        {
            var game = _sessionManager.GetByGameId(gameId)?.GridGame ?? await _context.GridGames.SingleAsync(g => g.Id == gameId);
            if (!game.IsGameOver || game.FinalScores is null)
            {
                return NotFound("This game hasn't ended yet.");
            }
            return game.FinalScores;
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
