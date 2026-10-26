using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TradeSim.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "tradesim";

    [Required]
    public string Audience { get; set; } = "tradesim-api";

    [Required, MinLength(32)]
    public string Secret { get; set; } = "";

    [Range(1, 24 * 60)]
    public int ExpiryMinutes { get; set; } = 60;

    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Secret));
}
