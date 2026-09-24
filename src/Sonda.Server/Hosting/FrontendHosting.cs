using Microsoft.Extensions.FileProviders;
namespace Sonda.Server.Hosting;

public static class FrontendHosting
{
    public static void UseFrontendAssets(this WebApplication app)
    {
        var root=app.Configuration["Frontend:Root"];
        if(string.IsNullOrWhiteSpace(root))return;
        var full=Path.GetFullPath(root);
        if(!File.Exists(Path.Combine(full,"index.html")))throw new InvalidOperationException("Frontend build not found.");
        // Public immutable code/assets contain no team data. API authentication remains unchanged.
        app.UseStaticFiles(new StaticFileOptions { FileProvider=new PhysicalFileProvider(full) });
        foreach(var route in new[]{"/","/search","/monitoring","/configuration","/account","/login","/redeem"})
            app.MapGet(route,()=>Results.File(Path.Combine(full,"index.html"),"text/html")).AllowAnonymous();
    }
}
