namespace GridGameAPI.UtilityInfrastructure
{
    public static class AuthPolicy
    {
        public const string AnyUser = nameof(AnyUser);
        public const string AdminOnly = nameof(AdminOnly);
        public const string NamedAccountOnly = nameof(NamedAccountOnly);
    }
    public static class AppRole
    {
        public const string Admin = nameof(Admin);
    }
    public static class Constants
    {
        public const string GuestUserNamePrefix = "Guest-";

        /// <summary>Claim distinguishing a permanent named account from a disposable guest one</summary>
        public const string AccountTypeClaim = "accountType";
        public const string NamedAccountType = "Named";
        public const string GuestAccountType = "Guest";
    }
    /// <summary>A board cell holds the number of the player owning it, or one of these</summary>
    public static class TileState
    {
        public const int Dead = -1;
        public const int Unclaimed = 0;
    }
}
