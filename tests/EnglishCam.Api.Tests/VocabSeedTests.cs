using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;

namespace EnglishCam.Api.Tests;

/// <summary>Chỉ đọc: DB mới tinh vừa seed xong. Kiểm tra API trả đúng cái app mong đợi.</summary>
public class VocabSeedTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private static string SeedPath(string name) => Path.Combine(AppContext.BaseDirectory, "Data", "Seed", name);

    private async Task<(JsonNode Vocab, byte[] Bytes)> GetVocabAsync()
    {
        var bytes = await f.Anonymous().GetByteArrayAsync("/api/vocab");
        return (JsonNode.Parse(bytes)!, bytes);
    }

    [Fact]
    public async Task Vocab_is_public_and_identical_to_the_json_shipped_in_the_app()
    {
        var (vocab, _) = await GetVocabAsync();
        var seed = JsonNode.Parse(File.ReadAllText(SeedPath("vocab.json")))!;
        Assert.Equal(60, vocab["words"]!.AsArray().Count);
        Assert.Equal(11, vocab["categories"]!.AsArray().Count);
        Assert.True(JsonNode.DeepEquals(seed, vocab), "vocab.json do API phát khác bản đóng gói trong app");
    }

    [Fact]
    public async Task Served_vocab_passes_the_same_rules_the_app_enforces_on_load()
    {
        var (vocab, _) = await GetVocabAsync();
        var cats = vocab["categories"]!.AsArray().Select(c => (string)c!["id"]!).ToHashSet();
        var ids = new HashSet<string>();
        var labels = new HashSet<string>();
        var pictured = 0;
        foreach (var w in vocab["words"]!.AsArray())
        {
            Assert.True(ids.Add((string)w!["id"]!), "trùng id");
            Assert.Contains((string)w["category"]!, cats);
            var label = (string?)w["label"];
            if (label is not null) Assert.True(labels.Add(label), "trùng label");
            else Assert.Null(w["hunt"]); // hunt chỉ có ở từ có nhãn
            foreach (var f in new[] { "en", "vi", "phonetic", "exampleEn", "exampleVi", "audio", "category" })
                Assert.False(string.IsNullOrWhiteSpace((string?)w[f]), $"{ids.Last()} thiếu {f}");
            if (w["image"] is not null || w["emoji"] is not null) pictured++;
        }
        Assert.True(pictured >= 4);
        Assert.Equal(24, labels.Count);
        Assert.Equal(["apple", "table"], vocab["words"]!.AsArray().Where(w => (bool?)w!["hunt"] == false).Select(w => (string)w!["id"]!).Order());
    }

    [Fact]
    public async Task Pack_manifest_matches_the_bytes_of_vocab_json_exactly()
    {
        var anon = f.Anonymous();
        var manifest = JsonNode.Parse(await anon.GetStringAsync("/api/pack/manifest.json"))!;
        var vocabBytes = await anon.GetByteArrayAsync("/api/pack/vocab.json");
        Assert.Equal(1, (int)manifest["version"]!);
        var entry = Assert.Single(manifest["files"]!.AsArray()); // 60 từ đều dùng asset đã đóng gói sẵn trong APK
        Assert.Equal("vocab.json", (string)entry!["path"]!);
        Assert.Equal(vocabBytes.Length, (int)entry["size"]!);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(vocabBytes)).ToLowerInvariant(), (string)entry["sha256"]!);
        Assert.Equal(vocabBytes, await anon.GetByteArrayAsync("/api/vocab")); // /api/vocab và /api/pack/vocab.json là cùng một bản
    }

    [Fact]
    public async Task Etag_lets_the_app_skip_downloading_an_unchanged_vocab()
    {
        var anon = f.Anonymous();
        var first = await anon.GetAsync("/api/vocab");
        var etag = first.Headers.ETag!.Tag;
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/vocab");
        req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.NotModified, (await anon.SendAsync(req)).StatusCode);
        var version = await anon.GetFromJsonAsync<JsonNode>("/api/vocab/version");
        Assert.Equal(1, (int)version!["version"]!);
    }

    [Fact]
    public async Task Unknown_pack_paths_and_traversal_are_404()
    {
        var anon = f.Anonymous();
        foreach (var p in new[] { "audio/words/nope.mp3", "..%2Fappsettings.json", "audio/../appsettings.json", "assets/audio/words/book.mp3" })
            Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/pack/{p}")).StatusCode);
    }

    [Fact]
    public void Model_labels_in_the_api_match_the_real_models_labels_txt()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "models", "labels.txt"))) dir = dir.Parent;
        if (dir is null) return; // chạy ngoài repo ML: bỏ qua
        var real = File.ReadAllLines(Path.Combine(dir.FullName, "models", "labels.txt")).Select(l => l.Trim()).Where(l => l.Length > 0);
        var api = File.ReadAllLines(SeedPath("model_labels.txt")).Select(l => l.Trim()).Where(l => l.Length > 0);
        Assert.Equal(real.Order(), api.Order());
        Assert.Equal(real.Order(), EnglishCam.Api.Services.ModelLabels.All.Order());
    }
}
