using System.Text.Json.Serialization;

namespace MultiModelAgent.Jev;

public class JevResponse
{
    public string? Model { get; set; }

    public Dictionary<string, JevAnswer> Answers { get; set; } = [];

    public JevUsage? Usage { get; set; }
}

public class JevAnswer
{
    public JevQuestionType Type { get; set; }

    public double? Noul { get; set; }

    public string? Choice { get; set; }

    public double? Score { get; set; }

    // Score
    public Dictionary<string, string>? Legend { get; set; }

    // Choice + Score
    public Dictionary<string, double>? Probabilities { get; set; }

    public double? Confidence { get; set; }
}

public class JevUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}
