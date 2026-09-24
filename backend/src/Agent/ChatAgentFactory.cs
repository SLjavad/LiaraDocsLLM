using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace LiaraDocsAssistant.Agent;

/// <summary>
/// Builds the Microsoft Agent Framework ChatClientAgent for /api/chat.
/// Reuses the existing "chat" HttpClient (same NFR4 resilience policy already
/// applied to it in Program.cs, same one RouterService posts to) as the OpenAI
/// SDK's transport, pointed at CHAT_MODEL_BASE_URL — the OpenAI SDK is used
/// here purely as a well-tested client for the OpenAI-wire-compatible protocol
/// (tool-calling/streaming translation), not as a dependency on OpenAI's own
/// service: CHAT_MODEL_BASE_URL/CHAT_MODEL_API_KEY/CHAT_MODEL_NAME stay
/// config-only per NFR2, any OpenAI-compatible provider still works unchanged.
///
/// A fresh ChatClientAgent is minted per chat turn (cheap — it wraps the one
/// already-built ChatClient) because each turn's tools are scoped to that
/// request's session (see ChatTools). No AgentSession/MAF-side history is
/// used: this system owns conversation history itself in the `messages`
/// table, and feeds the reconstructed message list in on every call.
/// </summary>
public sealed class ChatAgentFactory
{
    private readonly ChatClient chatClient;

    public ChatAgentFactory(HttpClient chatHttpClient, string baseUrl, string apiKey, string modelName)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(baseUrl),
            Transport = new HttpClientPipelineTransport(chatHttpClient),
        };
        var client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        chatClient = client.GetChatClient(modelName);
    }

    public AIAgent CreateAgent(string instructions, IList<AITool> tools) =>
        chatClient.AsAIAgent(instructions: instructions, name: "liara-docs-assistant", tools: tools);
}
