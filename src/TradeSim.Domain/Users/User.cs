namespace TradeSim.Domain.Users;

public sealed class User
{
    private User() { }

    public Guid Id { get; private set; }
    public string Username { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;

    public static User Create(string username, string passwordHash) => new()
    {
        Id = Guid.CreateVersion7(),
        Username = username,
        PasswordHash = passwordHash,
    };
}
