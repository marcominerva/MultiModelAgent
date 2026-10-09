namespace MultiModelAgent.Jev;

public class JevRequest
{
    public object? State { get; set; }

    public string Model { get; set; } = "jev-latest";

    public Dictionary<string, JevQuestion> Questions { get; set; } = [];
}

public class JevQuestion
{
    public JevQuestionType Type { get; set; }

    public object? Instructions { get; set; }

    public object? Criteria { get; set; }

    public static Dictionary<string, object?> CreateNoulCriteria(string? trueDefinition = null, string? falseDefinition = null)
    {
        return new Dictionary<string, object?>
        {
            ["true"] = trueDefinition,
            ["false"] = falseDefinition
        };
    }

    public static Dictionary<string, object?> CreateChoiceCriteria(Dictionary<string, object?> choices)
        => choices;

    public static IEnumerable<object> CreateScoreCriteria(IEnumerable<object> scores)
        => scores;
}

