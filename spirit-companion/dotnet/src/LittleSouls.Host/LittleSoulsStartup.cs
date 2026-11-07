using LittleSouls.Host;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using SingularFramework.Apps.LittleSouls;

[assembly: HostingStartup(typeof(LittleSoulsStartup))]

namespace LittleSouls.Host;

/// <summary>
/// How Little Souls joins Singular's host without editing it: ASP.NET runs the application assembly's hosting startups
/// while Singular's Program.cs builds the host. Services go in here; the web app and the daemon's endpoints are appended
/// after Singular's pipeline by <see cref="LittleSoulsPipeline"/>, so Singular's authentication and app-context
/// resolution have already run when a request reaches them.
/// </summary>
public sealed class LittleSoulsStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices((ctx, services) =>
    {
        services.AddLittleSoulsApp(ctx.Configuration);
        services.AddLittleSoulsTurn(ctx.Configuration);
        services.AddTransient<IStartupFilter, LittleSoulsPipeline>();
    });
}

/// <summary>The web app at /souls/, and the daemon's endpoints under /api/app/littlesouls/ after Singular's own.</summary>
public sealed class LittleSoulsPipeline(IWebHostEnvironment env) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        var web = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, "wwwroot"));
        // The site's root is the app.
        app.Use(async (http, nextMw) =>
        {
            if (http.Request.Path == "/" && HttpMethods.IsGet(http.Request.Method)) { http.Response.Redirect("/souls/"); return; }
            await nextMw(http);
        });
        // Sign-in on the app's own address: the browser reaches one origin (a preview or a single domain), and the
        // host forwards the two grants the web app uses to Keycloak, for this app's client only.
        app.Map("/souls/auth/token", branch => branch.Run(KeycloakToken.ForwardAsync));
        app.UseDefaultFiles(new DefaultFilesOptions { RequestPath = "/souls", FileProvider = web });
        app.UseStaticFiles(new StaticFileOptions { RequestPath = "/souls", FileProvider = web });
        next(app);
        // Reached only when none of Singular's endpoints matched; /api/app requests have their context resolved by then.
        app.Map("/api/app/littlesouls", branch => branch.UseLittleSoulsEndpoints());
    };
}

/// <summary>Forwards the web app's password and refresh-token grants to Keycloak's token endpoint (Keycloak:Issuer).</summary>
internal static class KeycloakToken
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task ForwardAsync(HttpContext http)
    {
        if (!HttpMethods.IsPost(http.Request.Method) || !http.Request.HasFormContentType) { http.Response.StatusCode = 405; return; }
        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        var config = http.RequestServices.GetRequiredService<IConfiguration>();
        string issuer = (config["Keycloak:Issuer"] ?? "").Split(',')[0].Trim().TrimEnd('/');
        string grant = form["grant_type"].ToString(), client = form["client_id"].ToString();
        // only this app's own clients (provisioning names them <tenant>-littlesouls), and only these two grants
        if (issuer.Length == 0 || !client.EndsWith("-" + LittleSoulsApp.Slug, StringComparison.Ordinal) || grant is not ("password" or "refresh_token"))
        {
            http.Response.StatusCode = 400;
            await http.Response.WriteAsJsonAsync(new { error = "invalid_request" });
            return;
        }
        var fields = new Dictionary<string, string> { ["grant_type"] = grant, ["client_id"] = client, ["scope"] = "openid" };
        foreach (var k in new[] { "username", "password", "refresh_token" })
            if (form.TryGetValue(k, out var v) && v.Count > 0) fields[k] = v.ToString();
        using var res = await Http.PostAsync($"{issuer}/protocol/openid-connect/token", new FormUrlEncodedContent(fields), http.RequestAborted);
        http.Response.StatusCode = (int)res.StatusCode;
        http.Response.ContentType = "application/json";
        http.Response.Headers.CacheControl = "no-store";
        await res.Content.CopyToAsync(http.Response.Body, http.RequestAborted);
    }
}
