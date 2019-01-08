using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace RobustNugetRestore
{
    class Program
    {
        static int Main(string[] args)
        {
            string[] parsedArgs = args.TakeWhile(a => a != "--").ToArray();
            if (parsedArgs.Length > 1)
            {
                Log("Usage: RobustNugetRestore [solutionfile]", ConsoleColor.Red);
                return 1;
            }

            string solutionfile = parsedArgs.Length < 1 ? null : parsedArgs[0];

            return RestorePackages(solutionfile) ? 0 : 1;
        }

        static bool RestorePackages(string solutionfile)
        {
            var tcvars = GetTeamcityVariables();

            string nugetexe = Path.Combine(tcvars["teamcity.tool.NuGet.CommandLine.DEFAULT"], "tools", "NuGet.exe");

            Log($"Using nuget: '{nugetexe}'");

            for (var tries = 0; tries < 10; tries++)
            {
                int exitcode = LogTCSection($"Nuget restore, try {tries}", () =>
                {
                    string processArgs;
                    if (solutionfile == null)
                    {
                        Log("Restoring");
                        processArgs = "restore";
                    }
                    else
                    {
                        Log($"Restoring: '{solutionfile}'");
                        processArgs = $"restore \"{solutionfile}\"";
                    }
                    var process = Process.Start(nugetexe, processArgs);
                    process.WaitForExit();
                    return process.ExitCode;
                });

                if (exitcode != 0)
                {
                    if (tries == 9)
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

        static Dictionary<string, string> GetTeamcityVariables()
        {
            Dictionary<string, string> empty = new Dictionary<string, string>();

            string buildpropfile = Environment.GetEnvironmentVariable("TEAMCITY_BUILD_PROPERTIES_FILE");
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
            string[] rows = File.ReadAllLines(buildpropfile);

            var valuesBuild = GetPropValues(rows);

            string configpropfile = valuesBuild["teamcity.configuration.properties.file"];
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
