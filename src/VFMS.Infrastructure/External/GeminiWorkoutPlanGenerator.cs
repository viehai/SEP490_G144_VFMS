using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VFMS.Application.Interfaces;

namespace VFMS.Infrastructure.External;

/// <summary>Settings bound from the "Gemini" configuration section. Keep the API key in user-secrets or environment variables.</summary>
public sealed class GeminiSettings
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gemini-3.5-flash-lite";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
    public int TimeoutSeconds { get; set; } = 25;
}

/// <summary>
/// API-02: asks Google Gemini for a weekly plan built only from the gym's exercise catalog.
/// Returns null (so the service uses the GBR-95 fallback) when no key is configured, the call fails or the reply is unusable.
/// </summary>
public sealed class GeminiWorkoutPlanGenerator : IWorkoutPlanGenerator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly GeminiSettings _settings;
    private readonly ILogger<GeminiWorkoutPlanGenerator> _logger;

    public GeminiWorkoutPlanGenerator(HttpClient http, GeminiSettings settings, ILogger<GeminiWorkoutPlanGenerator> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    public string ModelName => _settings.Model;

    public async Task<GeneratedPlan?> GenerateAsync(
        AnonymisedSurvey survey, IReadOnlyList<CatalogExercise> catalog, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogWarning("Gemini:ApiKey is not configured; using the standard fallback plan.");
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(new Uri(_settings.BaseUrl), $"models/{Uri.EscapeDataString(_settings.Model)}:generateContent"));
            request.Headers.Add("x-goog-api-key", _settings.ApiKey);
            request.Content = JsonContent.Create(new
            {
                contents = new[] { new { role = "user", parts = new[] { new { text = BuildPrompt(survey, catalog) } } } },
                generationConfig = new { responseMimeType = "application/json", temperature = 0.4 }
            });

            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini returned HTTP {Status}; using the fallback plan.", (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<GeminiResponse>(Json, timeout.Token);
            var text = body?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            return string.IsNullOrWhiteSpace(text) ? null : Parse(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            _logger.LogWarning(ex, "Gemini call failed; using the fallback plan.");
            return null;
        }
    }

    /// <summary>Parses the model's JSON reply. Public for tests.</summary>
    public static GeneratedPlan? Parse(string text)
    {
        var json = text.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            if (start < 0 || end <= start)
                return null;
            json = json[start..(end + 1)];
        }
        var reply = JsonSerializer.Deserialize<PlanReply>(json, Json);
        if (reply?.Days is null)
            return null;
        var items = reply.Days
            .Where(d => d.Exercises is not null)
            .SelectMany(d => d.Exercises!.Select((e, i) =>
                new GeneratedPlanItem(d.Day, i + 1, e.ExerciseId, e.Sets, e.Reps, e.DurationMinutes, e.Note)))
            .ToArray();
        return new GeneratedPlan(reply.PlanName ?? "Personalised plan", items, reply.NutritionTips);
    }

    public static string BuildPrompt(AnonymisedSurvey survey, IReadOnlyList<CatalogExercise> catalog)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a certified strength and conditioning coach at a gym.");
        sb.AppendLine("Create a safe one-week workout plan for this anonymous gym user.");
        sb.AppendLine($"Training days per week: {survey.WorkoutFrequencyPerWeek}. Use day numbers 1-7 and leave rest days out.");
        sb.AppendLine($"Goal: {survey.FitnessGoal}. Level: {survey.FitnessLevel}.");
        sb.AppendLine($"Age: {(survey.Age?.ToString() ?? "unknown")}. Gender: {survey.Gender ?? "unknown"}. " +
                      $"Weight: {survey.WeightKg} kg. Height: {survey.HeightCm} cm.");
        if (survey.BodyFatPercent is not null || survey.SkeletalMuscleMassKg is not null)
            sb.AppendLine($"InBody: body fat {survey.BodyFatPercent?.ToString() ?? "unknown"} %, " +
                          $"skeletal muscle mass {survey.SkeletalMuscleMassKg?.ToString() ?? "unknown"} kg.");
        if (!string.IsNullOrWhiteSpace(survey.HealthConditions))
            sb.AppendLine($"Health notes (avoid aggravating these): {survey.HealthConditions}");
        sb.AppendLine("Use ONLY exercises from this catalog, referenced by id:");
        foreach (var e in catalog)
            sb.AppendLine($"- id {e.Id}: {e.Name} (muscle: {e.MuscleGroup ?? "-"}, level: {e.DifficultyLevel ?? "-"})");
        sb.AppendLine("Give 4-8 exercises per training day. Strength work needs sets and reps; cardio or stretching needs durationMinutes.");
        sb.AppendLine("Add nutritionTips: 2-4 short sentences of nutrition advice for this goal, written in Vietnamese.");
        sb.AppendLine("Reply with JSON only, in exactly this shape:");
        sb.AppendLine("{\"planName\":\"string\",\"nutritionTips\":\"string\",\"days\":[{\"day\":1,\"exercises\":[{\"exerciseId\":1,\"sets\":3,\"reps\":12,\"durationMinutes\":null,\"note\":\"short tip\"}]}]}");
        return sb.ToString();
    }

    private sealed record GeminiResponse(List<Candidate>? Candidates);
    private sealed record Candidate(GeminiContent? Content);
    private sealed record GeminiContent(List<GeminiPart>? Parts);
    private sealed record GeminiPart(string? Text);

    private sealed record PlanReply(string? PlanName, List<DayReply>? Days, string? NutritionTips = null);
    private sealed record DayReply(int Day, List<ExerciseReply>? Exercises);
    private sealed record ExerciseReply(
        int ExerciseId, int? Sets, int? Reps,
        [property: JsonPropertyName("durationMinutes")] int? DurationMinutes, string? Note);
}
