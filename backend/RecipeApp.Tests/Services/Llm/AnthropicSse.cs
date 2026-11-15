using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace RecipeApp.Tests.Services.Llm;

/// <summary>Messages API answers in the shapes the Claude API sends, so the SDK's own parsing runs against them with no network.</summary>
public static class AnthropicSse
{
    /// <summary>A streamed message whose one text block is <paramref name="text"/>, after a thinking block as the default model sends.</summary>
    public static HttpResponseMessage Message(string text, string stopReason = "end_turn")
    {
        var events = new (string Name, JsonNode Data)[]
        {
            ("message_start", new JsonObject
            {
                ["type"] = "message_start",
                ["message"] = new JsonObject
                {
                    ["id"] = "msg_test", ["type"] = "message", ["role"] = "assistant", ["model"] = "claude-opus-5-5",
                    ["content"] = new JsonArray(), ["stop_reason"] = null, ["stop_sequence"] = null,
                    ["usage"] = new JsonObject { ["input_tokens"] = 10, ["output_tokens"] = 1 },
                },
            }),
            ("content_block_start", new JsonObject
            {
                ["type"] = "content_block_start", ["index"] = 0,
                ["content_block"] = new JsonObject { ["type"] = "thinking", ["thinking"] = "", ["signature"] = "" },
            }),
            ("content_block_delta", new JsonObject
            {
                ["type"] = "content_block_delta", ["index"] = 0,
                ["delta"] = new JsonObject { ["type"] = "signature_delta", ["signature"] = "sig" },
            }),
            ("content_block_stop", new JsonObject { ["type"] = "content_block_stop", ["index"] = 0 }),
            ("content_block_start", new JsonObject
            {
                ["type"] = "content_block_start", ["index"] = 1,
                ["content_block"] = new JsonObject { ["type"] = "text", ["text"] = "" },
            }),
            ("content_block_delta", new JsonObject
            {
                ["type"] = "content_block_delta", ["index"] = 1,
                ["delta"] = new JsonObject { ["type"] = "text_delta", ["text"] = text },
            }),
            ("content_block_stop", new JsonObject { ["type"] = "content_block_stop", ["index"] = 1 }),
            ("message_delta", new JsonObject
            {
                ["type"] = "message_delta",
                ["delta"] = new JsonObject { ["stop_reason"] = stopReason, ["stop_sequence"] = null },
                ["usage"] = new JsonObject { ["output_tokens"] = 20 },
            }),
            ("message_stop", new JsonObject { ["type"] = "message_stop" }),
        };

        var body = new StringBuilder();
        foreach (var (name, data) in events)
            body.Append("event: ").Append(name).Append('\n')
                .Append("data: ").Append(data.ToJsonString()).Append("\n\n");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8, "text/event-stream"),
        };
    }

    /// <summary>An error answer as the Claude API shapes it.</summary>
    public static HttpResponseMessage Error(HttpStatusCode status, string type = "api_error") =>
        new(status)
        {
            Content = new StringContent(
                new JsonObject
                {
                    ["type"]  = "error",
                    ["error"] = new JsonObject { ["type"] = type, ["message"] = $"stubbed {(int)status}" },
                }.ToJsonString(),
                Encoding.UTF8, "application/json"),
        };

    /// <summary>A Models API answer for one model.</summary>
    public static HttpResponseMessage Model(string id) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                new JsonObject
                {
                    ["type"] = "model", ["id"] = id, ["display_name"] = "Claude Opus 5.5",
                    ["created_at"] = "2026-09-01T00:00:00Z",
                }.ToJsonString(),
                Encoding.UTF8, "application/json"),
        };
}
