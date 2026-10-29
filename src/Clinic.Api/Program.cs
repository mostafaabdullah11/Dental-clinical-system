using Clinic.Api.Composition;
using Clinic.Api.Operator;

if (args.Contains("--healthcheck"))
{
    // Container healthcheck (DEPLOYMENT_ARCHITECTURE.md §1.9): the internal readiness probe over loopback; exit code only.
    return await ContainerHealthcheck.RunAsync();
}

var operatorMode = OperatorCli.IsOperatorCommand(args);

// Operator commands never receive raw arguments as configuration (they may contain a username only).
var builder = WebApplication.CreateBuilder(operatorMode ? [] : args);
builder.AddClinicApi(operatorMode);

var app = builder.Build();

if (operatorMode)
{
    return await OperatorCli.RunAsync(args, app.Services);
}

app.UseClinicApi();
await app.RunAsync();
return 0;

public partial class Program;
