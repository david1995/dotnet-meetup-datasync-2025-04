using CommunityToolkit.Datasync.Server;
using Microsoft.EntityFrameworkCore;
using Web.Db;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDatasyncServices();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

var app = builder.Build();

// Initialize the database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await context.InitializeDatabaseAsync().ConfigureAwait(false);
}

// Configure and run the web service.

// use Swashbuckle because of this: https://github.com/CommunityToolkit/Datasync/issues/48#issuecomment-2649462851
// and this: https://github.com/CommunityToolkit/Datasync/issues/266
app.MapSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.Run();
