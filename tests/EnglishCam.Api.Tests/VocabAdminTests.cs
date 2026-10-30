using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using EnglishCam.Api.Dtos;
using Xunit;

namespace EnglishCam.Api.Tests;

public class VocabAdminTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private static string NewId(string prefix = "w") => $"{prefix}_{Guid.NewGuid():N}"[..14];

    private static object Word(string id, string audio, string category = "animals", string? label = null, string? image = null,
        string? emoji = "🦒", bool? hunt = null) => new
    {
        id, label, en = id, vi = "từ thử", phonetic = "/test/", exampleEn = "A test.", exampleVi = "Một câu thử.",
        category, audio, image, emoji, hunt
    };

    private async Task<Session> Admin() => await f.LoginAdminAsync();

    private static async Task<int> LiveVersion(HttpClient c) =>
        (int)(await c.GetFromJsonAsync<JsonNode>("/api/vocab/version"))!["version"]!;

    private async Task<string> UploadAudioAsync(Session admin, string id, int seed = 1)
    {
        var path = $"audio/words/{id}.mp3";
        (await ApiFactory.UploadAsync(admin, path, ApiFactory.FakeMp3(seed))).EnsureSuccessStatusCode();
        return path;
    }

    [Fact]
    public async Task Only_admins_can_write_and_the_public_can_read()
    {
        var parent = await f.RegisterParentAsync();
        var anon = f.Anonymous();
        var body = Word("x_nope", "audio/words/x.mp3");
        foreach (var (method, url) in new[] { (HttpMethod.Post, "/api/admin/words"), (HttpMethod.Post, "/api/admin/vocab/publish"),
                     (HttpMethod.Get, "/api/admin/words"), (HttpMethod.Get, "/api/admin/media"), (HttpMethod.Get, "/api/admin/vocab/status") })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await parent.Client.SendAsync(new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync("/api/vocab")).StatusCode);
    }

    [Fact]
    public async Task Creating_a_word_enforces_the_model_contract_and_media_rules()
    {
        var admin = await Admin();
        string[] Errors(HttpResponseMessage r) => r.Content.ReadFromJsonAsync<JsonNode>().Result!["errors"]!.AsArray().Select(e => (string)e!).ToArray();

        // nhãn không thuộc model
        var r1 = await admin.Client.PostAsJsonAsync("/api/admin/words", Word(NewId(), "assets/audio/words/book.mp3", label: "unicorn"));
        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);
        Assert.Contains(Errors(r1), e => e.Contains("unicorn"));

        // nhãn hợp lệ nhưng đã gán cho từ khác ("book")
        var r2 = await admin.Client.PostAsJsonAsync("/api/admin/words", Word(NewId(), "assets/audio/words/book.mp3", label: "book"));
        Assert.Contains(Errors(r2), e => e.Contains("đã gán"));

        // chủ đề không có, file âm thanh chưa tải lên
        var r3 = await admin.Client.PostAsJsonAsync("/api/admin/words", Word(NewId(), "audio/words/missing.mp3", category: "khong_co"));
        Assert.Equal(2, Errors(r3).Length);

        // id sai định dạng (ví dụ cố chèn đường dẫn) bị chặn bởi validation
        var r4 = await admin.Client.PostAsJsonAsync("/api/admin/words", Word("../evil", "assets/audio/words/book.mp3"));
        Assert.Equal(HttpStatusCode.BadRequest, r4.StatusCode);

        // đã có id này
        var r5 = await admin.Client.PostAsJsonAsync("/api/admin/words", Word("book", "assets/audio/words/book.mp3"));
        Assert.Equal(HttpStatusCode.Conflict, r5.StatusCode);
    }

    [Fact]
    public async Task Word_crud_and_soft_delete_with_restore()
    {
        var admin = await Admin();
        var id = NewId();
        var audio = await UploadAudioAsync(admin, id);
        var created = await admin.Client.PostAsJsonAsync("/api/admin/words", Word(id, audio));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = (await created.Content.ReadFromJsonAsync<WordDto>())!;
        Assert.False(dto.Hunt); // không nhãn thì không dùng cho game Tìm vật

        var upd = await admin.Client.PutAsJsonAsync($"/api/admin/words/{id}", Word(id, audio).with2("vi", "đã sửa"));
        Assert.Equal("đã sửa", (await upd.Content.ReadFromJsonAsync<WordDto>())!.Vi);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PutAsJsonAsync($"/api/admin/words/{id}", Word("khac_id", audio))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync($"/api/admin/words/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.PutAsJsonAsync($"/api/admin/words/{id}", Word(id, audio))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsJsonAsync("/api/admin/words", Word(id, audio))).StatusCode);
        var listed = await admin.Client.GetFromJsonAsync<PagedResult<WordDto>>($"/api/admin/words?search={id}&includeDeleted=true");
        Assert.True(Assert.Single(listed!.Items).IsDeleted);
        Assert.Empty((await admin.Client.GetFromJsonAsync<PagedResult<WordDto>>($"/api/admin/words?search={id}"))!.Items);

        var restored = await admin.Client.PostAsync($"/api/admin/words/{id}/restore", null);
        Assert.False((await restored.Content.ReadFromJsonAsync<WordDto>())!.IsDeleted);
    }

    [Fact]
    public async Task A_label_can_move_to_another_word_after_the_first_one_is_deleted()
    {
        var admin = await Admin();
        // Mượn nhãn "pen": xoá từ pen rồi gán nhãn cho từ mới.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync("/api/admin/words/pen")).StatusCode);
        var id = NewId();
        var created = await admin.Client.PostAsJsonAsync("/api/admin/words", Word(id, "assets/audio/words/pen.mp3", category: "school", label: "pen"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True((await created.Content.ReadFromJsonAsync<WordDto>())!.Hunt);
        // pen cũ không khôi phục được vì nhãn đang bị từ mới giữ
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsync("/api/admin/words/pen/restore", null)).StatusCode);
    }

    [Fact]
    public async Task Categories_cannot_be_deleted_while_they_still_have_words()
    {
        var admin = await Admin();
        var id = NewId("c");
        Assert.Equal(HttpStatusCode.Created, (await admin.Client.PostAsJsonAsync("/api/admin/categories",
            new { id, vi = "Thử", en = "Test", emoji = "🧪", color = "#AABBCC" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsJsonAsync("/api/admin/categories", new { id, vi = "Thử", en = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.PostAsJsonAsync("/api/admin/categories",
            new { id = NewId("c"), vi = "Thử", en = "Test", color = "xanh" })).StatusCode);

        var wid = NewId();
        var audio = await UploadAudioAsync(admin, wid);
        await admin.Client.PostAsJsonAsync("/api/admin/words", Word(wid, audio, category: id));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.DeleteAsync($"/api/admin/categories/{id}")).StatusCode);
        await admin.Client.DeleteAsync($"/api/admin/words/{wid}");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync($"/api/admin/categories/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.PostAsync($"/api/admin/categories/{id}/restore", null)).StatusCode);
    }

    [Fact]
    public async Task Uploads_are_validated_by_path_size_and_real_content()
    {
        var admin = await Admin();
        async Task<HttpStatusCode> Up(string path, byte[] bytes) => (await ApiFactory.UploadAsync(admin, path, bytes)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Up("../evil.mp3", ApiFactory.FakeMp3()));
        Assert.Equal(HttpStatusCode.BadRequest, await Up("audio/words/../../x.mp3", ApiFactory.FakeMp3()));
        Assert.Equal(HttpStatusCode.BadRequest, await Up("/audio/words/x.mp3", ApiFactory.FakeMp3()));
        Assert.Equal(HttpStatusCode.BadRequest, await Up("assets/audio/words/x.mp3", ApiFactory.FakeMp3())); // không giả danh asset đóng gói
        Assert.Equal(HttpStatusCode.BadRequest, await Up("audio/words/x.exe", ApiFactory.FakeMp3()));
        Assert.Equal(HttpStatusCode.BadRequest, await Up("other/x.mp3", ApiFactory.FakeMp3()));
        Assert.Equal(HttpStatusCode.BadRequest, await Up("audio/words/x.mp3", ApiFactory.FakePng())); // đuôi mp3 mà ruột là png
        Assert.Equal(HttpStatusCode.BadRequest, await Up("images/x.png", ApiFactory.FakeMp3()));
        Assert.Equal(HttpStatusCode.BadRequest, await Up("audio/words/x.mp3", []));
        Assert.Equal(HttpStatusCode.BadRequest, await Up("audio/words/big.mp3", ApiFactory.FakeMp3(length: 1_100_000)));
        Assert.Equal(HttpStatusCode.Created, await Up($"images/{NewId("i")}.png", ApiFactory.FakePng()));
    }

    [Fact]
    public async Task Edits_stay_in_draft_until_published_and_publishing_pins_exact_bytes()
    {
        var admin = await Admin();
        var anon = f.Anonymous();
        var startVersion = await LiveVersion(anon);
        var before = await anon.GetByteArrayAsync("/api/vocab");

        // 1) Thêm từ mới kèm âm thanh: bản nháp có thay đổi nhưng app chưa thấy gì.
        var id = NewId();
        var audio = await UploadAudioAsync(admin, id, seed: 7);
        (await admin.Client.PostAsJsonAsync("/api/admin/words", Word(id, audio))).EnsureSuccessStatusCode();
        var status = await admin.Client.GetFromJsonAsync<VocabStatusDto>("/api/admin/vocab/status");
        Assert.True(status!.HasUnpublishedChanges);
        Assert.Equal(before, await anon.GetByteArrayAsync("/api/vocab"));
        Assert.Equal(startVersion, await LiveVersion(anon));

        // 2) Xuất bản: version +1, manifest liệt kê file mới với đúng sha256 và size.
        var pub = await admin.Client.PostAsJsonAsync("/api/admin/vocab/publish", new { notes = "Thêm từ thử" });
        Assert.Equal(HttpStatusCode.Created, pub.StatusCode);
        Assert.Equal(startVersion + 1, await LiveVersion(anon));
        Assert.False((await admin.Client.GetFromJsonAsync<VocabStatusDto>("/api/admin/vocab/status"))!.HasUnpublishedChanges);

        var manifest = JsonNode.Parse(await anon.GetStringAsync("/api/pack/manifest.json"))!;
        Assert.Equal("Thêm từ thử", (string)manifest["notes"]!);
        var entry = manifest["files"]!.AsArray().Single(x => (string)x!["path"]! == audio)!;
        var served = await anon.GetByteArrayAsync($"/api/pack/{audio}");
        Assert.Equal(ApiFactory.FakeMp3(7), served);
        Assert.Equal(served.Length, (int)entry["size"]!);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(served)).ToLowerInvariant(), (string)entry["sha256"]!);

        var vocab = JsonNode.Parse(await anon.GetStringAsync("/api/vocab"))!;
        Assert.Contains(vocab["words"]!.AsArray(), w => (string)w!["id"]! == id && (string)w["audio"]! == audio);

        // 3) Tải đè cùng đường dẫn: bản đang phát vẫn giữ đúng byte cũ cho đến khi xuất bản lại.
        (await ApiFactory.UploadAsync(admin, audio, ApiFactory.FakeMp3(99))).EnsureSuccessStatusCode();
        Assert.Equal(ApiFactory.FakeMp3(7), await anon.GetByteArrayAsync($"/api/pack/{audio}"));
        Assert.True((await admin.Client.GetFromJsonAsync<VocabStatusDto>("/api/admin/vocab/status"))!.HasUnpublishedChanges);
        Assert.Equal(HttpStatusCode.Created, (await admin.Client.PostAsJsonAsync("/api/admin/vocab/publish", new { })).StatusCode);
        Assert.Equal(ApiFactory.FakeMp3(99), await anon.GetByteArrayAsync($"/api/pack/{audio}"));

        // 4) Xuất bản lần nữa khi không có gì đổi → 409.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.PostAsJsonAsync("/api/admin/vocab/publish", new { })).StatusCode);
    }

    [Fact]
    public async Task Media_in_use_cannot_be_deleted_and_bundled_assets_are_protected()
    {
        var admin = await Admin();
        var id = NewId();
        var audio = await UploadAudioAsync(admin, id, seed: 5);
        (await admin.Client.PostAsJsonAsync("/api/admin/words", Word(id, audio))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Client.DeleteAsync($"/api/admin/media?path={audio}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.DeleteAsync("/api/admin/media?path=assets/audio/words/book.mp3")).StatusCode);
        await admin.Client.DeleteAsync($"/api/admin/words/{id}");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync($"/api/admin/media?path={audio}")).StatusCode);
    }

    [Fact]
    public async Task Model_labels_endpoint_lists_all_24_and_who_uses_them()
    {
        var admin = await Admin();
        var labels = (await admin.Client.GetFromJsonAsync<JsonNode>("/api/admin/vocab/model-labels"))!.AsArray();
        Assert.Equal(24, labels.Count);
        Assert.Contains(labels, l => (string)l!["label"]! == "book" && (string?)l["usedBy"] == "book");
    }
}

/// <summary>Làm hỏng bản nháp một cách có chủ ý nên cần DB riêng, không dùng chung với các test khác.</summary>
public class VocabPublishValidationTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Publish_refuses_a_draft_the_app_would_reject()
    {
        var admin = await f.LoginAdminAsync();
        // Làm hỏng bản nháp từ bên dưới API: một file âm thanh biến mất khỏi bảng media.
        var id = $"w_{Guid.NewGuid():N}"[..14];
        var audio = await Upload(admin, id);
        (await admin.Client.PostAsJsonAsync("/api/admin/words", new { id, label = (string?)null, en = id, vi = "từ thử", phonetic = "/t/", exampleEn = "A.", exampleVi = "B.", category = "animals", audio, image = (string?)null, emoji = "🦒", hunt = (bool?)null })).EnsureSuccessStatusCode();
        await f.WithDbAsync(async db =>
        {
            db.MediaFiles.Remove(db.MediaFiles.Single(m => m.Path == audio));
            await db.SaveChangesAsync();
        });
        var validate = await admin.Client.GetFromJsonAsync<JsonNode>("/api/admin/vocab/validate");
        Assert.False((bool)validate!["valid"]!);
        var pub = await admin.Client.PostAsJsonAsync("/api/admin/vocab/publish", new { });
        Assert.Equal(HttpStatusCode.BadRequest, pub.StatusCode);
        Assert.Contains((await pub.Content.ReadFromJsonAsync<JsonNode>())!["errors"]!.AsArray(), e => ((string)e!).Contains(audio));
    }


    private static async Task<string> Upload(Session admin, string id)
    {
        var path = $"audio/words/{id}.mp3";
        (await ApiFactory.UploadAsync(admin, path, ApiFactory.FakeMp3(3))).EnsureSuccessStatusCode();
        return path;
    }
}

static class RecordExt
{
    /// <summary>Sao chép đối tượng ẩn danh và đổi một trường (để test sửa từ gọn hơn).</summary>
    public static JsonObject with2(this object o, string field, string value)
    {
        var node = JsonSerializer_Serialize(o);
        node[field] = value;
        return node;
    }

    private static JsonObject JsonSerializer_Serialize(object o) =>
        JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(o))!.AsObject();
}
