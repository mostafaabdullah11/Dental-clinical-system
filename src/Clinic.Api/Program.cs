using Clinic.Api.Composition;
using Clinic.Api.Operator;

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
