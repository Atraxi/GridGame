using GridGameAPI.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GridGameAPI.Database
{
    public class GameContext : DbContext
    {
        public DbSet<GridGame> GridGames { get; set; }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder
                .Properties<int[,]>()
                .HaveConversion<IntMultiDimensionalArrayConverter>();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseInMemoryDatabase("GridGames")
                .UseSeeding((context, _) =>
                {
                    context.Set<GridGame>().Add(SeedData());
                    context.SaveChanges();
                })
                .UseAsyncSeeding(async (context, _, cancellationToken) =>
                {
                    context.Set<GridGame>().Add(SeedData());
                    await context.SaveChangesAsync(cancellationToken);
                });
        }

        private static GridGame SeedData()
        {
            return new GridGame
            {
                Id = 1,
                CreationDate = DateTime.Now,
                Name = "DBSeed test game 1",
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
                GameBoard = new int[3, 4],
            };
        }
    }
}
