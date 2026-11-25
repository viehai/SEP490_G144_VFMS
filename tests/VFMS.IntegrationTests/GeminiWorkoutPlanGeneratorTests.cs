using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VFMS.Application.Interfaces;
using VFMS.Infrastructure.External;

namespace VFMS.IntegrationTests;

/// <summary>The Gemini client against a fake HTTP server: busy replies are retried, other errors fall back at once.</summary>
public sealed class GeminiWorkoutPlanGeneratorTests
{
    private const string Plan = """{"planName":"Giảm mỡ","nutritionTips":"Ăn đủ đạm.","days":[{"day":1,"exercises":[{"exerciseId":1,"sets":3,"reps":12,"durationMinutes":null,"note":"chậm"}]}]}""";

    private static string Reply(string text, bool withThought = false)
    {
        var thought = withThought ? """{"text":"thinking...","thought":true},""" : "";
        return "{\"candidates\":[{\"content\":{\"parts\":[" + thought + "{\"text\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}]}}]}";
    }

    private static (GeminiWorkoutPlanGenerator Generator, List<HttpStatusCode> Calls) Generator(params (HttpStatusCode Status, string Body)[] replies)
    {
        var calls = new List<HttpStatusCode>();
        var queue = new Queue<(HttpStatusCode, string)>(replies);
        var http = new HttpClient(new Fake(() =>
        {
            var (status, body) = queue.Dequeue();
            calls.Add(status);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }));
        var settings = new GeminiSettings { ApiKey = "test-key", Model = "m", TimeoutSeconds = 5 };
        return (new GeminiWorkoutPlanGenerator(http, settings, NullLogger<GeminiWorkoutPlanGenerator>.Instance) { RetryDelay = TimeSpan.Zero }, calls);
    }

    private static readonly AnonymisedSurvey Survey = new(30, "Male", 80, 175, "WeightLoss", "Beginner", 3, null);
    private static readonly CatalogExercise[] Catalog = [new(1, "Squat", "Legs", "Beginner")];

    [Fact]
    public async Task BusyGeminiIsRetriedThenThePlanIsUsed()
    {
        var (generator, calls) = Generator(
            (HttpStatusCode.ServiceUnavailable, """{"error":{"code":503,"message":"The model is overloaded."}}"""),
            (HttpStatusCode.OK, Reply(Plan, withThought: true)));

        var plan = await generator.GenerateAsync(Survey, Catalog);

        Assert.Equal([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK], calls);
        Assert.NotNull(plan);
        Assert.Equal("Giảm mỡ", plan.Name);
        Assert.Single(plan.Items);
    }

    [Fact]
    public async Task GivesUpAfterThreeBusyRepliesAndDoesNotRetryOtherErrors()
    {
        var busy = (HttpStatusCode.ServiceUnavailable, "{}");
        var (stillBusy, busyCalls) = Generator(busy, busy, busy);
        Assert.Null(await stillBusy.GenerateAsync(Survey, Catalog));
        Assert.Equal(GeminiWorkoutPlanGenerator.MaxAttempts, busyCalls.Count);

        var (badKey, badCalls) = Generator((HttpStatusCode.Forbidden, """{"error":{"message":"API key not valid"}}"""));
        Assert.Null(await badKey.GenerateAsync(Survey, Catalog));
        Assert.Single(badCalls);

        var (quota, quotaCalls) = Generator((HttpStatusCode.TooManyRequests, """{"error":{"code":429,"message":"Quota exceeded"}}"""));
        Assert.Null(await quota.GenerateAsync(Survey, Catalog));
        Assert.Single(quotaCalls);
    }

    private sealed class Fake(Func<HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply());
    }
}
