using GridGameAPI.Database;
using GridGameAPI.Model;
using GridGameAPI.UtilityInfrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

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
        private const int MAX_NAME_LENGTH = 100;
        private const string UNTITLED_DRAFT_NAME = "Untitled draft";

        private static readonly Expression<Func<Map, MapSummary>> ToSummary = map => new MapSummary
        {
            Id = map.Id,
            Name = map.Name,
            CreationDate = map.CreationDate,
            PlayerCount = map.PlayerCount,
            ActionsPerTurn = map.ActionsPerTurn,
            DesignedByUserName = map.DesignedBy!.UserName!,
            IsDraft = map.IsDraft,
        };

        /// <summary>Published maps only - the list games are hosted from. Drafts are only ever seen by their designer</summary>
        [HttpGet]
        public async Task<IEnumerable<MapSummary>> GetSummaries(int page)
        {
            return await _context.Maps
                .Where(map => !map.IsDraft)
                .OrderByDescending(map => map.CreationDate)
                .Skip((Math.Max(page, 1) - 1) * PAGE_SIZE)
                .Take(PAGE_SIZE)
                .Select(ToSummary)
                .ToListAsync();
        }

        /// <summary>The caller's own maps, drafts included</summary>
        [HttpGet]
        public async Task<IEnumerable<MapSummary>> GetMine()
        {
            var playerId = _userManager.GetUserId(User)!;
            return await _context.Maps
                .Where(map => map.DesignedByPlayerId == playerId)
                .OrderByDescending(map => map.CreationDate)
                .Select(ToSummary)
                .ToListAsync();
        }

        /// <summary>A whole map, board included, for the editor to open: to edit one of the caller's own, or to clone
        /// any published one. Someone else's draft is reported as not found rather than forbidden, so drafts don't
        /// leak their existence</summary>
        [HttpGet]
        public async Task<ActionResult> Get(int mapId)
        {
            var playerId = _userManager.GetUserId(User);
            var map = await _context.Maps
                .Include(map => map.DesignedBy)
                .SingleOrDefaultAsync(map => map.Id == mapId);
            if (map is null || (map.IsDraft && map.DesignedByPlayerId != playerId))
            {
                return NotFound("No map with that id exists.");
            }
            return Ok(new
            {
                map.Id,
                map.Name,
                map.PlayerCount,
                map.ActionsPerTurn,
                map.StartingBoard,
                map.IsDraft,
                DesignedByUserName = map.DesignedBy?.UserName,
                IsMine = map.DesignedByPlayerId == playerId,
            });
        }

        //Designing a map is a privilege of permanent named accounts, same as hosting games
        [Authorize(Policy = AuthPolicy.NamedAccountOnly)]
        [HttpPost]
        //Bounds the request body itself so a claimed board size can't force a huge allocation during JSON
        //deserialization before the dimension check below ever gets a chance to run
        [RequestSizeLimit(2_000_000)]
        public async Task<ActionResult> Create(SaveMapRequest request)
        {
            if (Validate(request) is { } error)
            {
                return BadRequest(error);
            }

            var map = new Map
            {
                CreationDate = DateTime.Now,
                Name = NameFor(request),
                DesignedByPlayerId = _userManager.GetUserId(User)!,
                PlayerCount = request.PlayerCount,
                ActionsPerTurn = request.ActionsPerTurn,
                StartingBoard = request.StartingBoard,
                IsDraft = request.IsDraft,
            };
            _context.Maps.Add(map);
            await _context.SaveChangesAsync();

            return Ok(map.Id);
        }

        /// <summary>Overwrites one of the caller's own maps, draft or published. Safe for a published map that games
        /// were already hosted from - CreateFromMap copies the board, so those games keep the one they started with</summary>
        [Authorize(Policy = AuthPolicy.NamedAccountOnly)]
        [HttpPost]
        [RequestSizeLimit(2_000_000)]
        public async Task<ActionResult> Update(int mapId, SaveMapRequest request)
        {
            var map = await _context.Maps.SingleOrDefaultAsync(map => map.Id == mapId);
            if (map is null || map.DesignedByPlayerId != _userManager.GetUserId(User))
            {
                return NotFound("No map with that id exists.");
            }
            if (Validate(request) is { } error)
            {
                return BadRequest(error);
            }

            map.Name = NameFor(request);
            map.PlayerCount = request.PlayerCount;
            map.ActionsPerTurn = request.ActionsPerTurn;
            map.StartingBoard = request.StartingBoard;
            map.IsDraft = request.IsDraft;
            await _context.SaveChangesAsync();

            return Ok(map.Id);
        }

        private static string NameFor(SaveMapRequest request) =>
            string.IsNullOrWhiteSpace(request.Name) ? UNTITLED_DRAFT_NAME : request.Name.Trim();

        /// <returns>Why the map can't be saved, or null if it can. The bounds apply to drafts too, since they're about
        /// what the server will store at all - only the playability checks wait until the map is published</returns>
        private static string? Validate(SaveMapRequest request)
        {
            if (request.PlayerCount < MIN_PLAYERS || request.PlayerCount > MAX_PLAYERS)
            {
                return $"Player count must be between {MIN_PLAYERS} and {MAX_PLAYERS}.";
            }
            if (request.ActionsPerTurn < MIN_ACTIONS_PER_TURN || request.ActionsPerTurn > MAX_ACTIONS_PER_TURN)
            {
                return $"Actions per turn must be between {MIN_ACTIONS_PER_TURN} and {MAX_ACTIONS_PER_TURN}.";
            }
            if (request.Name?.Trim().Length > MAX_NAME_LENGTH)
            {
                return $"Map names can be at most {MAX_NAME_LENGTH} characters.";
            }

            var rows = request.StartingBoard.GetLength(0);
            var columns = request.StartingBoard.GetLength(1);
            if (rows < 1 || columns < 1 || rows > MAX_BOARD_DIMENSION || columns > MAX_BOARD_DIMENSION)
            {
                return $"Board dimensions must be between 1 and {MAX_BOARD_DIMENSION}.";
            }
            foreach (var value in request.StartingBoard)
            {
                if (value < TileState.Dead || value > request.PlayerCount)
                {
                    return "Every tile must be dead, unclaimed, or owned by one of the map's players.";
                }
            }

            if (request.IsDraft)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return "A published map needs a name.";
            }
            if (!MapValidation.AllPlayersHaveAtLeastOneTile(request.StartingBoard, request.PlayerCount))
            {
                return "Every player needs at least one starting tile.";
            }
            if (!MapValidation.AllPlayersCanReachEachOther(request.StartingBoard, request.PlayerCount))
            {
                return "Every player must be able to reach every other player - check for dead-tile walls sealing part of the board off.";
            }
            return null;
        }
    }
}
