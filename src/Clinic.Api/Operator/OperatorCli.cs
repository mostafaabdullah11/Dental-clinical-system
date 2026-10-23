using Clinic.Modules.Identity.Operator;

namespace Clinic.Api.Operator;

/// <summary>
/// Server-local operator commands, run inside the api container (`dotnet Clinic.Api.dll &lt;command&gt;`), never over
/// HTTP (FR-AUTH-009). The password is read from the terminal without echo (or from redirected stdin) and is
/// never printed, logged or audited.
/// </summary>
internal static class OperatorCli
{
    public const string CreateInitialManager = "create-initial-manager";
    public const string SetManagerPassword = "set-manager-password";

    public static bool IsOperatorCommand(string[] args) =>
        args.Length > 0 && args[0] is CreateInitialManager or SetManagerPassword;

    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine($"Usage: {args[0]} <username>");
            return 2;
        }

        var userName = args[1];
        var password = ReadPassword("Password: ");
        var confirmation = ReadPassword("Confirm password: ");
        if (password != confirmation)
        {
            Console.Error.WriteLine("Passwords do not match. Nothing was changed.");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<OperatorCommands>();
        var (outcome, fieldError) = args[0] == CreateInitialManager
            ? await commands.CreateInitialManagerAsync(userName, password, CancellationToken.None)
            : await commands.SetManagerPasswordAsync(userName, password, CancellationToken.None);

        switch (outcome)
        {
            case OperatorOutcome.Succeeded:
                Console.WriteLine("Done. The action was audited with the operator marker.");
                return 0;
            case OperatorOutcome.AlreadyInitialized:
                Console.Error.WriteLine("A Manager account already exists. The initial-Manager command runs only once.");
                return 3;
            case OperatorOutcome.ManagerNotFound:
                Console.Error.WriteLine("No Manager account with that username exists.");
                return 4;
            default:
                Console.Error.WriteLine($"Rejected: {fieldError}");
                return 5;
        }
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            var line = Console.ReadLine() ?? string.Empty;
            Console.WriteLine();
            return line;
        }

        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}
