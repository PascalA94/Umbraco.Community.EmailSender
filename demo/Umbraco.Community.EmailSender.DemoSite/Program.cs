
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);


builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();

// Core writes invitation emails to the pickup folder but does not create it; the e2e invited-user spec needs invites.
if (builder.Configuration["Umbraco:CMS:Global:Smtp:PickupDirectoryLocation"] is { Length: > 0 } pickup)
{
    Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, pickup));
}


await app.BootUmbracoAsync();


app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
