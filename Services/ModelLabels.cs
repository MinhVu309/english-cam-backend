namespace EnglishCam.Api.Services;

/// <summary>
/// Các nhãn mà model.tflite nhận ra (bản sao của models/labels.txt, hợp đồng với app). Admin chỉ được gán <c>label</c>
/// thuộc danh sách này. Khi nâng model: cập nhật Data/Seed/model_labels.txt cùng lúc (có test so với models/labels.txt).
/// </summary>
public static class ModelLabels
{
    public static readonly IReadOnlySet<string> All = Load();

    private static HashSet<string> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "model_labels.txt");
        return File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal);
    }
}
