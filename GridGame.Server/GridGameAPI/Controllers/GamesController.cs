using GridGameAPI.ActiveGames;
using GridGameAPI.Database;
using GridGameAPI.Model;
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
                    //TODO it would be nice to include dynamic stuff like turnNumber, but because of GameSessionManager it gets messy and hard to embed in an IQueryable. A nice to have for later
                })
                .Take(PAGE_SIZE);
        }

        [HttpGet]
        public async Task<GridGame> GetGame(int gameId)
        {
            return _sessionManager.GetByGameId(gameId)?.GridGame ?? await _context.GridGames.SingleAsync(game => game.Id == gameId);
        }

        [HttpPost]
        public async Task<int> Post(GridGame newGame)
        {
            _context.GridGames.Add(newGame);
            await _context.SaveChangesAsync();
            return newGame.Id;
        }
    }
}
