﻿using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System;
using System.Data.SqlClient;
using System.Threading;

namespace Neptune.Services.Databases
{
    public interface IDatabaseCreator
    {
        void Create(string masterConnectionString, string appConnectionString);
    }

    public class DatabaseCreator : IDatabaseCreator
    {
        private readonly ILogger<DatabaseCreator> _log;

        public DatabaseCreator(ILogger<DatabaseCreator> log)
        {
            _log = log;
        }

        public void Create(string masterConnectionString, string appConnectionString)
        {
            Policy
                .Handle<Exception>()
                .WaitAndRetry(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (exception, timeSpan, retryCount) => {
                        _log.LogError(exception, $"Attempt {retryCount}");
                    })
                .Execute(() => Provision(masterConnectionString, appConnectionString));
        }

        private void Provision(string masterConnectionString, string appConnectionString)
        {
            var database = GetDatabaseName(appConnectionString);
            using (_log.BeginScope(("database", database)))
            {
                try
                {
                    _log.LogInformation($"Provisioning database");

                    if (CheckDatabaseExists(masterConnectionString, database))
                    {
                        return;
                    }

                    CreateDatabase(masterConnectionString, database);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error provisioning database");
                    throw;
                }
            }
        }

        private void CreateDatabase(string master, string database)
        {
            _log.LogInformation($"Creating database {database}");

            using (var connection = new SqlConnection(master))
            using (var cmd = new SqlCommand($"CREATE DATABASE {database};", connection))
            {
                connection.Open();
                cmd.ExecuteNonQuery();
            }

            _log.LogInformation($"Created database {database}");
        }

        private bool CheckDatabaseExists(string master, string database)
        {
            _log.LogInformation($"Checking for existance of database {database}");
            using (var connection = new SqlConnection(master))
            using (var command = new SqlCommand($"SELECT db_id('{database}')", connection))
            {
                connection.Open();
                var exists = (command.ExecuteScalar() != DBNull.Value);
                if (exists)
                {
                    _log.LogInformation($"Database {database} exists");
                }
                else
                {
                    _log.LogInformation($"Database {database} does not exist");
                }
                return exists;
            }
        }

        private static string GetDatabaseName(string connection)
        {
            var builder = new SqlConnectionStringBuilder(connection);
            return builder.InitialCatalog;
        }
    }
}
