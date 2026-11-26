using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VFMS.Application.Interfaces;
using VFMS.Infrastructure.External;

namespace VFMS.IntegrationTests;

/// <summary>The Gemini client against a fake HTTP server: busy replies are retried, then the backup model; other errors fall back at once.</summary>
public sealed class GeminiWorkoutPlanGeneratorTests
{
    private const string Plan = """{"planName":"Giảm mỡ","nutritionTips":"Ăn đủ đạm.","days":[{"day":1,"exercises":[{"exerciseId":1,"sets":3,"reps":12,"durationMinutes":null,"note":"chậm"}]}]}""";

    private static string Reply(string text, bool withThought = false)
    {
        var thought = withThought ? """{"text":"thinking...","thought":true},""" : "";
        return "{\"candidates\":[{\"content\":{\"parts\":[" + thought + "{\"text\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}]}}]}";
    }

    // Main model "main", backup "backup". A null body makes the fake throw a timeout for that call.
    private static (GeminiWorkoutPlanGenerator Generator, List<string> Calls) Generator(params (HttpStatusCode Status, string? Body)[] replies)
    {
        var calls = new List<string>();
        var queue = new Queue<(HttpStatusCode, string?)>(replies);
        var http = new HttpClient(new Fake(request =>
        {
            var (status, body) = queue.Dequeue();
            var model = request.RequestUri!.AbsolutePath.Contains("backup") ? "backup" : "main";
            calls.Add($"{model} {(body is null ? "timeout" : ((int)status).ToString())}");
            if (body is null)
                throw new TaskCanceledException("slow");
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }));
        var settings = new GeminiSettings { ApiKey = "test-key", Model = "main", FallbackModel = "backup", TimeoutSeconds = 5 };
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

        var plan = await generator.GenerateAsync(Survey, Catalog, []);

        Assert.Equal(["main 503", "main 200"], calls);
        Assert.NotNull(plan);
        Assert.Equal("Giảm mỡ", plan.Name);
        Assert.Single(plan.Items);
        Assert.Equal("main", generator.ModelName);
    }

    [Fact]
    public async Task BackupModelAnswersWhenTheMainOneStaysBusyOrIsSlow()
    {
        var busy = (HttpStatusCode.ServiceUnavailable, (string?)"{}");
        var (stillBusy, busyCalls) = Generator(busy, busy, busy, (HttpStatusCode.OK, Reply(Plan)));
        Assert.NotNull(await stillBusy.GenerateAsync(Survey, Catalog, []));
        Assert.Equal(["main 503", "main 503", "main 503", "backup 200"], busyCalls);
        Assert.Equal("backup", stillBusy.ModelName);

        var (slow, slowCalls) = Generator((HttpStatusCode.OK, null), (HttpStatusCode.OK, Reply(Plan)));
        Assert.NotNull(await slow.GenerateAsync(Survey, Catalog, []));
        Assert.Equal(["main timeout", "backup 200"], slowCalls);
    }

    [Fact]
    public async Task GivesUpWhenBothModelsAreBusyAndDoesNotTryTheBackupForOtherErrors()
    {
        var busy = (HttpStatusCode.ServiceUnavailable, (string?)"{}");
        var (allBusy, busyCalls) = Generator(busy, busy, busy, busy, busy, busy);
        Assert.Null(await allBusy.GenerateAsync(Survey, Catalog, []));
        Assert.Equal(2 * GeminiWorkoutPlanGenerator.MaxAttempts, busyCalls.Count);

        var (badKey, badCalls) = Generator((HttpStatusCode.Forbidden, """{"error":{"message":"API key not valid"}}"""));
        Assert.Null(await badKey.GenerateAsync(Survey, Catalog, []));
        Assert.Equal(["main 403"], badCalls);

        var (quota, quotaCalls) = Generator((HttpStatusCode.TooManyRequests, """{"error":{"code":429,"message":"Quota exceeded"}}"""));
        Assert.Null(await quota.GenerateAsync(Survey, Catalog, []));
        Assert.Equal(["main 429"], quotaCalls);
    }

    [Fact]
    public void PromptListsMachinesAndReplyKeepsTheChosenMachine()
    {
        var prompt = GeminiWorkoutPlanGenerator.BuildPrompt(Survey, Catalog, [new CatalogEquipment(7, "Máy đạp đùi")]);
        Assert.Contains("- id 7: Máy đạp đùi", prompt);
        Assert.Contains("\"equipmentId\"", prompt);

        var plan = GeminiWorkoutPlanGenerator.Parse(
            """{"planName":"p","days":[{"day":1,"exercises":[{"exerciseId":1,"equipmentId":7,"sets":3,"reps":12},{"exerciseId":1,"equipmentId":null,"sets":3,"reps":12}]}]}""");
        Assert.Equal([7, null], plan!.Items.Select(i => i.EquipmentId));
    }

    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }
}
