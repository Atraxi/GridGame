namespace GridGameAPI.Controllers
{
    public record RegisterModel(string UserName, string Email, string Password);

    public record LoginModel(string UserName, string Password);

    public record RefreshModel(string RefreshToken);

    public record RemoveOpponentModel(string OpponentId);

    /// <param name="UserNameOrEmail">Either identifies the account - an email address can be shared by several</param>
    public record ForgotPasswordModel(string UserNameOrEmail);

    public record ResetPasswordModel(string Token, string Password);
}
