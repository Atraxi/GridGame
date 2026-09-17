using GridGameAPI.Model;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GridGameAPI.Database
{
    public class GameContext(DbContextOptions<GameContext> options) : IdentityDbContext<Player>(options)
    {
        public DbSet<GridGame> GridGames { get; set; }

        public DbSet<Map> Maps { get; set; }

        public DbSet<RefreshToken> RefreshTokens { get; set; }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder
                .Properties<int[,]>()
                .HaveConversion<IntMultiDimensionalArrayConverter>();
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Map>()
                .HasOne(map => map.DesignedBy)
                .WithMany()
                .HasForeignKey(map => map.DesignedByPlayerId);

            builder.Entity<RefreshToken>()
                .HasOne(refreshToken => refreshToken.Player)
                .WithMany()
                .HasForeignKey(refreshToken => refreshToken.PlayerId);

            builder.Entity<RefreshToken>()
                .HasIndex(refreshToken => refreshToken.TokenHash)
                .IsUnique();
        }

        /// <summary>Used once at startup to give a freshly-created database something to show; see Program.cs</summary>
        internal static GridGame SeedData()
        {
            return new GridGame
            {
                //Id and the Players' Id are left unset so SQL Server assigns the GridGames identity and Identity
                //assigns each Player's GUID itself - setting them explicitly here caused an IDENTITY_INSERT failure,
                //since EF then tries to insert AspNetUsers rows carrying a GridGameId FK before the identity value exists
                CreationDate = DateTime.Now,
                Name = "DBSeed test game 1",
                TurnNumber = 0,
                PlayerCount = 2,
                ActionsPerTurn = 3,
                Players = [
                    new Player { UserName = "Test player 1" },
                    new Player { UserName = "Test player 2" },
                ],
                GameBoard = SeedBoard(),
            };
        }

        //A 20x20 board: player 1 starts in the top-left corner, player 2 in the bottom-right, with a small dead-ground
        //cluster near the centre for terrain - well short of a full wall, so the two are always able to reach each other
        private static int[,] SeedBoard()
        {
            const int size = 20;
            var board = new int[size, size];

            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    board[x, y] = 1;
                    board[size - 1 - x, size - 1 - y] = 2;
                }
            }

            foreach (var (x, y) in new[] { (9, 9), (9, 10), (10, 9), (10, 10) })
            {
                board[x, y] = -1;
            }

            return board;
        }
    }
}
