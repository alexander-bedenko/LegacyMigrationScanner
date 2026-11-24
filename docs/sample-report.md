# Migration report: LegacyShop.sln

Scanned 2026-11-24 09:53 with Legacy Migration Scanner 0.3.0. Estimate assumes 2 h per point.

## Summary

| Metric | Value |
|---|---|
| Projects | 4 |
| Findings (critical / warning / info) | 17 / 29 / 12 |
| Estimated effort, P50 | 223 h (~27.9 person-days) |
| Estimated effort, P90 | 381 h (~47.6 person-days) |

## Projects

| Project | Kind | Format | Target | LOC | Readiness | P50, h | P90, h | Top cost drivers |
|---|---|---|---|---:|---|---:|---:|---|
| LegacyShop.Web | AspNetWebApplication | Legacy | v4.8 | 95 | 🔴 Red | 116 | 185 | LMS004 (8), LMS600 (7), LMS113 (6) |
| LegacyShop.Data | ClassLibrary | Legacy | v4.8 | 55 | 🔴 Red | 55 | 95 | LMS400 (8), LMS302 (7.8), LMS402 (2) |
| LegacyShop.Services | WcfService | Legacy | v4.8 | 57 | 🔴 Red | 52 | 101 | LMS210 (6), LMS005 (5), LMS301 (5) |
| LegacyShop.Common | ClassLibrary | SDK | netstandard2.0 | 9 | 🟢 Green | 0 | 0 |  |

## Migration order

Projects in the same wave do not depend on each other and can be migrated in parallel.

1. LegacyShop.Common
2. LegacyShop.Data
3. LegacyShop.Services, LegacyShop.Web

## Issues by rule

| Rule | Severity | Title | Projects | Occurrences | Recommendation |
|---|---|---|---:|---:|---|
| LMS004 | Critical | ASP.NET (System.Web) web application project | 1 | 1 | Create a new ASP.NET Core host. For large apps consider incremental migration: YARP proxy in front plus System.Web adapters, moving endpoints one by one. |
| LMS005 | Critical | WCF service application project | 1 | 1 | Host services in ASP.NET Core with CoreWCF (closest to existing code) or redesign as gRPC / REST APIs. |
| LMS100 | Critical | ASP.NET Web Forms pages/controls | 1 | 1 | Web Forms are not available on modern .NET. Rewrite to Razor Pages / Blazor, or keep them on .NET Framework behind YARP while the rest migrates. |
| LMS111 | Critical | ASP.NET Web API 2 (System.Web.Http) | 1 | 3 | Port controllers to ASP.NET Core: ApiController -> ControllerBase + [ApiController], IHttpActionResult -> IActionResult, HttpConfiguration/routes -> Program.cs, filters -> ASP.NET Core filters. |
| LMS113 | Critical | Static HttpContext.Current access | 2 | 4 | Inject IHttpContextAccessor (or pass HttpContext explicitly). Look for usages in non-web layers: they indicate layering problems that must be untangled first. |
| LMS200 | Critical | WCF .svc service endpoints | 1 | 1 | Host with CoreWCF in ASP.NET Core (keeps contracts and SOAP clients working) or migrate to gRPC/REST. |
| LMS201 | Critical | WCF server configuration (system.serviceModel/services) | 1 | 1 | Move bindings/behaviors to code-based configuration in CoreWCF; check that every binding used is supported by CoreWCF. |
| LMS210 | Critical | WCF service contracts ([ServiceContract]) | 2 | 3 | Contracts can be reused with CoreWCF; for gRPC convert to .proto. Server-side contracts drive most of the WCF migration effort. |
| LMS212 | Critical | Self-hosted WCF (ServiceHost) | 1 | 1 | Replace ServiceHost with CoreWCF hosted in ASP.NET Core / Generic Host. |
| LMS301 | Critical | AppDomain creation | 1 | 1 | Modern .NET has a single AppDomain. Use AssemblyLoadContext for plugin isolation/unloading or separate processes for security isolation. |
| LMS302 | Critical | BinaryFormatter serialization | 1 | 3 | BinaryFormatter is removed from modern .NET (throws since .NET 9). Switch to System.Text.Json, MessagePack or protobuf and plan data migration for persisted payloads. |
| LMS400 | Critical | Entity Framework EDMX model | 1 | 1 | EF Core has no EDMX. Reverse-engineer with Scaffold-DbContext, or keep EF6 (6.3+) with a code-first model; the EF6 designer does not work in SDK-style projects. |
| LMS001 | Warning | Legacy (non SDK-style) project format | 3 | 3 | Convert to SDK-style .csproj (manually or with the try-convert / Copilot modernization tooling). Remove explicit <Compile> items and AssemblyInfo duplicates. |
| LMS002 | Warning | packages.config is used | 2 | 2 | Migrate to <PackageReference> (Visual Studio: right-click packages.config > Migrate packages.config to PackageReference). |
| LMS101 | Warning | Global.asax application events | 1 | 1 | Move Application_Start logic to Program.cs and per-request events (BeginRequest/EndRequest/Error) to middleware. |
| LMS102 | Warning | HTTP modules (IHttpModule) | 1 | 1 | Rewrite as ASP.NET Core middleware. |
| LMS103 | Warning | HTTP handlers (IHttpHandler / .ashx) | 1 | 1 | Rewrite as minimal API endpoints or middleware. |
| LMS104 | Warning | ASP.NET session state | 1 | 1 | Use ASP.NET Core ISession (async, byte[]/string only, requires explicit serialization) backed by a distributed cache; review what is stored in session. |
| LMS110 | Warning | Dependency on System.Web namespace | 2 | 4 | Replace System.Web types with ASP.NET Core equivalents (Microsoft.AspNetCore.Http). Note: System.Web.HttpUtility is still available on modern .NET. |
| LMS114 | Warning | OWIN / Katana pipeline | 1 | 2 | Replace OWIN middleware and Startup with the ASP.NET Core pipeline; Microsoft.Owin.Security.* -> ASP.NET Core authentication handlers. |
| LMS202 | Warning | WCF client configuration (system.serviceModel/client) | 1 | 1 | Modern .NET WCF client (System.ServiceModel.Http/NetTcp packages) does not read config files: move endpoints/bindings to code and appsettings.json. |
| LMS211 | Warning | WCF client proxies (ClientBase<T> / ChannelFactory<T>) | 1 | 1 | Client side is supported on modern .NET via System.ServiceModel.* packages; regenerate proxies with dotnet-svcutil and configure bindings in code. |
| LMS303 | Warning | System.Drawing usage | 1 | 1 | System.Drawing.Common is Windows-only on modern .NET. Fine if you stay on Windows; for Linux/containers use ImageSharp or SkiaSharp. |
| LMS402 | Warning | Entity Framework 6 (System.Data.Entity) | 1 | 2 | EF6 6.3+ runs on modern .NET (lowest-risk first step). Migrating to EF Core is a separate effort: query translation, lazy loading and configuration differ. |
| LMS500 | Warning | Custom configuration sections | 2 | 2 | Move to appsettings.json bound with IOptions<T>. |
| LMS600 | Warning | Package must be replaced | 1 | 7 | See details below |
| LMS606 | Warning | Package not found on configured feeds | 1 | 1 | See details below |
| LMS003 | Info | Targets .NET Framework | 3 | 3 | Retarget to net10.0 (or multi-target netstandard2.0;net48 for shared libraries during a gradual migration). |
| LMS304 | Info | Windows Registry access | 1 | 1 | Works on Windows only. Prefer configuration (appsettings / environment variables) if the target is containers/Linux. |
| LMS306 | Info | ConfigurationManager usage | 1 | 1 | Works via the System.Configuration.ConfigurationManager package, but prefer IConfiguration / IOptions<T> with appsettings.json. |
| LMS401 | Info | T4 text templates | 1 | 1 | T4 is not run by dotnet build. Commit generated code or switch to source generators. |
| LMS601 | Info | Package must be upgraded | 2 | 2 | See details below |
| LMS602 | Info | Package not needed on modern .NET | 1 | 3 | See details below |
| LMS605 | Info | Package works but is deprecated / needs review | 1 | 1 | See details below |

## Details

### LegacyShop.Data

| Severity | Rule | Location | Count | Detail | Recommendation |
|---|---|---|---:|---|---|
| Critical | LMS302 BinaryFormatter serialization | `LegacyShop.Data/SnapshotStore.cs:3` | 3 | System.Runtime.Serialization.Formatters.Binary |  |
| Critical | LMS400 Entity Framework EDMX model | `LegacyShop.Data/ShopModel.edmx` | 1 |  |  |
| Warning | LMS001 Legacy (non SDK-style) project format | `LegacyShop.Data/LegacyShop.Data.csproj` | 1 |  |  |
| Warning | LMS002 packages.config is used | `LegacyShop.Data/packages.config` | 1 | 3 packages |  |
| Warning | LMS303 System.Drawing usage | `LegacyShop.Data/SnapshotStore.cs:1` | 1 | System.Drawing |  |
| Warning | LMS402 Entity Framework 6 (System.Data.Entity) | `LegacyShop.Data/ShopContext.cs:1` | 1 | System.Data.Entity |  |
| Warning | LMS402 Entity Framework 6 (System.Data.Entity) | `LegacyShop.Data/ShopModel.Context.cs:4` | 1 | System.Data.Entity.DbContext |  |
| Warning | LMS500 Custom configuration sections | `LegacyShop.Data/App.config:4` | 1 | entityFramework |  |
| Info | LMS003 Targets .NET Framework | `LegacyShop.Data/LegacyShop.Data.csproj` | 1 | v4.8 |  |
| Info | LMS304 Windows Registry access | `LegacyShop.Data/SnapshotStore.cs:31` | 1 | LocalMachine |  |
| Info | LMS401 T4 text templates | `LegacyShop.Data/ShopModel.Context.tt` | 1 | 1 file(s): ShopModel.Context.tt |  |
| Info | LMS601 Package must be upgraded | `LegacyShop.Data/packages.config` | 1 | EntityFramework 6.2.0 -> 6.3.0 | Use EntityFramework 6.5.x (or migrate to EF Core separately). |
| Info | LMS605 Package works but is deprecated / needs review | `LegacyShop.Data/packages.config` | 1 | NEST 7.17.5 | Use Elastic.Clients.Elasticsearch (for Elasticsearch 8+). |

### LegacyShop.Services

| Severity | Rule | Location | Count | Detail | Recommendation |
|---|---|---|---:|---|---|
| Critical | LMS005 WCF service application project | `LegacyShop.Services/LegacyShop.Services.csproj` | 1 |  |  |
| Critical | LMS113 Static HttpContext.Current access | `LegacyShop.Services/OrderService.svc.cs:12` | 1 |  |  |
| Critical | LMS200 WCF .svc service endpoints | `LegacyShop.Services/OrderService.svc` | 1 | 1 file(s): OrderService.svc |  |
| Critical | LMS201 WCF server configuration (system.serviceModel/services) | `LegacyShop.Services/Web.config:8` | 1 | LegacyShop.Services.OrderService |  |
| Critical | LMS210 WCF service contracts ([ServiceContract]) | `LegacyShop.Services/IOrderService.cs:6` | 2 | IInventoryService, IOrderService |  |
| Critical | LMS212 Self-hosted WCF (ServiceHost) | `LegacyShop.Services/LegacyHost.cs:10` | 1 |  |  |
| Critical | LMS301 AppDomain creation | `LegacyShop.Services/LegacyHost.cs:15` | 1 |  |  |
| Warning | LMS001 Legacy (non SDK-style) project format | `LegacyShop.Services/LegacyShop.Services.csproj` | 1 |  |  |
| Warning | LMS110 Dependency on System.Web namespace | `LegacyShop.Services/OrderService.svc.cs:12` | 1 | System.Web.HttpContext.Current.Request.UserHostAddress |  |
| Info | LMS003 Targets .NET Framework | `LegacyShop.Services/LegacyShop.Services.csproj` | 1 | v4.8 |  |

### LegacyShop.Web

| Severity | Rule | Location | Count | Detail | Recommendation |
|---|---|---|---:|---|---|
| Critical | LMS004 ASP.NET (System.Web) web application project | `LegacyShop.Web/LegacyShop.Web.csproj` | 1 |  |  |
| Critical | LMS100 ASP.NET Web Forms pages/controls | `LegacyShop.Web/Default.aspx` | 1 | 1 file(s): Default.aspx |  |
| Critical | LMS111 ASP.NET Web API 2 (System.Web.Http) | `LegacyShop.Web/App_Start/WebApiConfig.cs:1` | 1 | System.Web.Http |  |
| Critical | LMS111 ASP.NET Web API 2 (System.Web.Http) | `LegacyShop.Web/Controllers/OrdersController.cs:4` | 1 | System.Web.Http |  |
| Critical | LMS111 ASP.NET Web API 2 (System.Web.Http) | `LegacyShop.Web/Global.asax.cs:3` | 1 | System.Web.Http |  |
| Critical | LMS113 Static HttpContext.Current access | `LegacyShop.Web/Controllers/OrdersController.cs:16` | 2 |  |  |
| Critical | LMS113 Static HttpContext.Current access | `LegacyShop.Web/Global.asax.cs:16` | 1 |  |  |
| Critical | LMS210 WCF service contracts ([ServiceContract]) | `LegacyShop.Web/Controllers/OrdersController.cs:38` | 1 | IOrderServiceProxy |  |
| Warning | LMS001 Legacy (non SDK-style) project format | `LegacyShop.Web/LegacyShop.Web.csproj` | 1 |  |  |
| Warning | LMS002 packages.config is used | `LegacyShop.Web/packages.config` | 1 | 14 packages |  |
| Warning | LMS101 Global.asax application events | `LegacyShop.Web/Global.asax` | 1 |  |  |
| Warning | LMS102 HTTP modules (IHttpModule) | `LegacyShop.Web/Web.config:15` | 1 | AuditModule |  |
| Warning | LMS103 HTTP handlers (IHttpHandler / .ashx) | `LegacyShop.Web/Web.config:23` | 1 | report.axd |  |
| Warning | LMS104 ASP.NET session state | `LegacyShop.Web/Web.config:13` | 1 | mode=InProc |  |
| Warning | LMS110 Dependency on System.Web namespace | `LegacyShop.Web/Controllers/OrdersController.cs:3` | 1 | System.Web |  |
| Warning | LMS110 Dependency on System.Web namespace | `LegacyShop.Web/Default.aspx.cs:2` | 1 | System.Web.UI |  |
| Warning | LMS110 Dependency on System.Web namespace | `LegacyShop.Web/Global.asax.cs:2` | 1 | System.Web |  |
| Warning | LMS114 OWIN / Katana pipeline | `LegacyShop.Web/App_Start/Startup.cs:1` | 2 | Microsoft.Owin |  |
| Warning | LMS202 WCF client configuration (system.serviceModel/client) | `LegacyShop.Web/Web.config:28` | 1 | OrderServiceRef.IOrderService |  |
| Warning | LMS211 WCF client proxies (ClientBase<T> / ChannelFactory<T>) | `LegacyShop.Web/Controllers/OrdersController.cs:33` | 1 | ClientBase |  |
| Warning | LMS500 Custom configuration sections | `LegacyShop.Web/Web.config:4` | 1 | billing |  |
| Warning | LMS600 Package must be replaced | `LegacyShop.Web/packages.config` | 1 | Microsoft.AspNet.WebApi 5.2.9 | Use ASP.NET Core (built into Microsoft.AspNetCore.App). |
| Warning | LMS600 Package must be replaced | `LegacyShop.Web/packages.config` | 1 | Microsoft.AspNet.WebApi.Core 5.2.9 | Use ASP.NET Core (built into Microsoft.AspNetCore.App). |
| Warning | LMS600 Package must be replaced | `LegacyShop.Web/packages.config` | 1 | Microsoft.AspNet.WebApi.Owin 5.2.9 | Use ASP.NET Core (built into Microsoft.AspNetCore.App). |
| Warning | LMS600 Package must be replaced | `LegacyShop.Web/packages.config` | 1 | Microsoft.Owin 4.2.2 | Use ASP.NET Core middleware pipeline. |
| Warning | LMS600 Package must be replaced | `LegacyShop.Web/packages.config` | 1 | Microsoft.Owin.Host.SystemWeb 4.2.2 | Use ASP.NET Core middleware pipeline. |
| Warning | LMS600 Package must be replaced | `LegacyShop.Web/packages.config` | 1 | Unity.WebAPI 5.4.0 | Use Microsoft.Extensions.DependencyInjection (or Unity.Microsoft.DependencyInjection). |
| Warning | LMS600 Package must be replaced | `LegacyShop.Web/packages.config` | 1 | WindowsAzure.ServiceBus 6.2.2 | Use Azure.Messaging.ServiceBus. |
| Warning | LMS606 Package not found on configured feeds | `LegacyShop.Web/packages.config` | 1 | Acme.Internal.Billing 1.4.0 | Usually an internal package: add its feed with --nuget-source, or plan to multi-target it (netstandard2.0;net48) before migrating consumers. |
| Info | LMS003 Targets .NET Framework | `LegacyShop.Web/LegacyShop.Web.csproj` | 1 | v4.8 |  |
| Info | LMS306 ConfigurationManager usage | `LegacyShop.Web/Controllers/OrdersController.cs:11` | 1 | AppSettings |  |
| Info | LMS601 Package must be upgraded | `LegacyShop.Web/packages.config` | 1 | EntityFramework 6.2.0 -> 6.3.0 | Use EntityFramework 6.5.x (or migrate to EF Core separately). |
| Info | LMS602 Package not needed on modern .NET | `LegacyShop.Web/packages.config` | 1 | Microsoft.CodeDom.Providers.DotNetCompilerPlatform 2.0.1 | Remove the package; the functionality is built into the SDK/runtime. |
| Info | LMS602 Package not needed on modern .NET | `LegacyShop.Web/packages.config` | 1 | Microsoft.Web.Infrastructure 2.0.0 | Remove the package; the functionality is built into the SDK/runtime. |
| Info | LMS602 Package not needed on modern .NET | `LegacyShop.Web/packages.config` | 1 | Owin 1.0 | Remove the package; the functionality is built into the SDK/runtime. |

## Packages

16 distinct package version(s), 3 compatible as-is.

| Package | Version | Status | Target | Source | Note |
|---|---|---|---|---|---|
| EntityFramework | 6.2.0 | UpgradeRequired | 6.3.0 | catalog |  |
| Microsoft.AspNet.WebApi | 5.2.9 | Replace | ASP.NET Core (built into Microsoft.AspNetCore.App) | catalog |  |
| Microsoft.AspNet.WebApi.Core | 5.2.9 | Replace | ASP.NET Core (built into Microsoft.AspNetCore.App) | catalog |  |
| Microsoft.AspNet.WebApi.Owin | 5.2.9 | Replace | ASP.NET Core (built into Microsoft.AspNetCore.App) | catalog |  |
| Microsoft.Owin | 4.2.2 | Replace | ASP.NET Core middleware pipeline | catalog |  |
| Microsoft.Owin.Host.SystemWeb | 4.2.2 | Replace | ASP.NET Core middleware pipeline | catalog |  |
| Unity.WebAPI | 5.4.0 | Replace | Microsoft.Extensions.DependencyInjection (or Unity.Microsoft.DependencyInjection) | catalog |  |
| WindowsAzure.ServiceBus | 6.2.2 | Replace | Azure.Messaging.ServiceBus | catalog |  |
| Microsoft.CodeDom.Providers.DotNetCompilerPlatform | 2.0.1 | Remove |  | catalog |  |
| Microsoft.Web.Infrastructure | 2.0.0 | Remove |  | catalog |  |
| Owin | 1.0 | Remove |  | catalog |  |
| NEST | 7.17.5 | Review | Elastic.Clients.Elasticsearch (for Elasticsearch 8+) | catalog |  |
| Acme.Internal.Billing | 1.4.0 | NotFound |  | api.nuget.org | Not found on configured NuGet sources |

No issues found in: LegacyShop.Common.
