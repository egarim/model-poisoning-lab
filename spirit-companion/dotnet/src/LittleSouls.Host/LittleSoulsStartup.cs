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
        app.UseDefaultFiles(new DefaultFilesOptions { RequestPath = "/souls", FileProvider = web });
        app.UseStaticFiles(new StaticFileOptions { RequestPath = "/souls", FileProvider = web });
        next(app);
        // Reached only when none of Singular's endpoints matched; /api/app requests have their context resolved by then.
        app.Map("/api/app/littlesouls", branch => branch.UseLittleSoulsEndpoints());
    };
}
