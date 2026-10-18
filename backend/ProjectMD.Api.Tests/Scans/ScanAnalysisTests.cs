using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectMD.Api.Data;
using ProjectMD.Api.Dtos;
using ProjectMD.Api.Models;
using ProjectMD.Api.Services;
using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Pipeline;
using ProjectMD.Api.Tests.Ai;

namespace ProjectMD.Api.Tests.Scans;

// Tests for the structured analysis (design and API data) stored with each scan.
public class ScanAnalysisTests
{
    private static DesignSystem SampleDesign(params CssVariable[] variables) => new(
        [new ColorToken("#0f172a", 120, "background"), new ColorToken("#38bdf8", 9, "accent")],
        [new FontToken("Inter", [400, 600], ["16px"])],
        ["8px"],
        ["0 1px 2px #0003"],
        ["4px", "8px"],
        variables,
        ["640px"],
        "Tailwind");

    private static ApiEndpoint Endpoint(int i, string? shape = null) => new(
        "GET", "api.example.com", $"/api/items/{i}/{{id}}", true, 1, [200], "application/json", shape, null, ["page"]);

    // An analysis survives the database (jsonb column) unchanged.
    [Fact]
    public void Analysis_round_trips_through_the_database()
    {
        using var db = new TestDatabase();
        var user = db.AddUser("analysis-user");
        var scan = TestDatabase.NewScan(user.Id, DateTime.UtcNow);
        var api = new ApiCatalog([Endpoint(1, "{id:number}")], [new ThirdPartyHost("fonts.gstatic.com", 3, "Fonts")], false);
        scan.Analysis = new ScanAnalysis(1, SampleDesign(new CssVariable("--brand", "#38bdf8")), api);
        using (var write = db.CreateContext())
        {
            write.Scans.Add(scan);
            write.SaveChanges();
        }

        using var read = db.CreateContext();
        var loaded = read.Scans.Single(s => s.Id == scan.Id).Analysis;

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.Version);
        Assert.Equal("#38bdf8", loaded.Design!.Colors[1].Hex);
        Assert.Equal("--brand", loaded.Design.CssVariables[0].Name);
        Assert.Equal("{id:number}", loaded.Api!.Endpoints[0].ResponseShape);
        Assert.Equal("fonts.gstatic.com", loaded.Api.ThirdParty[0].Host);
    }

    // Scans saved before the column existed have a null analysis, and the DTO carries null.
    [Fact]
    public void Old_rows_read_back_with_a_null_analysis()
    {
        using var db = new TestDatabase();
        var user = db.AddUser("old-row-user");
        var scan = db.AddScan(user.Id, DateTime.UtcNow);

        using var read = db.CreateContext();
        var loaded = read.Scans.Single(s => s.Id == scan.Id);

        Assert.Null(loaded.Analysis);
        Assert.Null(loaded.ToDto().Analysis);
    }

    // A text column that holds damaged JSON gives null instead of failing the whole list.
    [Fact]
    public void Damaged_stored_json_reads_as_null()
    {
        using var db = new TestDatabase();
        var user = db.AddUser("damaged-user");
        var scan = db.AddScan(user.Id, DateTime.UtcNow);
        using (var write = db.CreateContext())
        {
            write.Database.ExecuteSqlRaw("UPDATE Scans SET Analysis = {0}", "{not json");
        }

        using var read = db.CreateContext();
        Assert.Null(read.Scans.Single(s => s.Id == scan.Id).Analysis);
    }

    // The DTO is camelCase and exposes design and api exactly as the contract says.
    [Fact]
    public void Dto_json_is_camel_case_with_design_and_api()
    {
        var scan = TestDatabase.NewScan(Guid.NewGuid(), DateTime.UtcNow);
        scan.Analysis = new ScanAnalysis(1, SampleDesign(), new ApiCatalog([], [], false));

        var json = JsonSerializer.SerializeToElement(scan.ToDto(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var analysis = json.GetProperty("analysis");

        Assert.Equal(1, analysis.GetProperty("version").GetInt32());
        Assert.Equal("#0f172a", analysis.GetProperty("design").GetProperty("colors")[0].GetProperty("hex").GetString());
        Assert.Equal("Tailwind", analysis.GetProperty("design").GetProperty("utilityFramework").GetString());
        Assert.False(analysis.GetProperty("api").GetProperty("truncated").GetBoolean());
    }

    // The API returns the analysis from the get endpoint, and null for an old scan.
    [Fact]
    public async Task Get_endpoint_returns_analysis_and_null_for_old_scans()
    {
        using var factory = new DefaultScansApiFactory();
        var uid = "analysis-" + Guid.NewGuid().ToString("N");
        var oldId = await factory.SeedScanAsync(uid, DateTime.UtcNow);
        var newId = await factory.SeedScanAsync(uid, DateTime.UtcNow, "new.example.com");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var scan = await db.Scans.SingleAsync(s => s.Id == newId);
            scan.Analysis = new ScanAnalysis(1, SampleDesign(), null);
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClientFor(uid);
        var fresh = await client.GetFromJsonAsync<JsonElement>($"/api/scans/{newId}");
        var old = await client.GetFromJsonAsync<JsonElement>($"/api/scans/{oldId}");
        var list = await client.GetFromJsonAsync<JsonElement>("/api/scans");

        Assert.Equal("Inter", fresh.GetProperty("analysis").GetProperty("design").GetProperty("fonts")[0].GetProperty("family").GetString());
        Assert.Equal(JsonValueKind.Null, old.GetProperty("analysis").ValueKind);
        Assert.Equal(2, list.GetArrayLength());
    }

    // An oversized analysis is cut to 64 KB: response shapes go first, then the endpoints.
    [Fact]
    public void Oversized_analysis_drops_shapes_then_endpoints()
    {
        var bigShape = new string('x', 2000);
        var endpoints = Enumerable.Range(0, 60).Select(i => Endpoint(i, bigShape)).ToList();
        var big = new ScanAnalysis(1, SampleDesign(), new ApiCatalog(endpoints, [], false));

        var limited = ScanAnalysisJson.Limit(big);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(ScanAnalysisJson.Serialize(limited)) <= ScanAnalysisJson.MaxBytes);
        Assert.All(limited.Api!.Endpoints, e => Assert.Null(e.ResponseShape));
        Assert.True(limited.Api.Truncated);
        Assert.NotEmpty(limited.Design!.Colors);

        var huge = new ScanAnalysis(1, SampleDesign(), new ApiCatalog(Enumerable.Range(0, 3000).Select(i => Endpoint(i)).ToList(), [], false));
        var cut = ScanAnalysisJson.Limit(huge);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(ScanAnalysisJson.Serialize(cut)) <= ScanAnalysisJson.MaxBytes);
        Assert.Empty(cut.Api!.Endpoints);
        Assert.NotEmpty(cut.Design!.Colors);
    }

    // A small analysis is left alone.
    [Fact]
    public void Small_analysis_is_unchanged()
    {
        var small = new ScanAnalysis(1, SampleDesign(), new ApiCatalog([Endpoint(1, "{a:number}")], [], false));

        Assert.Equal("{a:number}", ScanAnalysisJson.Limit(small).Api!.Endpoints[0].ResponseShape);
    }

    // The pipeline puts the digest's design and API data into the generated scan.
    [Fact]
    public async Task Pipeline_fills_analysis_from_the_digest()
    {
        var digest = Digests.Simple() with
        {
            Design = SampleDesign(),
            Api = new ApiCatalog([Endpoint(1)], [], false),
        };
        using var harness = new PipelineHarness(ScannerMode.Extract, null, digest);

        var scan = await harness.ScanAsync();

        Assert.Equal(1, scan.Analysis!.Version);
        Assert.Equal("Inter", scan.Analysis.Design!.Fonts[0].Family);
        Assert.Single(scan.Analysis.Api!.Endpoints);
    }

    // A page with no captured design or API data gives no analysis.
    [Fact]
    public async Task Pipeline_gives_null_analysis_when_nothing_was_captured()
    {
        using var harness = new PipelineHarness(ScannerMode.Extract, null, Digests.Simple());

        Assert.Null((await harness.ScanAsync()).Analysis);
    }

    // Canary secrets that look like tokens never reach the stored analysis, even when a page
    // declares them as CSS variables.
    [Fact]
    public async Task Secret_looking_values_do_not_reach_the_stored_analysis()
    {
        const string canaryOne = "CANARY-SECRET-ONE";
        const string canaryTwo = "CANARY-SECRET-TWO";
        const string canaryThree = "sk-CANARY-SECRET-THREE";
        var digest = Digests.Simple() with
        {
            Design = SampleDesign(
                new CssVariable("--api-token", canaryOne),
                new CssVariable("--session-id", canaryTwo),
                new CssVariable("--note", canaryThree),
                new CssVariable("--brand", "#38bdf8")),
        };
        using var harness = new PipelineHarness(ScannerMode.Extract, null, digest);

        var scan = await harness.ScanAsync();
        var json = ScanAnalysisJson.Serialize(scan.Analysis!);

        Assert.DoesNotContain("CANARY", json);
        Assert.Contains("--brand", json);
    }

    // Static mode attaches a small fixed sample design.
    [Fact]
    public async Task Static_generator_attaches_a_sample_design()
    {
        var generator = new StaticScanGenerator(TimeProvider.System);

        var scan = await generator.GenerateAsync(new Uri("https://example.com/"), CancellationToken.None);

        Assert.NotEmpty(scan.Analysis!.Design!.Colors);
        Assert.Null(scan.Analysis.Api);
    }
}
