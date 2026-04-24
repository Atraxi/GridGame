using GridGameAPI.Model;
using Microsoft.AspNetCore.Mvc;

namespace GridGameAPI.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class GamesController : ControllerBase
    {
        private const int PAGE_SIZE = 20;

        //TODO set up Entity Framework, Database, etc
        private readonly List<GridGame> games = [new GridGame {
            Id = 1,
            CreationDate = DateTime.Now,
            Name = "Hardcoded test game 1",
            TurnNumber = 0,
        }];

        [HttpGet]
        public IEnumerable<GameSummary> GetSummaries(int page)
        {
            return games
                .Skip((page - 1) * PAGE_SIZE)
                .Select(game => new GameSummary
                {
                    Id = game.Id,
                    Name = game.Name,
                    CreationDate = game.CreationDate,
                    TurnNumber = game.TurnNumber,
                })
                .Take(PAGE_SIZE);
        }

        [HttpGet]
        public GridGame GetGame(int gameId)
        {
            return games.Single(game => game.Id == gameId);
        }

        public int Post(GridGame gameSummary)
        {
            games.Add(gameSummary);
            return games.Count - 1;
        }
    }
}
