# graphql-dotnet-persistedqueries

Persisted Queries for [Graphql-Dotnet](https://github.com/graphql-dotnet/graphql-dotnet)

## Usage

### Get graphqlmap.json file using relay-compiler
```
> relay-compiler --src ./src --schema ./schema.graphql --watchman false --persist-output C:\graphqlmap.json
```

### Inject Library 

```csharp
      public void ConfigureServices(IServiceCollection services)
      {            
        container.AddSingleton<CacheQueryMonitor>(sp =>
          new CacheQueryMonitor(sp.GetService<IDistributedCache>(),@"c:\graphqlmap.json"));
      }
      public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
      {
        app.Use(next => context => {
          context.Request.EnableBuffering();
          return next(context);
        });
        app.UseMiddleware<PersistedGraphQLHttpMiddleware>();
      }
```
