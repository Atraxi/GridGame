using GridGameAPI.Database;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GridGameAPI.ActiveGames.SignalRHubs
{
    public class GridGameHub(GameSessionManager _sessionManager, GameContext _gameContext) : Hub
    {
        public void OnConnectedToGame(int gameId, int userId)
        {
            var session = _sessionManager.GetByGameIdOrCreate(gameId, _ => {
                var game = _gameContext.GridGames
                    .AsNoTracking()
                    .Single(game => game.Id == gameId);
                _gameContext.Entry(game);
                return new GameSession
                {
                    GridGame = game,
                };
            });
            session.Connections.Add(Context.ConnectionId, userId);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);
            session.Connections.Remove(Context.ConnectionId);
            if (session.Connections.Count == 0)
            {
                _sessionManager.Remove(session.GridGame.Id);
                _gameContext.Attach(session.GridGame);
                await _gameContext.SaveChangesAsync();
            }
        }

        public async Task OnTileClicked(int x, int y)
        {
            var session = _sessionManager.GetByConnectionId(Context.ConnectionId);

            session.GridGame.GameBoard[x, y]++;

            await Clients.Clients(session.Connections.Keys)
                .SendAsync("tileUpdateProcessed", new { x, y, newTileValue = session.GridGame.GameBoard[x, y] });
        }
    }
}
