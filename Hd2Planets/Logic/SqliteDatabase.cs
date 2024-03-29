#pragma warning disable S1075
#pragma warning disable S6603

using Hd2Planets.EventArgs;
using Hd2Planets.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hd2Planets.Logic
{
    internal class SqliteDatabase : IDisposable
    {
        #region Members

        private bool _disposedValue;
        internal SqliteConnection _sqliteConnection;
        internal string _downloadedJson;
        internal ILogger _logger;
        #endregion

        #region Properties

        public string DatabasePath { get; init; }
        public List<Planet> Planets { get; private set; }

        #endregion

        #region Events

        public event EventHandler Started;
        public event EventHandler<SqliteDatabaseCompletedEventArgs> Completed;

        #endregion 

        #region Constructor
        public SqliteDatabase(string pathToDatabase, ILogger logger = null)
        {
            this._logger = logger;
            this.DatabasePath = pathToDatabase;
        }
        #endregion

        #region DB init
        private async Task<bool> OpenDatabaseConnection()
        {
            if (this._sqliteConnection != null)
            {
                return this._sqliteConnection.State == System.Data.ConnectionState.Open;
            }

            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = this.DatabasePath
            };

            this._sqliteConnection = new(builder.ToString());
            await this._sqliteConnection.OpenAsync();

            return this._sqliteConnection.State == System.Data.ConnectionState.Open;
        }

        private async Task CloseDatabaseConnection()
        {
            if (this._sqliteConnection == null)
            {
                return;
            }

            await this._sqliteConnection.CloseAsync();
            this._sqliteConnection.Dispose();
            this._sqliteConnection = null;
        }

        private async Task<bool> CreateDatabase()
        {
            bool[] tablesCreated = new bool[5];

            using (SqliteTransaction t = this._sqliteConnection.BeginTransaction())
            {
                using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                {
                    cmd.CommandText = "CREATE TABLE environments (id INTEGER PRIMARY KEY, name TEXT UNIQUE, description TEXT);";
                    await cmd.ExecuteNonQueryAsync();
                }

                using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                {
                    cmd.CommandText = "CREATE TABLE biomes (id INTEGER PRIMARY KEY, slug TEXT UNIQUE, description TEXT);";
                    await cmd.ExecuteNonQueryAsync();
                }

                using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                {
                    cmd.CommandText = "CREATE TABLE sectors (id INTEGER PRIMARY KEY, name TEXT UNIQUE);";
                    await cmd.ExecuteNonQueryAsync();
                }

                using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                {
                    cmd.CommandText = "CREATE TABLE planets (id INTEGER PRIMARY KEY, `index` INTEGER UNIQUE, name TEXT, sector INT REFERENCES 'sectors'('id'), biome INT REFERENCES 'biomes'('id'));";
                    await cmd.ExecuteNonQueryAsync();
                }

                using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                {
                    cmd.CommandText = "CREATE TABLE environmentsMapping (id INTEGER PRIMARY KEY, planet INT REFERENCES 'planets'('id'), env INT REFERENCES 'environments'('id'));";
                    await cmd.ExecuteNonQueryAsync();
                }

                await t.CommitAsync();

                KeyValuePair<int, string>[] tables = [
                        new KeyValuePair<int,string>(0, "environments"),
                        new KeyValuePair<int,string>(1, "biomes"),
                        new KeyValuePair<int,string>(2, "planets"),
                        new KeyValuePair<int,string>(3, "environmentsMapping"),
                        new KeyValuePair<int,string>(4, "sectors"),
                        ];

                tablesCreated[0] = await DoesDatabaseTableExist("environments");
                tablesCreated[1] = await DoesDatabaseTableExist("biomes");
                tablesCreated[2] = await DoesDatabaseTableExist("planets");
                tablesCreated[3] = await DoesDatabaseTableExist("environmentsMapping");
                tablesCreated[4] = await DoesDatabaseTableExist("sectors");
            }

            return tablesCreated.All(x => x);
        }

        private async Task<bool> DoesDatabaseTableExist(string tablename)
        {
            using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@table;";
                cmd.Parameters.AddWithValue("@table", tablename);

                return (await cmd.ExecuteReaderAsync()).HasRows;
            }
        }

        #endregion

        #region InsertDBMethods

        private async Task InsertEnvironmentMapping(IList<Planet> planets)
        {
            using (SqliteTransaction t = this._sqliteConnection.BeginTransaction())
            {
                foreach (Planet p in planets)
                {
                    if (p.Environments == null || p.Environments.Length == 0)
                    {
                        continue;
                    }

                    int planetId = 0;

                    using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT id FROM planets WHERE `index` = @i;";
                        cmd.Parameters.AddWithValue("@i", p.Index);

                        planetId = int.Parse((await cmd.ExecuteScalarAsync()).ToString());
                    }

                    foreach (Models.Environment e in p.Environments)
                    {
                        int envId = 0;

                        using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                        {
                            cmd.CommandText = "SELECT id FROM environments WHERE name = @n;";
                            cmd.Parameters.AddWithValue("@n", e.Name);

                            envId = int.Parse((await cmd.ExecuteScalarAsync()).ToString());
                        }

                        using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                        {
                            cmd.CommandText = "INSERT INTO environmentsMapping (planet, env) VALUES (@p, @e);";
                            cmd.Parameters.AddWithValue("@p", planetId);
                            cmd.Parameters.AddWithValue("@e", envId);

                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }

                await t.CommitAsync();
            }
        }

        private async Task InsertPlanets(IList<Planet> planets)
        {
            using (SqliteTransaction t = this._sqliteConnection.BeginTransaction())
            {
                foreach (Planet p in planets)
                {
                    int biomeId = 0;
                    int sectorId = 0;

                    if (p.Biome != null)
                    {
                        using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                        {
                            cmd.CommandText = "SELECT id FROM biomes WHERE slug = @slug;";
                            cmd.Parameters.AddWithValue("@slug", p.Biome.Slug);
                            biomeId = int.Parse((await cmd.ExecuteScalarAsync()).ToString());
                        }
                    }

                    if (p.Sector != null)
                    {
                        using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                        {
                            cmd.CommandText = "SELECT id FROM sectors WHERE name = @name;";
                            cmd.Parameters.AddWithValue("@name", p.Sector);
                            sectorId = int.Parse((await cmd.ExecuteScalarAsync()).ToString());
                        }
                    }

                    using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO planets (`index`, name, sector, biome) VALUES (@i, @n, @s, @b);";
                        cmd.Parameters.AddWithValue("@i", p.Index);
                        cmd.Parameters.AddWithValue("@n", p.Name);
                        cmd.Parameters.AddWithValue("@s", sectorId);
                        cmd.Parameters.AddWithValue("@b", biomeId == default ? DBNull.Value : biomeId);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                await t.CommitAsync();
            }
        }

        private async Task InsertBiomes(IReadOnlySet<Biome> biomes)
        {
            using (SqliteTransaction t = this._sqliteConnection.BeginTransaction())
            {
                foreach (Biome b in biomes)
                {
                    using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO biomes (slug, description) VALUES (@slug, @description);";
                        cmd.Parameters.AddWithValue("@slug", b.Slug);
                        cmd.Parameters.AddWithValue("@description", b.Description);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                await t.CommitAsync();
            }
        }

        private async Task InsertSectors(IReadOnlySet<string> sectors)
        {
            using (SqliteTransaction t = this._sqliteConnection.BeginTransaction())
            {
                foreach (string s in sectors)
                {
                    using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO sectors (name) VALUES (@name);";
                        cmd.Parameters.AddWithValue("@name", s);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                await t.CommitAsync();
            }
        }

        private async Task InsertEnvironments(IReadOnlySet<Models.Environment> environments)
        {
            using (SqliteTransaction t = this._sqliteConnection.BeginTransaction())
            {
                foreach (Models.Environment e in environments)
                {
                    using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO environments (name, description) VALUES (@name, @description);";
                        cmd.Parameters.AddWithValue("@name", e.Name);
                        cmd.Parameters.AddWithValue("@description", e.Description);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                await t.CommitAsync();
            }
        }

        #endregion

        #region Custom Methods
        private static IEnumerable<Biome> ParseUniqueBiome(IEnumerable<Planet> planets)
        {
            foreach (Biome b in planets.Where(x => x.Biome != null).Select(x => x.Biome).GroupBy(x => x).Select(x => x.Key))
            {
                yield return b;
            }
        }

        private static IEnumerable<Models.Environment> ParseUniqueEnviroments(IEnumerable<Planet> planets)
        {
            foreach (IGrouping<Models.Environment, Models.Environment> e in planets.Where(x => x.Environments != null && x.Environments.Length != 0).SelectMany(x => x.Environments).GroupBy(x => x))
            {
                yield return e.Key;
            }
        }

        #endregion

        #region Json Methods
        private static IEnumerable<Planet> DeserializeJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                yield break;
            }

            JObject l = JObject.Parse(json);

            foreach (KeyValuePair<string, JToken> p in l)
            {
                yield return new()
                {
                    Index = int.Parse(p.Key),
                    Name = p.Value["name"].ToString(),
                    Sector = p.Value["sector"].ToString(),
                    Biome = p.Value["biome"].HasValues ? new()
                    {
                        Slug = $"{p.Value["biome"]["slug"].ToString()[..1].ToUpper()}{p.Value["biome"]["slug"].ToString()[1..]}",
                        Description = p.Value["biome"]["description"].ToString()
                    } : null,
                    Environments = p.Value["environmentals"].HasValues ? JArray.Parse(p.Value["environmentals"].ToString()).ToObject<Models.Environment[]>() : null
                };
            }
        }

        private async Task<bool> DownloadJson()
        {
            using (HttpClient client = new())
            {
                HttpResponseMessage response = await client.GetAsync("https://helldiverstrainingmanual.com/api/v1/planets");

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    return false;
                }

                this._downloadedJson = await response.Content.ReadAsStringAsync();
            }

            return true;
        }

        #endregion

        public async Task DownloadAndCreateDatabase()
        {
            this.Started?.Invoke(this, System.EventArgs.Empty);

            Stopwatch sw = Stopwatch.StartNew();

            if (!await this.DownloadJson())
            {
                this._logger?.LogError("Failed to download JSON from API");
                return;
            }

            this.Planets = DeserializeJson(this._downloadedJson).ToList();
            this._logger?.LogInformation("Planets received: {count}", this.Planets.Count);

            if (this.Planets == null || this.Planets.Count == 0)
            {
                this._logger?.LogError("Failed to deserialize JSON");
                return ;
            }

            HashSet<Models.Environment> environments = ParseUniqueEnviroments(this.Planets).ToHashSet();
            this._logger?.LogInformation("Unique environments: {count}", environments.Count);

            HashSet<Biome> biomes = ParseUniqueBiome(this.Planets).ToHashSet();
            this._logger?.LogInformation("Unique biomes: {count}", biomes.Count);

            HashSet<string> sectors = this.Planets.Where(x => x.Sector != null).GroupBy(x => x.Sector).Select(x => x.Key).ToHashSet();
            this._logger?.LogInformation("Unique sectors: {count}", sectors.Count);

            if (!await this.OpenDatabaseConnection())
            {
                this._logger?.LogError("Failed to open database connection");
                return;
            }

            if (!await this.CreateDatabase())
            {
                this._logger?.LogError("Failed to create database");
                return;
            }

            await this.InsertEnvironments(environments);
            await this.InsertBiomes(biomes);
            await this.InsertSectors(sectors);

            await this.InsertPlanets(this.Planets);

            await this.InsertEnvironmentMapping(this.Planets);

            await this.CloseDatabaseConnection();

            sw.Stop();

            this.Completed?.Invoke(this, new(sw.Elapsed));
        }

        #region Dispose
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    this._sqliteConnection?.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
