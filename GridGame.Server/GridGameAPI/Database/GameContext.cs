using GridGameAPI.Model;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace GridGameAPI.Database
{
    public class GameContext(DbContextOptions<GameContext> options) : IdentityDbContext<Player>(options)
    {
        public DbSet<GridGame> GridGames { get; set; }

        public DbSet<Map> Maps { get; set; }

        public DbSet<RefreshToken> RefreshTokens { get; set; }

        public DbSet<GameSeat> GameSeats { get; set; }

        public DbSet<GameInvite> GameInvites { get; set; }

        public DbSet<PastOpponent> PastOpponents { get; set; }

        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder
                .Properties<int[,]>()
                .HaveConversion<IntMultiDimensionalArrayConverter, IntMultiDimensionalArrayComparer>();
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

            builder.Entity<PasswordResetToken>()
                .HasOne(token => token.Player)
                .WithMany()
                .HasForeignKey(token => token.PlayerId);
            builder.Entity<PasswordResetToken>()
                .HasIndex(token => token.TokenHash)
                .IsUnique();

            //Which map a game was hosted from, so a rematch can be hosted on it again. Maps are never deleted, so this
            //doesn't cascade - SQL Server would refuse it anyway, since Maps already cascades from its designer
            builder.Entity<GridGame>()
                .HasOne<Map>()
                .WithMany()
                .HasForeignKey(game => game.MapId)
                .OnDelete(DeleteBehavior.NoAction);

            //Neither table is reachable by navigation from GridGame: the hub force-saves the whole game entity graph
            //(see GridGameHub.PersistGame), so seats and invites are written explicitly instead of riding along with it
            builder.Entity<GameSeat>()
                .HasKey(seat => new { seat.GridGameId, seat.PlayerNumber });
            builder.Entity<GameSeat>()
                .HasIndex(seat => new { seat.GridGameId, seat.PlayerId })
                .IsUnique();
            builder.Entity<GameSeat>()
                .HasOne<GridGame>()
                .WithMany()
                .HasForeignKey(seat => seat.GridGameId);
            builder.Entity<GameSeat>()
                .HasOne(seat => seat.Player)
                .WithMany()
                .HasForeignKey(seat => seat.PlayerId);

            builder.Entity<GameInvite>()
                .HasKey(invite => new { invite.GridGameId, invite.PlayerId });
            builder.Entity<GameInvite>()
                .HasOne(invite => invite.Game)
                .WithMany()
                .HasForeignKey(invite => invite.GridGameId);
            builder.Entity<GameInvite>()
                .HasOne<Player>()
                .WithMany()
                .HasForeignKey(invite => invite.PlayerId);

            builder.Entity<PastOpponent>()
                .HasKey(record => new { record.PlayerId, record.OpponentId });
            builder.Entity<PastOpponent>()
                .HasOne<Player>()
                .WithMany()
                .HasForeignKey(record => record.PlayerId);
            //SQL Server refuses two cascading foreign keys from one table to the same parent ("multiple cascade
            //paths"), so this side doesn't cascade. Only named accounts are ever recorded, and those are never deleted
            builder.Entity<PastOpponent>()
                .HasOne(record => record.Opponent)
                .WithMany()
                .HasForeignKey(record => record.OpponentId)
                .OnDelete(DeleteBehavior.NoAction);

            //One JSON column of compact int arrays rather than a row per action: the hub force-saves the whole game
            //entity (see GridGameHub.PersistGame), which keeps the history in step with the board for free, and a
            //game's history is small - even a long game is a few thousand actions at ~25 bytes each
            builder.Entity<GridGame>()
                .Property(game => game.MoveLog)
                .HasConversion(
                    log => JsonSerializer.Serialize(log.Select(action => action.ToStorage()), (JsonSerializerOptions?)null),
                    //Empty for games created before history was recorded - the migration adds the column with a "" default
                    json => string.IsNullOrEmpty(json)
                        ? new List<GameAction>()
                        : JsonSerializer.Deserialize<List<int[]>>(json, (JsonSerializerOptions?)null)!.Select(GameAction.FromStorage).ToList(),
                    new ValueComparer<List<GameAction>>(
                        (a, b) => a!.SequenceEqual(b!),
                        log => log.Aggregate(0, (hash, action) => HashCode.Combine(hash, action)),
                        log => log.ToList()));
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
