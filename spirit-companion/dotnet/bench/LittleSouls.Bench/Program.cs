using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Creatura.Seats;
using Daemon.Divination;
using LittleSouls.Bench;
using LittleSouls.Companion;
using LittleSouls.Daemon;

// B5: every scenario × every test spirit, through the real daemon turn (crisis filter, Creatura loop, grounding critic)
// with DeepSeek in the seat, then judged by a second DeepSeek model. Thresholds are fixed in Hypotheses before a run.
//
//   dotnet run --project bench/LittleSouls.Bench -- [--only id,id] [--spirits es_seer] [--parallel 4] [--out ../docs/bench]
//
// The key: DEEPSEEK_API_KEY, or none when the network proxy adds it. Seat and judge models: BENCH_SEAT (deepseek-flash),
// BENCH_JUDGE (deepseek-v4-pro), BENCH_EFFORT (low).
var opts = Args.Parse(args);
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
var scenarios = Scenarios.Load(Path.Combine(root, "scenarios.json"), opts.Only);
var spirits = Spirits.Load(Path.Combine(root, "test_spirits.json"), Path.Combine(root, "archetypes.json"), opts.Spirits);
string seatModel = Env("BENCH_SEAT", "deepseek-flash"), judgeModel = Env("BENCH_JUDGE", "deepseek-v4-pro"), effort = Env("BENCH_EFFORT", "low");
string key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? "proxy-injected";

var http = new HttpClient(new ReasoningEffort(effort) { InnerHandler = new HttpClientHandler() }) { Timeout = TimeSpan.FromMinutes(4) };
var endpoint = new ModelEndpoint(seatModel, Provider.OpenAiCompatible, "https://api.deepseek.com", seatModel, null, NativeTools: true, Reasoning: true, null, null);
var client = new ModelClient(http, keys: _ => key);
var judge = new Judge(http, key, judgeModel);
var oracle = new Oracle(Encoding.UTF8.GetBytes("little-souls-bench-secret"));

var cases = (from s in scenarios from sp in spirits where s.Turns.ContainsKey(sp.Language) select (s, sp)).ToList();
Console.WriteLine($"{cases.Count} conversations, seat {seatModel} (effort {effort}), judge {judgeModel}");
var results = new List<CaseResult>();
var gate = new SemaphoreSlim(opts.Parallel);
var sw = Stopwatch.StartNew();
await Task.WhenAll(cases.Select(async c =>
{
    await gate.WaitAsync();
    try
    {
        var r = await Runner.RunAsync(c.s, c.sp, oracle, endpoint, client, judge);
        lock (results) results.Add(r);
        Console.WriteLine($"  {c.s.Id,-28} {c.sp.Key,-10} {(r.Error is null ? r.Summary : "ERROR " + r.Error)}");
    }
    finally { gate.Release(); }
}));

results = results.OrderBy(r => r.Scenario).ThenBy(r => r.Spirit).ToList();
Directory.CreateDirectory(opts.Out);
string stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture);
string md = Report.Markdown(results, seatModel, judgeModel, effort, sw.Elapsed);
File.WriteAllText(Path.Combine(opts.Out, $"b5-{stamp}.md"), md);
File.WriteAllLines(Path.Combine(opts.Out, $"b5-{stamp}.jsonl"), results.Select(r => JsonSerializer.Serialize(r, Report.Json)));
Console.WriteLine();
Console.WriteLine(md.Split("\n## Conversations")[0]);
Console.WriteLine($"written to {opts.Out}/b5-{stamp}.md");

static string Env(string k, string d) => Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : d;
