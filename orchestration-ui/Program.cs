using Microsoft.Extensions.Logging.Abstractions;
using Selma.Orchestration.TaskMQUtils;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor()
       // .AddHubOptions(options => options.MaximumReceiveMessageSize = 128 * 1024);
       .AddHubOptions(options => options.MaximumReceiveMessageSize = long.MaxValue);
builder.Services.AddLogging();
var configuration = builder.Configuration
              .SetBasePath(Directory.GetCurrentDirectory())
              .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
              .AddJsonFile("credentials.json", optional: true, reloadOnChange: false)
              .AddEnvironmentVariables();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
  app.UseExceptionHandler("/Error");
  // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
  app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub(o =>
{
  o.ApplicationMaxBufferSize = long.MaxValue;
  o.TransportMaxBufferSize = long.MaxValue;
});
app.MapFallbackToPage("/_Host");

app.Run();
