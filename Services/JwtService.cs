using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EnglishCam.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace EnglishCam.Api.Services;

public record IssuedToken(string Token, DateTime ExpiresAt);

public class JwtService(IConfiguration cfg)
{
    /// <summary>Claim chứa SecurityStamp của user; đổi mật khẩu thì stamp đổi và mọi token cũ mất hiệu lực.</summary>
    public const string StampClaim = "sstamp";

    /// <summary>Claim chứa id phiên thiết bị của bé (chỉ token role Child có).</summary>
    public const string ChildSessionClaim = "csid";

    public IssuedToken Create(AppUser user, IEnumerable<string> roles, Guid? childSessionId = null)
    {
        var roleList = roles.ToList();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(StampClaim, user.SecurityStamp ?? "")
        };
        if (childSessionId is not null) claims.Add(new Claim(ChildSessionClaim, childSessionId.Value.ToString()));
        claims.AddRange(roleList.Select(r => new Claim(ClaimTypes.Role, r)));

        var lifetime = roleList.Contains(Roles.Child)
            ? TimeSpan.FromDays(int.Parse(cfg["Jwt:ChildExpireDays"] ?? "30"))
            : TimeSpan.FromMinutes(int.Parse(cfg["Jwt:ParentExpireMinutes"] ?? "10080"));
        var expires = DateTime.UtcNow + lifetime;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
        var token = new JwtSecurityToken(
            issuer: cfg["Jwt:Issuer"], audience: cfg["Jwt:Audience"], claims: claims, expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
