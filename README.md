# graphql-dotnet-persistedqueries

Persisted Queries for Graphql-Dotnet

## Usage
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

