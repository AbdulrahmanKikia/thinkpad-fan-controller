namespace LibraryFan.Setup;

/// <summary>
/// <c>--from zip --into folder</c> installs without a window so the package can be checked.
/// </summary>
public static class HeadlessInstall
{
    public static bool Try(string[] args, out int code)
    {
        code = 0;
        var from = Value(args, "--from");
        if (from is null)
            return false;

        try
        {
            var into = Value(args, "--into") ?? PackageInstall.DefaultAppDirectory();
            using var zip = File.OpenRead(from);
            PackageInstall.Extract(zip, into);
            if (Value(args, "--into") is null)
                PackageInstall.WriteStartMenuLauncher(PackageInstall.DefaultStartMenuCommand(), into);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            code = 1;
            return true;
        }
    }

    private static string? Value(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
