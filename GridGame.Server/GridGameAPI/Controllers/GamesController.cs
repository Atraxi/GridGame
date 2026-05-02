using GridGameAPI.Model;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

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
            Players = [
                new Player {
                    Name = "Test player 1",
                    Id = 1,
                },
                new Player {
                    Name = "Tst player 2",
                    Id = 2,
                },
            ],
            GameBoard = new int[3,4],
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
