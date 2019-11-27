using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace RobustNugetRestore
{
    class Program
    {
        static int Main(string[] args)
        {
            var parsedArgs = args.TakeWhile(a => a != "--").ToArray();
            int maxretries = 100;
            if (parsedArgs.Length > 3 || (parsedArgs.Length > 1 && !int.TryParse(parsedArgs[1], out maxretries)))
            {
                Log("Usage: RobustNugetRestore [solutionfile] [maxretries] [source1,source2,...]", ConsoleColor.Red);
                return 1;
            }

            var solutionfile = parsedArgs.Length < 1 ? null : parsedArgs[0];
            var sources = parsedArgs.Length < 3 ? null : parsedArgs[2].Split(',');

            return RestorePackages(solutionfile, maxretries, sources) ? 0 : 1;
        }

        static bool RestorePackages(string? solutionfile, int maxretries, string[]? sources)
        {
            var nugetexe = LocateNugetBinary();
            if (nugetexe == null)
            {
                Log("nuget binary not found.");
                return false;
            }

            Log($"Using nuget: '{nugetexe}'");

            for (var tries = 1; tries <= maxretries; tries++)
            {
                int exitcode = LogTCSection($"Nuget restore, try {tries}", () =>
                {
                    string processArgs;
                    if (solutionfile == null)
                    {
                        processArgs = "restore";
                    }
                    else
                    {
                        if (sources == null)
                        {
                            processArgs = $"restore \"{solutionfile}\"";
                        }
                        else
                        {
                            processArgs = $"restore \"{solutionfile}\" -Source {string.Join(" -Source ", sources)}";
                        }
                    }

                    Log($"Running: {nugetexe} {processArgs}");

                    var process = Process.Start(nugetexe, processArgs);
                    process.WaitForExit();
                    return process.ExitCode;
                });

                if (exitcode != 0)
                {
                    if (tries == maxretries)
                    {
                        LogTCError($"Could not restore nuget packages, try {tries}");
                        LogTCStat("NugetRestoreTries", tries);
                    }
                    else
                    {
                        LogTCWarning($"Could not restore nuget packages, try {tries}");
                        Thread.Sleep(5000);
                    }
                }
                else
                {
                    Log("Success!", ConsoleColor.Green);
                    LogTCStat("NugetRestoreTries", tries);
                    return true;
                }
            }

            return false;
        }

        static string? LocateNugetBinary()
        {
            Log("Searching for nuget binary...");

            var nugetpath = LocateNugetUsingTCVars();
            if (nugetpath != null)
            {
                return nugetpath;
            }

            nugetpath = LocateNugetUsingPath();
            if (nugetpath != null)
            {
                return nugetpath;
            }

            nugetpath = LocateNugetByDownloading();
            if (nugetpath != null)
            {
                return nugetpath;
            }

            return null;
        }

        static string? LocateNugetUsingTCVars()
        {
            var tcvars = GetTeamcityVariables();
            var nugetkey = "teamcity.tool.NuGet.CommandLine.DEFAULT";

            if (tcvars.ContainsKey(nugetkey))
            {
                string path = Path.Combine(tcvars[nugetkey], "tools", "nuget.exe");
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        static string? LocateNugetUsingPath()
        {
            var pathVariable = Environment.GetEnvironmentVariable("path");
            if (string.IsNullOrEmpty(pathVariable))
            {
                LogTCWarning("Couldn't find path variable.");
                return null;
            }
            var paths = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

            foreach (var path in paths)
            {
                Log($"Searching: '{path}'");
                if (File.Exists(Path.Combine(path, "nuget.exe")))
                {
                    return path;
                }
            }

            return null;
        }

        static string? LocateNugetByDownloading()
        {
            var localfile = "nuget.exe";

            if (File.Exists(localfile) && new FileInfo(localfile).Length > 0)
            {
                return localfile;
            }

            var url = "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe";

            Log($"Downloading: '{url}' -> '{localfile}'");
            using var client = new WebClient();
            try
            {
                client.DownloadFile(url, localfile);
            }
            catch (WebException ex)
            {
                Log($"Couldn't download '{url}': {ex.Message}");
                return null;
            }

            return localfile;
        }

        static Dictionary<string, string> GetTeamcityVariables()
        {
            Dictionary<string, string> empty = new Dictionary<string, string>();

            var buildpropfile = Environment.GetEnvironmentVariable("TEAMCITY_BUILD_PROPERTIES_FILE");
            if (string.IsNullOrEmpty(buildpropfile))
            {
                LogTCWarning("Couldn't find Teamcity build properties file.");
                return empty;
            }
            if (!File.Exists(buildpropfile))
            {
                LogTCWarning($"Couldn't find Teamcity build properties file: '{buildpropfile}'");
                return empty;
            }

            Log($"Reading Teamcity build properties file: '{buildpropfile}'");
            var rows = File.ReadAllLines(buildpropfile);

            var valuesBuild = GetPropValues(rows);

            var configpropfile = valuesBuild["teamcity.configuration.properties.file"];
            if (string.IsNullOrEmpty(configpropfile))
            {
                LogTCWarning("Couldn't find Teamcity config properties file.");
                return empty;
            }
            if (!File.Exists(configpropfile))
            {
                LogTCWarning($"Couldn't find Teamcity config properties file: '{configpropfile}'");
                return empty;
            }

            Log($"Reading Teamcity config properties file: '{configpropfile}'");
            rows = File.ReadAllLines(configpropfile);

            var valuesConfig = GetPropValues(rows);

            return valuesConfig;
        }

        static Dictionary<string, string> GetPropValues(string[] rows)
        {
            Dictionary<string, string> dic = new Dictionary<string, string>();

            foreach (string row in rows)
            {
                int index = row.IndexOf('=');
                if (index != -1)
                {
                    string key = row.Substring(0, index);
                    string value = Regex.Unescape(row.Substring(index + 1));
                    dic[key] = value;
                }
            }

            return dic;
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
        }

        static void LogTCWarning(string message)
        {
            Log($"##teamcity[message text='{message}' status='WARNING']", ConsoleColor.Yellow);
        }

        static void LogTCError(string message)
        {
            Log($"##teamcity[message text='{message}' status='ERROR']", ConsoleColor.Red);
        }

        static void Log(string message, ConsoleColor color)
        {
            ConsoleColor oldcolor = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(message);
            }
            finally
            {
                Console.ForegroundColor = oldcolor;
            }
        }

        static void LogTCStat(string key, long value)
        {
            Log($"##teamcity[buildStatisticValue key='{key}' value='{value}']", ConsoleColor.Magenta);
        }

        static T LogTCSection<T>(string message, Func<T> func)
        {
            ConsoleColor oldColor = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"##teamcity[blockOpened name='{message}']");
            }
            finally
            {
                Console.ForegroundColor = oldColor;
            }

            T result = func.Invoke();

            oldColor = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"##teamcity[blockClosed name='{message}']");
            }
            finally
            {
                Console.ForegroundColor = oldColor;
            }

            return result;
        }
    }
}
