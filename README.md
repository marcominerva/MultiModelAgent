# Multi-Model Agent

Multi-Model Agent is a .NET 10 console application that demonstrates context-aware model routing for an AI agent. Before each request is sent to a chat model, the application asks Jev to classify the effective task by complexity. The selected route is persisted for the current agent session and used to stream the response.

## How it works

1. The console application collects a user prompt.
2. `AutoRoutingAgent` combines the current request with up to four recent conversation messages.
3. Jev evaluates that state against the routing criteria and selects a model.
4. `RoutePersistingRoutingChatClient` activates the selected route for the current session.
5. Microsoft Agent Framework runs the request and streams the response to the console.

The current routing policy uses three tiers:

| Route | Intended workload |
| --- | --- |
| `gpt-6.1-sol` | Complex multi-step reasoning, difficult analysis, and advanced coding |
| `gpt-6-luna` | General-purpose generation, planning, synthesis, and nuanced tasks |
| `gpt-5.4-mini` | Simple factual questions, short transformations, and straightforward tasks |

`gpt-6-luna` is the default route. Follow-up prompts are classified using relevant conversation context, so a short continuation of a complex task can remain on the more capable route.

## Project structure

```text
MultiModelAgent/
|-- Program.cs                 Application setup, console loop, routing agent, and HTTP tracing
|-- Constants.cs               Service endpoints and local credentials
`-- Jev/
    |-- JevRequest.cs          Jev request and question contracts
    |-- JevResponse.cs         Jev response, answer, and usage contracts
    |-- JevQuestionType.cs     Supported Jev question types
```

The project currently uses an OpenAI-compatible endpoint through `Microsoft.Agents.AI.OpenAI`.

## Requirements

- .NET 10 SDK
- Access to an OpenAI-compatible endpoint with the configured model deployments
- A Jev API endpoint and API key

## Configuration

For local experimentation, configure the following values used by `Constants.cs`:

- OpenAI-compatible endpoint and API key
- Jev endpoint and API key

> [!WARNING]
> Do not commit API keys or other credentials. Keep secrets in user secrets, environment variables, or a secure secret store before publishing or deploying the application.

The model names and their routing criteria are defined in `Program.cs`. If your endpoint exposes different deployments, update both the routing client dictionary and the corresponding Jev choice criteria so every possible Jev answer maps to a registered route.

## Use another model provider

The routing layer depends on the `IChatClient` abstraction, so routes do not need to use the same provider. Google Gemini, Anthropic, OpenAI-compatible models, or any other provider with a `Microsoft.Extensions.AI` adapter can participate in the same routing table.

To add a provider:

1. Add the provider SDK and its Microsoft Agent Framework or `Microsoft.Extensions.AI` integration package. This project already references `Google.GenAI` and `Microsoft.Agents.AI.Anthropic`.
2. Create the provider client with its endpoint and credentials. `Program.cs` contains commented initialization examples for Google and Anthropic that can be used as a starting point.
3. Adapt each provider model to `IChatClient` and register it in the dictionary passed to `RoutePersistingRoutingChatClient`. Use a unique route key; it can differ from the provider's model or deployment identifier.
4. Add the same route key to the choices passed to `JevQuestion.CreateChoiceCriteria`, together with a clear description of the workloads for which Jev should select it.
5. Optionally set the new route as `DefaultRoute`.
6. Verify that the provider supports the chat options used by the agent, particularly streaming, reasoning options, and conversation history. Remove or customize unsupported options per provider rather than assuming identical capabilities.

For example, a mixed routing table can map `fast` to a Gemini `IChatClient`, `general` to an Anthropic `IChatClient`, and `complex` to an OpenAI-compatible `IChatClient`. Jev must return `fast`, `general`, or `complex` respectively. Every possible Jev choice must have a matching dictionary entry; otherwise the routing client cannot activate that route.

The following example configures the routing client after each provider-specific client has been adapted to `IChatClient`:

```csharp
IChatClient geminiChatClient = CreateGeminiChatClient();
IChatClient anthropicChatClient = CreateAnthropicChatClient();
IChatClient openAIChatClient = CreateOpenAIChatClient();

var routingClient = new RoutePersistingRoutingChatClient(
    new Dictionary<string, IChatClient>
    {
        ["fast"] = geminiChatClient,
        ["general"] = anthropicChatClient,
        ["complex"] = openAIChatClient
    },
    new()
    {
        DefaultRoute = "general"
    });
```

`CreateGeminiChatClient`, `CreateAnthropicChatClient`, and `CreateOpenAIChatClient` represent provider-specific initialization and adapter code. Replace them with the APIs exposed by the packages and versions used by your application. The route names in this dictionary must exactly match the choices sent to Jev.

Provider credentials should be configured independently and loaded from environment variables, user secrets, or a secure secret store. Do not add them to `Constants.cs` in a repository that will be published.

## Run the application

From the repository root:

```bash
dotnet run --project MultiModelAgent/MultiModelAgent.csproj
```

Enter a question at the prompt. The application prints the outbound model request for diagnostics and streams the selected model's response. Press `Ctrl+C` to stop the console loop.

## Key implementation details

- Built on Microsoft Agent Framework and `Microsoft.Extensions.AI` abstractions.
- Supports both streaming and non-streaming agent execution; the console uses streaming by default.
- Persists the selected model route per `AgentSession`.
- Keeps in-memory chat history and reduces it after 20 messages.
- Sends only the current request and a small recent context window to Jev for routing.
- Uses `jev-latest` as the default Jev model and the `systemone` API operation.

## License

This project is licensed under the [MIT License](LICENSE.txt).
