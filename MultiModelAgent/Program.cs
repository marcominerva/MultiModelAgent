using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using MultiModelAgent;
using MultiModelAgent.Jev;
using OpenAI;
using OpenAI.Responses;

var openAIClient = new OpenAIClient(new ApiKeyCredential(Constants.OpenAIApiKey), new()
{
    Endpoint = new(Constants.OpenAIEndpoint),
    Transport = new HttpClientPipelineTransport(new(new TraceHttpClientHandler()))
})
.GetResponsesClient();

//var geminiClient = new Client(apiKey: "42", clientOptions: new()
//{
//    HttpClientFactory = () => new(new TraceHttpClientHandler())
//});

//var anthropicClient = new AnthropicClient(new()
//{
//    ApiKey = "42",
//    HttpClient = new(new TraceHttpClientHandler())
//});

var routingClient = new RoutePersistingRoutingChatClient(new Dictionary<string, IChatClient>
{
    ["gpt-6.1-sol"] = openAIClient.AsIChatClientWithStoredOutputDisabled("gpt-6.1-sol"),
    ["gpt-6-luna"] = openAIClient.AsIChatClientWithStoredOutputDisabled("gpt-6-luna"),
    ["gpt-5.4-mini"] = openAIClient.AsIChatClientWithStoredOutputDisabled("gpt-5.4-mini")
},
new() { DefaultRoute = "gpt-6-luna" });

var chatClientAgent = routingClient
    .AsAIAgent(new ChatClientAgentOptions
    {
        ChatOptions = new()
        {
            Instructions = "You are a helpful assistant.",
            Reasoning = new()
            {
                Effort = ReasoningEffort.Medium,
                Output = ReasoningOutput.Summary
            }
        },
        ChatHistoryProvider = new InMemoryChatHistoryProvider(new()
        {
            ReducerTriggerEvent = InMemoryChatHistoryProviderOptions.ChatReducerTriggerEvent.AfterMessageAdded,
            ChatReducer = new MessageCountingChatReducer(20),
            StorageInputRequestMessageFilter = messages =>
            {
                return messages.Where(m => m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.ChatHistory
                    && m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.AIContextProvider);
            }
        })
    });

var jevHttpClient = new HttpClient()
{
    BaseAddress = new(Constants.JevEndpoint)
};
jevHttpClient.DefaultRequestHeaders.Authorization = new("Bearer", Constants.JevApiKey);

var agent = new AutoRoutingAgent(chatClientAgent, routingClient, jevHttpClient);

var session = await agent.CreateSessionAsync();

while (true)
{
    Console.Write("Question: ");
    var question = Console.ReadLine()!;

    //var response = await agent.RunAsync(question, session);
    //Console.WriteLine(response);

    var response = agent.RunStreamingAsync(question, session);

    await foreach (var message in response)
    {
        Console.Write(message);
    }

    Console.WriteLine();
    Console.WriteLine();
}

public class AutoRoutingAgent(AIAgent innerAgent, RoutePersistingRoutingChatClient routingClient, HttpClient jevHttpClient) : DelegatingAIAgent(innerAgent)
{
    private static readonly JsonSerializerOptions jevJsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    protected override async Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        await SetModelAsync(messages, session, cancellationToken);

        var response = await base.RunCoreAsync(messages, session, options, cancellationToken);
        return response;
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await SetModelAsync(messages, session, cancellationToken);

        await foreach (var update in base.RunCoreStreamingAsync(messages, session, options, cancellationToken))
        {
            yield return update;
        }
    }

    private async Task SetModelAsync(IEnumerable<ChatMessage> messages, AgentSession? session, CancellationToken cancellationToken)
    {
        if (session is null)
        {
            return;
        }

        var state = GetState();

        var request = new JevRequest
        {
            State = state,
            Questions = new Dictionary<string, JevQuestion>
            {
                ["model"] = new JevQuestion
                {
                    Type = JevQuestionType.Choice,
                    Instructions = """
                        First reconstruct the effective task represented by currentRequest. Use conversationContext only
                        when currentRequest refers to, continues, or modifies an earlier task; ignore unrelated previous
                        topics. Then select the model based on the complexity of that effective task, not on the wording
                        or length of currentRequest alone. A follow-up that only changes a value, constraint, assumption,
                        or requested detail retains the reasoning requirements of the task it modifies. For example,
                        after a complex causal analysis, "what if growth were 10%?" remains a complex causal analysis.
                        Conversely, classify a self-contained request solely by its own complexity.
                        """,
                    Criteria = JevQuestion.CreateChoiceCriteria(new Dictionary<string, object?>
                    {
                        ["gpt-6.1-sol"] = "Complex multi-step reasoning, difficult analysis, advanced coding, or tasks where correctness requires deep deliberation, including follow-ups that modify such tasks.",
                        ["gpt-6-luna"] = "General-purpose generation, planning, synthesis, or nuanced tasks that are neither simple nor deeply complex.",
                        ["gpt-5.4-mini"] = "Simple, self-contained factual questions, short transformations, or straightforward tasks requiring little reasoning."
                    })
                }
            }
        };

        using var response = await jevHttpClient.PostAsJsonAsync("systemone", request, jevJsonSerializerOptions, cancellationToken);
        var jevResponse = await response.Content.ReadFromJsonAsync<JevResponse>(jevJsonSerializerOptions, cancellationToken);

        if (jevResponse?.Answers.TryGetValue("model", out var modelAnswer) == true && !string.IsNullOrWhiteSpace(modelAnswer.Choice))
        {
            routingClient.SetActiveRoute(session, modelAnswer.Choice);
        }

        object GetState()
        {
            string? conversationContext = null;
            if (session.TryGetInMemoryChatHistory(out var historyMessages))
            {
                conversationContext = string.Join("\n", historyMessages.Where(m => !string.IsNullOrWhiteSpace(m.Text))
                    .TakeLast(4).Select(m => $"{m.Role}: {m.Text}"));
            }

            var currentRequest = string.Join("\n", messages.Where(m => !string.IsNullOrWhiteSpace(m.Text))
                .Select(m => $"{m.Role}: {m.Text}"));

            return new
            {
                ConversationContext = conversationContext,
                CurrentRequest = currentRequest
            };
        }
    }
}

public class TraceHttpClientHandler : HttpClientHandler
{
    private static readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestString = request.Content is null ? "(no request body)" : await request.Content.ReadAsStringAsync(cancellationToken);

        PrintText($"Raw Request ({request.RequestUri})", ConsoleColor.Green);
        PrintText(FormatJson(requestString), ConsoleColor.DarkGray);
        PrintSeparator();

        var response = await base.SendAsync(request, cancellationToken);

        //var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
        //PrintText("Raw Response", ConsoleColor.Green);
        //PrintText(FormatJson(responseString), ConsoleColor.DarkGray);
        //PrintSeparator();

        return response;

        static void PrintText(string message, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        static void PrintSeparator() => Console.WriteLine(new string('-', 50));
    }

    private static string FormatJson(string input)
    {
        try
        {
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(input);
            return JsonSerializer.Serialize(jsonElement, jsonSerializerOptions);
        }
        catch
        {
            return input;
        }
    }
}