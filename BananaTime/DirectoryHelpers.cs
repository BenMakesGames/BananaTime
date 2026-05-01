using System;
using System.IO;

namespace BananaTime;

public static class DirectoryHelpers
{
    private static readonly string AppDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        public static readonly string BananaTimeDirectory = $"{AppDataDirectory}{Path.DirectorySeparatorChar}BananaTime";

    public static readonly string LogDirectory = $"{BananaTimeDirectory}{Path.DirectorySeparatorChar}Logs";
    public static readonly string SaveDirectory = $"{BananaTimeDirectory}{Path.DirectorySeparatorChar}Saves";

    public static void EnsureDirectoryExists()
    {
        Directory.CreateDirectory(BananaTimeDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(SaveDirectory);
    }
}
