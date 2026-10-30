using System.Text.Json.Nodes;
using EnglishCam.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Data;

/// <summary>DB chưa có huy hiệu nào thì nạp từ Data/Seed/badges.json (8 huy hiệu đóng gói trong app + 2 huy hiệu chuỗi ngày).</summary>
public static class BadgeSeeder
{
    public static async Task SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        if (await db.Badges.AnyAsync()) return;
        var root = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "badges.json")))!;
        var order = 0;
        foreach (var b in root["badges"]!.AsArray())
            db.Badges.Add(new Badge
            {
                Id = (string)b!["id"]!, Name = (string)b["name"]!, Description = (string)b["description"]!,
                Emoji = (string)b["emoji"]!, Metric = (string)b["metric"]!, Threshold = (int)b["threshold"]!, SortOrder = order++
            });
        await db.SaveChangesAsync();
    }
}
