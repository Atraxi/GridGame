namespace GridGameAPI.Bots
{
    /// <summary>Finds every IBot in this assembly, keyed by class name</summary>
    public class BotRegistry
    {
        private readonly Dictionary<string, Type> _types = typeof(IBot).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false } && type.IsAssignableTo(typeof(IBot)) &&
                           type.GetConstructor(Type.EmptyTypes) is not null)
            .ToDictionary(type => type.Name);

        public IEnumerable<string> Names => _types.Keys.Order();

        /// <returns>A new bot, or null if there is no bot with this name (they get renamed and removed)</returns>
        public IBot? Create(string name) =>
            _types.TryGetValue(name, out var type) ? (IBot)Activator.CreateInstance(type)! : null;
    }
}
