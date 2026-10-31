namespace GridGameAPI.Controllers
{
    public record RegisterModel(string UserName, string Email, string Password);

    public record LoginModel(string UserName, string Password);

    public record RefreshModel(string RefreshToken);

    public record RemoveOpponentModel(string OpponentId);
    public record PaletteSlot(string Primary, string? Secondary);

    public record SetDefaultPaletteModel(PaletteSlot[]? Slots);

    public record SetColoursModel(string? Primary, string? Secondary, bool UseDefaultColours);
    public record MergeAccountModel(string UserName, string Password);

    /// <param name="UserNameOrEmail">Either identifies the account - an email address can be shared by several</param>
    public record ForgotPasswordModel(string UserNameOrEmail);

    public record ResetPasswordModel(string Token, string Password);
}
