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
        private readonly static LogEventLevel _logLevel = LogEventLevel.Verbose;
#else
        private readonly static LogEventLevel logLevel = LogEventLevel.Information;
#endif
        private static Microsoft.Extensions.Logging.ILogger _pLogger;

        private readonly static string _dbPath = Path.Combine(Environment.CurrentDirectory, "planets.db");

        private static void CreateLogger()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.FromLogContext()
                .WriteTo.Console(restrictedToMinimumLevel: _logLevel)
                .WriteTo.Debug(restrictedToMinimumLevel: _logLevel)
#if DEBUG
                .Enrich.WithProperty("Debug", true)
#endif
                .CreateLogger();

            _pLogger = new SerilogLoggerFactory(Log.Logger).CreateLogger("Program");
            _pLogger.LogTrace("Logger created");
        }

        static void Main(string[] args)
        {
            CreateLogger();

            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }

            using (SqliteDatabase db = new(_dbPath, new SerilogLoggerFactory(Log.Logger).CreateLogger("Database")))
            {
                db.Started += (s, e) => _pLogger.LogInformation("Started");
                db.Completed += OnCompleted;
                db.DownloadAndCreateDatabase().Wait();
            }

            Log.CloseAndFlush();
            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }

        private static void OnCompleted(object sender, SqliteDatabaseCompletedEventArgs e)
        {
            _pLogger.LogInformation("Completed \"{objectname}\" - duration: {duration}", sender.GetType().Name, e.DurationString);
        }
    }
}