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
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Hd2Planets.Logic
{
    internal class SqliteDatabase : IDisposable
    {
        #region Members

        private bool _disposedValue;
        internal SqliteConnection _sqliteConnection;
        private readonly string _planetsAPIAdress = "https://helldiverstrainingmanual.com/api/v1/planets";
        private readonly string _campignsAPIAdress = "https://helldiverstrainingmanual.com/api/v1/war/campaign#downloadJSON=true";

        internal string _downloadedPlanetsJson;
        internal string _downloadedCampaignsJson;
        internal ILogger _logger;
        #endregion

        #region Properties

        public string DatabasePath { get; init; }
        public List<Planet> Planets { get; private set; }
        public List<Campaign> Campaigns { get; private set; }

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
        private async Task<bool> OpenDBConnection()
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

        private async Task CloseDBConnection()
        {
            if (this._sqliteConnection == null)
            {
                return;
            }

            await this._sqliteConnection.CloseAsync();
            this._sqliteConnection.Dispose();
            this._sqliteConnection = null;
        }

        private async Task<bool> CreateDBTabels()
        {
            bool[] tablesCreated = new bool[6];

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

                using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                {
                    cmd.CommandText = @"CREATE TABLE campaigns (
                                                id INTEGER PRIMARY KEY,
                                                faction TEXT,
                                                players INTEGER,
                                                health REAL,
                                                maxHealth REAL,
                                                percentage REAL,
                                                defense BOOLEAN,
                                                majorOrder BOOLEAN,
                                                expireDateTime INTEGER,
                                                planetId INTEGER,
                                                FOREIGN KEY(planetId) REFERENCES planets(id));";
                    await cmd.ExecuteNonQueryAsync();
                }
                await t.CommitAsync();

                tablesCreated[0] = await DoesDBTableExist("environments");
                tablesCreated[1] = await DoesDBTableExist("biomes");
                tablesCreated[2] = await DoesDBTableExist("planets");
                tablesCreated[3] = await DoesDBTableExist("environmentsMapping");
                tablesCreated[4] = await DoesDBTableExist("sectors");
                tablesCreated[5] = await DoesDBTableExist("campaigns");
            }

            return tablesCreated.All(x => x);
        }

        private async Task<bool> DoesDBTableExist(string tablename)
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


        private async Task InsertCampaigns(IList<Campaign> campaigns)
        {
            using (SqliteTransaction t = this._sqliteConnection.BeginTransaction())
            {
                foreach (Campaign c in campaigns)
                {
                    int planetId = 0;

                    if (c.CampaignPlanet != null)
                    {
                        using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                        {
                            cmd.CommandText = "SELECT id FROM planets WHERE `index` = @planetindex;";
                            cmd.Parameters.AddWithValue("@planetindex", c.CampaignPlanet.Index);
                            planetId = int.Parse((await cmd.ExecuteScalarAsync()).ToString());
                        }
                    }

                    using (SqliteCommand cmd = this._sqliteConnection.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO campaigns (faction, players, health, maxHealth, percentage, defense, majorOrder, expireDateTime, planetId) VALUES (@faction, @players, @health, @maxHealth, @percentage, @defense, @majorOrder, @expireDateTime, @planetId);";

                        cmd.Parameters.AddWithValue("@faction", c.Faction);
                        cmd.Parameters.AddWithValue("@players", c.Players);
                        cmd.Parameters.AddWithValue("@health", c.Health);
                        cmd.Parameters.AddWithValue("@maxHealth", c.MaxHealth);
                        cmd.Parameters.AddWithValue("@percentage", (double)c.Percentage);
                        cmd.Parameters.AddWithValue("@defense", c.Defense ? 1 : 0); 
                        cmd.Parameters.AddWithValue("@majorOrder", c.MajorOrder ? 1 : 0);
                        cmd.Parameters.AddWithValue("@expireDateTime", c.ExpireDateTimeEpochFormat);
                        cmd.Parameters.AddWithValue("@planetId", planetId == default ? DBNull.Value : planetId);

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
        private static IEnumerable<Planet> DeserializePlanetsJson(string json)
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

        private static IEnumerable<Campaign> DeserializeCampaignsJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                yield break;
            }

            JArray l = JArray.Parse(json);

            foreach ( JToken p in l)
            {
                yield return new Campaign()
                {
                    CampaignPlanet = new() {
                        Index = int.Parse(p["planetIndex"].ToString()),
                    },
                    Faction = p["faction"].ToString(),
                    Players = int.Parse(p["players"].ToString()),
                    Health = int.Parse(p["health"].ToString()),
                    MaxHealth = int.Parse(p["maxHealth"].ToString()),
                    Percentage = double.Parse(p["percentage"].ToString()),
                    Defense = bool.Parse(p["defense"].ToString()),
                    MajorOrder = bool.Parse(p["majorOrder"].ToString()),
                    ExpireDateTimeEpochFormat = p["expireDateTime"].HasValues ? long.Parse(p["expireDateTime"].ToString()) : default,
                };
            }
        }

        private async Task<bool> DownloadJson()
        {
            using (HttpClient client = new())
            {
                HttpResponseMessage planetsResponse = await client.GetAsync(_planetsAPIAdress);

                if (planetsResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    return false;
                }

                this._downloadedPlanetsJson = await planetsResponse.Content.ReadAsStringAsync();

                HttpResponseMessage campaignResponse = await client.GetAsync(_campignsAPIAdress);

                if (campaignResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    return false;
                }

                this._downloadedCampaignsJson = await campaignResponse.Content.ReadAsStringAsync();
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
                this._logger?.LogError("Failed to download JSON from APIs");
                return;
            }

            this.Planets = DeserializePlanetsJson(this._downloadedPlanetsJson).ToList();
            this.Campaigns = DeserializeCampaignsJson(this._downloadedCampaignsJson).ToList();

            this._logger?.LogInformation("Planets received: {count}", this.Planets.Count);
            this._logger?.LogInformation("Campaigns received: {count}", this.Campaigns.Count);


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

            if (!await this.OpenDBConnection())
            {
                this._logger?.LogError("Failed to open database connection");
                return;
            }

            if (!await this.CreateDBTabels())
            {
                this._logger?.LogError("Failed to create database");
                return;
            }

            await this.InsertEnvironments(environments);
            await this.InsertBiomes(biomes);
            await this.InsertSectors(sectors);

            await this.InsertPlanets(this.Planets);

            await this.InsertEnvironmentMapping(this.Planets);

            await this.InsertCampaigns(this.Campaigns);

            await this.CloseDBConnection();

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
