using Hd2Planets.EventArgs;
using Hd2Planets.Logic;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using System;
using System.IO;

namespace Hd2Planets
{
    internal static class Program
    {
#if DEBUG
        private readonly static LogEventLevel logLevel = LogEventLevel.Verbose;
#else
        private readonly static LogEventLevel logLevel = LogEventLevel.Information;
#endif
        private static Microsoft.Extensions.Logging.ILogger pLogger;

        private readonly static string dbPath = Path.Combine(Environment.CurrentDirectory, "planets.db");

        private static void CreateLogger()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.FromLogContext()
                .WriteTo.Console(restrictedToMinimumLevel: logLevel)
                .WriteTo.Debug(restrictedToMinimumLevel: logLevel)
#if DEBUG
                .Enrich.WithProperty("Debug", true)
#endif
                .CreateLogger();

            pLogger = new SerilogLoggerFactory(Log.Logger).CreateLogger("Program");
            pLogger.LogTrace("Logger created");
        }

        static void Main(string[] args)
        {
            CreateLogger();

            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }

            using (SqliteDatabase db = new(dbPath, new SerilogLoggerFactory(Log.Logger).CreateLogger("Database")))
            {
                db.Started += (s, e) => pLogger.LogInformation("Started");
                db.Completed += OnCompleted;
                db.DownloadAndCreateDatabase().Wait();
            }

            Log.CloseAndFlush();
            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }

        private static void OnCompleted(object sender, SqliteDatabaseCompletedEventArgs e)
        {
            pLogger.LogInformation("Completed \"{objectname}\" - duration: {duration}", sender.GetType().Name, e.DurationString);
        }
    }
}