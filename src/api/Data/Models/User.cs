namespace PortsideApi.Data.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string UserName { get; set; }

    public required string PasswordHash { get; set; }

    public required DateTime TimeStamp { get; set; }
}
