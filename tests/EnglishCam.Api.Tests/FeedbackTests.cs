using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using EnglishCam.Api.Dtos;
using Xunit;

namespace EnglishCam.Api.Tests;

public class FeedbackTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private static object Fb(Guid? id = null, string predicted = "hat", double p = 0.81, string? wordId = "cup", string text = "cup",
        string source = "top3", object[]? top3 = null) => new
    {
        id = id ?? Guid.NewGuid(), predictedLabel = predicted, predictedProbability = p,
        top3 = top3 ?? [new { label = "hat", probability = 0.81 }, new { label = "umbrella", probability = 0.09 }, new { label = "towel", probability = 0.06 }],
        correctWordId = wordId, correctText = text, source, modelSource = "bundled", vocabVersion = 3,
    };

    private static Task<HttpResponseMessage> Send(Session s, object body) => s.Client.PostAsJsonAsync("/api/feedback/recognition", body);

    private async Task<PagedResult<AdminFeedbackDto>> ListAsync(string query = "")
    {
        var admin = await f.LoginAdminAsync();
        return (await admin.Client.GetFromJsonAsync<PagedResult<AdminFeedbackDto>>("/api/admin/feedback" + query))!;
    }

    [Fact]
    public async Task Feedback_needs_a_signed_in_user_and_both_child_and_parent_can_send()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous().PostAsJsonAsync("/api/feedback/recognition", Fb())).StatusCode);
        var kid = await f.NewKidAsync();
        var r1 = await Send(kid.Device, Fb());
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.False((await r1.Content.ReadFromJsonAsync<FeedbackResultDto>())!.Updated);
        Assert.Equal(HttpStatusCode.OK, (await Send(kid.Parent, Fb())).StatusCode);
    }

    [Fact]
    public async Task Everything_the_app_sends_is_stored_and_unknown_word_ids_are_dropped()
    {
        var kid = await f.NewKidAsync();
        var known = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await Send(kid.Device, Fb(known, wordId: "cup", text: "cup"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(kid.Device, Fb(unknown, wordId: "not_a_word", text: "cái bàn", source: "typed"))).StatusCode);

        var list = await ListAsync("?pageSize=100");
        var a = list.Items.Single(x => x.Id == known);
        Assert.Equal(("hat", 0.81, "cup", "cup", "top3", "bundled", 3), (a.PredictedLabel, a.PredictedProbability, a.CorrectWordId, a.CorrectText, a.Source, a.ModelSource, a.VocabVersion));
        Assert.Equal(["hat", "umbrella", "towel"], a.Top3.Select(c => c.Label));
        Assert.Equal(0.09, a.Top3[1].Probability);
        var b = list.Items.Single(x => x.Id == unknown);
        Assert.Null(b.CorrectWordId);
        Assert.Equal(("cái bàn", "typed"), (b.CorrectText, b.Source));
    }

    [Fact]
    public async Task Sending_again_with_the_same_id_updates_the_same_row_and_does_not_double_count()
    {
        var kid = await f.NewKidAsync();
        var id = Guid.NewGuid();
        await Send(kid.Device, Fb(id, wordId: "cup", text: "cup"));
        var before = (await ListAsync()).Total;

        var r = await Send(kid.Device, Fb(id, wordId: "umbrella", text: "umbrella"));
        Assert.True((await r.Content.ReadFromJsonAsync<FeedbackResultDto>())!.Updated);
        var after = await ListAsync("?pageSize=100");
        Assert.Equal(before, after.Total);
        Assert.Equal("umbrella", after.Items.Single(x => x.Id == id).CorrectWordId);
    }

    [Fact]
    public async Task Another_user_cannot_overwrite_someone_elses_feedback()
    {
        var a = await f.NewKidAsync();
        var b = await f.NewKidAsync();
        var id = Guid.NewGuid();
        await Send(a.Device, Fb(id, wordId: "cup", text: "cup"));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(b.Device, Fb(id, wordId: "book", text: "book"))).StatusCode);
        Assert.Equal("cup", (await ListAsync("?pageSize=100")).Items.Single(x => x.Id == id).CorrectText);
    }

    [Theory]
    [InlineData("nope", "cup", 0.5)]                   // source lạ
    [InlineData("typed", "   ", 0.5)]                  // chỉ khoảng trắng
    [InlineData("typed", "", 0.5)]
    [InlineData("typed", "\t\n \u0007", 0.5)]          // toàn ký tự điều khiển
    [InlineData("typed", "x", 1.5)]                    // xác suất ngoài [0,1]
    public async Task Bad_feedback_is_rejected(string source, string text, double p)
    {
        var kid = await f.NewKidAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(kid.Device, Fb(text: text, source: source, p: p))).StatusCode);
    }

    [Fact]
    public async Task Text_longer_than_60_characters_or_too_many_candidates_is_rejected()
    {
        var kid = await f.NewKidAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(kid.Device, Fb(text: new string('a', 61), source: "typed"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(kid.Device, Fb(text: new string('a', 60), source: "typed"))).StatusCode);
        var six = Enumerable.Range(0, 6).Select(i => (object)new { label = "x" + i, probability = 0.1 }).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(kid.Device, Fb(top3: six))).StatusCode);
    }

    [Fact]
    public async Task Text_is_cleaned_control_characters_and_extra_spaces_removed()
    {
        var kid = await f.NewKidAsync();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await Send(kid.Device, Fb(id, wordId: null, text: "  quả \t\n  táo\u0000  ", source: "typed"))).StatusCode);
        Assert.Equal("quả táo", (await ListAsync("?pageSize=100")).Items.Single(x => x.Id == id).CorrectText);
    }

    [Fact]
    public async Task Each_account_can_add_at_most_100_new_feedbacks_a_day_but_may_still_edit_old_ones()
    {
        var kid = await f.NewKidAsync();
        var first = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await Send(kid.Device, Fb(first))).StatusCode);
        for (var i = 1; i < 100; i++) Assert.Equal(HttpStatusCode.OK, (await Send(kid.Device, Fb())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(kid.Device, Fb())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(kid.Device, Fb(first, text: "cup đã sửa"))).StatusCode);
        // người khác không bị ảnh hưởng
        Assert.Equal(HttpStatusCode.OK, (await Send((await f.NewKidAsync()).Device, Fb())).StatusCode);
    }

    [Fact]
    public async Task Only_admin_can_read_or_delete_feedback()
    {
        var kid = await f.NewKidAsync();
        var admin = await f.LoginAdminAsync();
        var id = Guid.NewGuid();
        await Send(kid.Device, Fb(id));
        foreach (var url in new[] { "/api/admin/feedback", "/api/admin/feedback/summary", "/api/admin/feedback/export.csv" })
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await kid.Parent.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await kid.Device.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous().GetAsync(url)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await kid.Parent.Client.DeleteAsync($"/api/admin/feedback/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync($"/api/admin/feedback/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.DeleteAsync($"/api/admin/feedback/{id}")).StatusCode);
    }

    [Fact]
    public async Task Admin_list_can_filter_and_page()
    {
        var kid = await f.NewKidAsync();
        var tag = "loc" + Guid.NewGuid().ToString("N")[..8];
        await Send(kid.Device, Fb(wordId: null, text: tag + " moi", source: "typed"));
        await Send(kid.Device, Fb(wordId: "cup", text: tag + " cup", source: "top3"));
        Assert.Equal(2, (await ListAsync("?search=" + tag)).Total);
        Assert.Equal(1, (await ListAsync($"?search={tag}&source=typed")).Total);
        Assert.Equal(1, (await ListAsync($"?search={tag}&unknownOnly=true")).Total);
        var paged = await ListAsync($"?search={tag}&pageSize=1&page=2");
        Assert.Equal((2, 1), (paged.Total, paged.Items.Count));
    }

    [Fact]
    public async Task Csv_export_has_a_BOM_header_and_blocks_formula_injection()
    {
        var kid = await f.NewKidAsync();
        var admin = await f.LoginAdminAsync();
        await Send(kid.Device, Fb(wordId: null, text: "=1+1", source: "typed"));
        var bytes = await (await admin.Client.GetAsync("/api/admin/feedback/export.csv")).Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        var csv = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        Assert.StartsWith("created_at,predicted_label,predicted_probability,correct_word_id,correct_text,source,model_source,vocab_version", csv);
        Assert.Contains(",'=1+1,typed,", csv);
        Assert.DoesNotContain(",=1+1,", csv);
    }

    [Fact]
    public async Task Csv_columns_stay_aligned_whatever_the_server_culture_uses_for_decimals()
    {
        var kid = await f.NewKidAsync();
        var admin = await f.LoginAdminAsync();
        await Send(kid.Device, Fb(p: 0.6867, wordId: "cup", text: "cup"));
        var old = (CultureInfo.DefaultThreadCurrentCulture, CultureInfo.CurrentCulture);
        // Máy chủ chạy với ngôn ngữ dùng dấu phẩy thập phân (như máy dev này): 0,6867 sẽ phá cột CSV.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = new CultureInfo("vi-VN");
        try
        {
            var csv = await (await admin.Client.GetAsync("/api/admin/feedback/export.csv")).Content.ReadAsStringAsync();
            var lines = csv.TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.All(lines, l => Assert.Equal(8, l.Split(',').Length));
            Assert.Contains(lines, l => l.Contains(",0.6867,"));
        }
        finally
        {
            (CultureInfo.DefaultThreadCurrentCulture, CultureInfo.CurrentCulture) = old;
        }
    }

    [Fact]
    public async Task Deleting_the_parent_account_also_removes_the_feedback_of_their_children()
    {
        var kid = await f.NewKidAsync();
        var admin = await f.LoginAdminAsync();
        var id = Guid.NewGuid();
        await Send(kid.Device, Fb(id));
        await Send(kid.Parent, Fb());
        var before = (await ListAsync()).Total;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.DeleteAsync($"/api/admin/users/{kid.Parent.Auth.UserId}")).StatusCode);
        var after = await ListAsync("?pageSize=100");
        Assert.Equal(before - 2, after.Total);
        Assert.DoesNotContain(after.Items, x => x.Id == id);
    }
}

/// <summary>Tổng hợp cần cơ sở dữ liệu riêng: con số là tuyệt đối nên không được lẫn dữ liệu của test khác.</summary>
public class FeedbackSummaryTests(ApiFactory f) : IClassFixture<ApiFactory>
{
    private static object Fb(string predicted, string? wordId, string text, string source) => new
    {
        id = Guid.NewGuid(), predictedLabel = predicted, predictedProbability = 0.6, correctWordId = wordId, correctText = text, source,
    };

    [Fact]
    public async Task Summary_counts_the_confusions_and_the_unknown_words_people_typed()
    {
        var admin = await f.LoginAdminAsync();
        var empty = (await admin.Client.GetFromJsonAsync<FeedbackSummaryDto>("/api/admin/feedback/summary"))!;
        Assert.Equal((0, 0, 0), (empty.Total, empty.Last7Days, empty.SameAsPrediction));
        Assert.Empty(empty.TopConfusions);
        Assert.Empty(empty.UnknownWords);

        var kid = await f.NewKidAsync();
        var other = await f.NewKidAsync();
        async Task Post(Session s, object body) => (await s.Client.PostAsJsonAsync("/api/feedback/recognition", body)).EnsureSuccessStatusCode();
        for (var i = 0; i < 3; i++) await Post(i == 0 ? other.Device : kid.Device, Fb("hat", "cup", "cup", "top3"));   // hat → cup ×3
        await Post(kid.Device, Fb("hat", "umbrella", "umbrella", "top3"));                                              // hat → umbrella ×1
        await Post(kid.Device, Fb("apple", null, "quả táo", "typed"));                                                  // từ lạ
        await Post(other.Device, Fb("apple", null, "Quả Táo", "typed"));                                                // cùng từ, khác hoa thường
        await Post(kid.Device, Fb("pen", null, "bàn chải", "typed"));
        await Post(kid.Device, Fb("cup", "cup", "cup", "top3"));                                                        // model đoán đúng

        var s = (await admin.Client.GetFromJsonAsync<FeedbackSummaryDto>("/api/admin/feedback/summary"))!;
        Assert.Equal((8, 8, 1), (s.Total, s.Last7Days, s.SameAsPrediction));
        Assert.Equal(new FeedbackPairDto("hat", "cup", 3), s.TopConfusions[0]);
        Assert.Contains(new FeedbackPairDto("hat", "umbrella", 1), s.TopConfusions);
        Assert.DoesNotContain(s.TopConfusions, p => p.PredictedLabel == "cup" && p.Correct == "cup");
        Assert.Equal(new FeedbackTextDto("quả táo", 2), s.UnknownWords[0]);
        Assert.Equal(new FeedbackTextDto("bàn chải", 1), s.UnknownWords[1]);
    }
}
