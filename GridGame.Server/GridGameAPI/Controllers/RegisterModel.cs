namespace GridGameAPI.Controllers
{
    public record RegisterModel(string UserName, string Email, string Password);

    public record LoginModel(string UserName, string Password);

    public record RefreshModel(string RefreshToken);
}
