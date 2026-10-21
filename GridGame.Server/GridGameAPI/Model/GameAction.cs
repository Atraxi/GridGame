namespace GridGameAPI.Model
{
    public enum GameActionKind
    {
        Spawn = 0,
        Jump = 1,
        Resign = 2,
    }

    /// <summary>
    /// One entry in a game's move history (GridGame.MoveLog). Records what each action overwrote rather than just
    /// where it went, so the history can be stepped backwards from the current board - the map a game was hosted from
    /// may since have been edited, so replaying forwards from its starting board isn't reliable.
    ///
    /// Undoing a spawn or jump puts Before back on (X, Y), and for a jump puts Player back on the origin, which is
    /// always the mover's own tile beforehand. A resignation changes no tiles.
    /// </summary>
    /// <param name="TurnNumber">GridGame.TurnNumber when the action was taken</param>
    /// <param name="Before">What (X, Y) held before the action landed there: unclaimed, or an enemy player's number</param>
    public record GameAction(int TurnNumber, int Player, GameActionKind Kind, int X, int Y, int Before, int OriginX, int OriginY)
    {
        public static GameAction Resignation(int turnNumber, int player) =>
            new(turnNumber, player, GameActionKind.Resign, -1, -1, 0, -1, -1);

        /// <summary>Stored as a flat array of ints - a whole game's history is rewritten each time the game is
        /// persisted, so it's kept compact: about 25 bytes an action</summary>
        public int[] ToStorage() => [TurnNumber, Player, (int)Kind, X, Y, Before, OriginX, OriginY];

        public static GameAction FromStorage(int[] values) =>
            new(values[0], values[1], (GameActionKind)values[2], values[3], values[4], values[5], values[6], values[7]);

        public object ToWire() => new
        {
            turnNumber = TurnNumber,
            player = Player,
            kind = Kind switch
            {
                GameActionKind.Spawn => "spawn",
                GameActionKind.Jump => "jump",
                _ => "resign",
            },
            x = X,
            y = Y,
            before = Before,
            originX = Kind == GameActionKind.Jump ? OriginX : (int?)null,
            originY = Kind == GameActionKind.Jump ? OriginY : (int?)null,
        };
    }
}
