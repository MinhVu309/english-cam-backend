using EnglishCam.Api.Models;
using EnglishCam.Api.Services;
using Xunit;

namespace EnglishCam.Api.Tests;

/// <summary>Logic chọn từ kiểu Leitner, thuần (không DB, Random cố định seed).</summary>
public class WordSelectorTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private static List<SelectableWord> Pool(int n = 10) =>
        Enumerable.Range(0, n).Select(i => new SelectableWord($"w{i}", true, i % 2 == 0)).ToList();

    private static ChildWordProgress P(string id, int box, int seen = 3, DateTime? due = null) =>
        new() { WordId = id, Box = box, SeenCount = seen, NextDueAt = due };

    [Fact]
    public void Weight_prefers_new_and_weak_words_over_mastered_ones()
    {
        Assert.Equal(4, WordSelector.Weight(null, Now));
        Assert.Equal(4, WordSelector.Weight(P("a", 5, seen: 0), Now));
        Assert.Equal(5, WordSelector.Weight(P("a", 1), Now));
        Assert.Equal(1, WordSelector.Weight(P("a", 5), Now));
        Assert.True(WordSelector.Weight(P("a", 1), Now) > WordSelector.Weight(P("a", 3), Now));
        Assert.True(WordSelector.Weight(P("a", 3), Now) > WordSelector.Weight(P("a", 5), Now));
    }

    [Fact]
    public void A_word_not_yet_due_is_less_likely_than_the_same_word_when_due()
    {
        var due = WordSelector.Weight(P("a", 3, due: Now.AddDays(-1)), Now);
        var notDue = WordSelector.Weight(P("a", 3, due: Now.AddDays(2)), Now);
        Assert.Equal(due * 0.4, notDue, 6);
    }

    [Fact]
    public void Next_due_grows_with_the_box()
    {
        Assert.Equal(Now, WordSelector.NextDue(1, Now));
        Assert.Equal(Now.AddDays(1), WordSelector.NextDue(2, Now));
        Assert.Equal(Now.AddDays(10), WordSelector.NextDue(5, Now));
        Assert.Equal(Now.AddDays(10), WordSelector.NextDue(99, Now)); // ngoài khoảng thì kẹp về hộp 5
    }

    [Fact]
    public void Weak_word_is_picked_far_more_often_than_a_mastered_one()
    {
        var pool = Pool();
        var progress = new Dictionary<string, ChildWordProgress>
        {
            ["w0"] = P("w0", 1),                                 // hay sai
            ["w1"] = P("w1", 5, due: Now.AddDays(5)),            // đã thuộc, chưa tới hạn
        };
        var rng = new Random(42);
        int weak = 0, mastered = 0;
        for (var i = 0; i < 4000; i++)
        {
            var pick = WordSelector.PickTargets(pool, progress, 1, Now, rng).Single().Id;
            if (pick == "w0") weak++;
            if (pick == "w1") mastered++;
        }
        Assert.True(weak > mastered * 8, $"weak={weak} mastered={mastered}"); // 5 so với 0,4 → khoảng 12 lần
        Assert.True(mastered > 0, "từ đã thuộc vẫn có cơ hội xuất hiện");
    }

    [Fact]
    public void Targets_are_distinct_and_never_exceed_the_pool()
    {
        var picks = WordSelector.PickTargets(Pool(5), new Dictionary<string, ChildWordProgress>(), 20, Now, new Random(1));
        Assert.Equal(5, picks.Count);
        Assert.Equal(5, picks.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void Questions_have_distinct_options_that_contain_the_target_exactly_once()
    {
        var questions = WordSelector.BuildQuestions(Pool(12), new Dictionary<string, ChildWordProgress>(), 8, 4, Now, new Random(7));
        Assert.Equal(8, questions.Count);
        Assert.Equal(8, questions.Select(q => q.WordId).Distinct().Count());
        foreach (var q in questions)
        {
            Assert.Equal(4, q.OptionIds.Count);
            Assert.Equal(4, q.OptionIds.Distinct().Count());
            Assert.Single(q.OptionIds, id => id == q.WordId);
        }
    }

    [Fact]
    public void More_questions_than_words_repeats_targets_instead_of_failing()
    {
        var questions = WordSelector.BuildQuestions(Pool(5), new Dictionary<string, ChildWordProgress>(), 12, 4, Now, new Random(3));
        Assert.Equal(12, questions.Count);
        Assert.All(questions, q => Assert.Equal(4, q.OptionIds.Distinct().Count()));
    }

    [Fact]
    public void Positions_of_the_right_answer_are_shuffled()
    {
        var questions = WordSelector.BuildQuestions(Pool(12), new Dictionary<string, ChildWordProgress>(), 20, 4, Now, new Random(11));
        var positions = questions.Select(q => q.OptionIds.IndexOf(q.WordId)).Distinct().Count();
        Assert.True(positions >= 3, "đáp án đúng không được luôn nằm ở một vị trí");
    }
}
