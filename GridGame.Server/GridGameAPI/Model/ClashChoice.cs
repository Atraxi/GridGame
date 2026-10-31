namespace GridGameAPI.Model
{
    /// <summary>What one account decided when players' chosen colours clashed in a game: whether to see clashing seats
    /// in their own default colours instead. Per viewer, so players can answer differently. Covers every clash that
    /// game, including later ones</summary>
    public class ClashChoice
    {
        public int GridGameId { get; set; }

        public required string PlayerId { get; set; }

        public bool SwitchToDefaults { get; set; }
    }
}
