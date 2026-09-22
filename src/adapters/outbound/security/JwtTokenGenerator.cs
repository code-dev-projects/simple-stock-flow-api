using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SimpleStockFlow.Application.Ports.Outbound;
using SimpleStockFlow.Domain.Identity;

namespace SimpleStockFlow.Adapters.Security;

internal sealed class JwtTokenGenerator : ITokenGenerator
{
    private const string RoleClaimName = "role";

    private readonly JwtOptions _options;

    public JwtTokenGenerator(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
            throw new InvalidOperationException("Jwt:SigningKey is not configured.");
    }

    public (string Token, DateTimeOffset ExpiresAt) Generate(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.LifetimeMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            // E-01 names this claim "role", and the JwtSecurityToken constructor writes claims
            // to the payload untouched -- it never runs the outbound map that CreateToken would.
            // ClaimTypes.Role would therefore ship the WS-* URI on the wire, a second spelling
            // no document declares. Authorization is unaffected: the bearer handler maps "role"
            // back to ClaimTypes.Role on the way in, which RoleClaimType still points at.
            new Claim(RoleClaimName, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
