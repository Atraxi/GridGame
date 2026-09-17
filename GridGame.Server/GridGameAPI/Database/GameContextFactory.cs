using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GridGameAPI.Database
{
    /// <summary>Lets `dotnet ef` build GameContext for migrations without spinning up the whole app or needing a live database</summary>
    public class GameContextFactory : IDesignTimeDbContextFactory<GameContext>
    {
        public GameContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<GameContext>();
            optionsBuilder.UseSqlServer("Server=localhost;Database=GridGame;Trusted_Connection=True;TrustServerCertificate=True;");
            return new GameContext(optionsBuilder.Options);
        }
    }
}
