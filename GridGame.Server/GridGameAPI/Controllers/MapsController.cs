using GridGameAPI.Database;
using GridGameAPI.Model;
using GridGameAPI.UtilityInfrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GridGameAPI.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    [Authorize(Policy = AuthPolicy.AnyUser)]
    public class MapsController(GameContext _context, UserManager<Player> _userManager) : ControllerBase
    {
        private const int PAGE_SIZE = 20;
        private const int MIN_PLAYERS = 2;
        private const int MAX_PLAYERS = 8;
        private const int MIN_ACTIONS_PER_TURN = 1;
        private const int MAX_ACTIONS_PER_TURN = 10;
        //Bounded well below the game's own DoS-scale concern - a board this size is already close to the limit of
        //what Board.tsx can render as one DOM node per tile
        private const int MAX_BOARD_DIMENSION = 200;

        [HttpGet]
        public async Task<IEnumerable<MapSummary>> GetSummaries(int page)
        {
            return await _context.Maps
                .OrderByDescending(map => map.CreationDate)
                .Skip((page - 1) * PAGE_SIZE)
                .Take(PAGE_SIZE)
                .Select(map => new MapSummary
                {
                    Id = map.Id,
                    Name = map.Name,
                    CreationDate = map.CreationDate,
                    PlayerCount = map.PlayerCount,
                    ActionsPerTurn = map.ActionsPerTurn,
                    DesignedByUserName = map.DesignedBy!.UserName!,
                })
                .ToListAsync();
        }

        [HttpGet]
        public async Task<IEnumerable<MapSummary>> GetMine()
        {
            var playerId = _userManager.GetUserId(User)!;
            return await _context.Maps
                .Where(map => map.DesignedByPlayerId == playerId)
                .Select(map => new MapSummary
                {
                    Id = map.Id,
                    Name = map.Name,
                    CreationDate = map.CreationDate,
                    PlayerCount = map.PlayerCount,
                    ActionsPerTurn = map.ActionsPerTurn,
                    DesignedByUserName = map.DesignedBy!.UserName!,
                })
                .ToListAsync();
        }

        //Designing a map is a privilege of permanent named accounts, same as hosting games
        [Authorize(Policy = AuthPolicy.NamedAccountOnly)]
        [HttpPost]
        //Bounds the request body itself so a claimed board size can't force a huge allocation during JSON
        //deserialization before the dimension check below ever gets a chance to run
        [RequestSizeLimit(2_000_000)]
        public async Task<ActionResult> Create(CreateMapRequest request)
        {
            if (request.PlayerCount < MIN_PLAYERS || request.PlayerCount > MAX_PLAYERS)
            {
                return BadRequest($"Player count must be between {MIN_PLAYERS} and {MAX_PLAYERS}.");
            }
            if (request.ActionsPerTurn < MIN_ACTIONS_PER_TURN || request.ActionsPerTurn > MAX_ACTIONS_PER_TURN)
            {
                return BadRequest($"Actions per turn must be between {MIN_ACTIONS_PER_TURN} and {MAX_ACTIONS_PER_TURN}.");
            }

            var rows = request.StartingBoard.GetLength(0);
            var columns = request.StartingBoard.GetLength(1);
            if (rows < 1 || columns < 1 || rows > MAX_BOARD_DIMENSION || columns > MAX_BOARD_DIMENSION)
            {
                return BadRequest($"Board dimensions must be between 1 and {MAX_BOARD_DIMENSION}.");
            }

            if (!MapValidation.AllPlayersHaveAtLeastOneTile(request.StartingBoard, request.PlayerCount))
            {
                return BadRequest("Every player needs at least one starting tile.");
            }
            if (!MapValidation.AllPlayersCanReachEachOther(request.StartingBoard, request.PlayerCount))
            {
                return BadRequest("Every player must be able to reach every other player - check for dead-tile walls sealing part of the board off.");
            }

            var map = new Map
            {
                CreationDate = DateTime.Now,
                Name = request.Name,
                DesignedByPlayerId = _userManager.GetUserId(User)!,
                PlayerCount = request.PlayerCount,
                ActionsPerTurn = request.ActionsPerTurn,
                StartingBoard = request.StartingBoard,
            };
            _context.Maps.Add(map);
            await _context.SaveChangesAsync();

            return Ok(map.Id);
        }
    }
}
