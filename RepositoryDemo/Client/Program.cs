global using System.Net.Http.Json;
global using Newtonsoft.Json;
global using System.Net;
global using AvnRepository;
global using BlazorDB;
global using Microsoft.JSInterop;
global using Microsoft.AspNetCore.SignalR.Client;
global using Dapper.Contrib.Extensions;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RepositoryDemo.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<CustomerRepository>();
builder.Services.AddBlazorDB(options =>
{
    options.Name = "RepositoryDemo";
    options.Version = 1;

    // List all your entities here, but as StoreSchema objects
    options.StoreSchemas = new List<StoreSchema>()
    {
        new StoreSchema()
        {
            Name = "Customer",      // Name of entity
            PrimaryKey = "Id",      // Primary Key of entity
            PrimaryKeyAuto = true,  // Whether or not the Primary key is generated
            Indexes = new List<string> { "Id" }
        },
        new StoreSchema()
        {
            Name = $"Customer{Globals.LocalTransactionsSuffix}",
            PrimaryKey = "Id",
            PrimaryKeyAuto = true,
            Indexes = new List<string> { "Id" }
        },
        new StoreSchema()
        {
            Name = $"Customer{Globals.KeysSuffix}",
            PrimaryKey = "Id",
            PrimaryKeyAuto = true,
            Indexes = new List<string> { "Id" }
        }
    };
});

builder.Services.AddScoped<CustomerIndexedDBSyncRepository>();
await builder.Build().RunAsync();