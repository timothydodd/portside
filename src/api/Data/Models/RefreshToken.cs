namespace PortsideApi.Data.Models;

public class RefreshToken
{
    public int Id { get; set; }

    public required string Token { get; set; }

    public required Guid UserId { get; set; }

    public DateTime ExpiryDate { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedDate { get; set; }
}
