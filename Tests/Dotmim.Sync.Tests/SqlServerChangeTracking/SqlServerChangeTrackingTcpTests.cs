﻿
using Dotmim.Sync.MySql;
using Dotmim.Sync.Sqlite;
using Dotmim.Sync.SqlServer;
using Dotmim.Sync.Tests.Core;
using Dotmim.Sync.Tests.Models;
using Dotmim.Sync.Web.Server;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Dotmim.Sync.Tests
{
    public class SqlServerChangeTrackingTcpTests : TcpTests
    {
        public SqlServerChangeTrackingTcpTests(HelperProvider fixture, ITestOutputHelper output) : base(fixture, output)
        {
        }

        public override string[] Tables => new string[]
        {
            "SalesLT.ProductCategory", "SalesLT.ProductModel", "SalesLT.Product", "Employee", "Customer", "Address", "CustomerAddress", "EmployeeAddress",
            "SalesLT.SalesOrderHeader", "SalesLT.SalesOrderDetail", "dbo.Sql", "Posts", "Tags", "PostTag",
            "PricesList", "PricesListCategory", "PricesListDetail"
        };

        public override List<ProviderType> ClientsType => new List<ProviderType>
            {  ProviderType.Sql, ProviderType.Sql, ProviderType.MySql, ProviderType.Sqlite};

        public override ProviderType ServerType =>
            ProviderType.Sql;


        public override RemoteOrchestrator CreateRemoteOrchestrator(ProviderType providerType, string dbName)
        {
            var cs = HelperDatabase.GetConnectionString(ProviderType.Sql, dbName);
            var orchestrator = new RemoteOrchestrator(new SqlSyncChangeTrackingProvider(cs));

            return orchestrator;
        }

        public override LocalOrchestrator CreateLocalOrchestrator(ProviderType providerType, string dbName)
        {
            var cs = HelperDatabase.GetConnectionString(providerType, dbName);
            var orchestrator = new LocalOrchestrator();

            switch (providerType)
            {
                case ProviderType.Sql:
                    orchestrator.Provider = new SqlSyncProvider(cs);
                    break;
                case ProviderType.MySql:
                    orchestrator.Provider = new MySqlSyncProvider(cs);
                    break;
                case ProviderType.Sqlite:
                    orchestrator.Provider = new SqliteSyncProvider(cs);
                    break;
            }

            return orchestrator;
        }

        public T CreateOrchestrator<T>(ProviderType providerType, string dbName, bool useChangeTracking = false) where T : IOrchestrator
        {
            // Get connection string
            var cs = HelperDatabase.GetConnectionString(providerType, dbName);

            IOrchestrator orchestrator = null;

            if (typeof(T) == typeof(RemoteOrchestrator))
                orchestrator = new RemoteOrchestrator();
            else if (typeof(T) == typeof(LocalOrchestrator))
                orchestrator = new LocalOrchestrator();
            else if (typeof(T) == typeof(WebServerOrchestrator))
                orchestrator = new WebServerOrchestrator();

            if (orchestrator == null)
                throw new Exception("Orchestrator does not exists");

            switch (providerType)
            {
                case ProviderType.Sql:
                    orchestrator.Provider = useChangeTracking ? new SqlSyncChangeTrackingProvider(cs) : new SqlSyncProvider(cs);
                    break;
                case ProviderType.MySql:
                    orchestrator.Provider = new MySqlSyncProvider(cs);
                    break;
                case ProviderType.Sqlite:
                    orchestrator.Provider = new SqliteSyncProvider(cs);
                    break;
            }
            return (T)orchestrator;
        }





        public override async Task EnsureDatabaseSchemaAndSeedAsync((string DatabaseName, ProviderType ProviderType, IOrchestrator Orchestrator) t, bool useSeeding = false, bool useFallbackSchema = false)
        {
            AdventureWorksContext ctx = null;
            try
            {
                ctx = new AdventureWorksContext(t, useFallbackSchema, useSeeding);
                await ctx.Database.EnsureCreatedAsync();

                if (t.ProviderType == ProviderType.Sql)
                    await this.ActivateChangeTracking(t.DatabaseName);
            }
            catch (Exception)
            {
            }
            finally
            {
                if (ctx != null)
                    ctx.Dispose();
            }
        }

        public override async Task CreateDatabaseAsync(ProviderType providerType, string dbName, bool recreateDb = true)
        {
            await HelperDatabase.CreateDatabaseAsync(providerType, dbName, recreateDb);

            if (providerType == ProviderType.Sql)
                await this.ActivateChangeTracking(dbName);
        }


        private async Task ActivateChangeTracking(string dbName)
        {

            var script = $"ALTER DATABASE {dbName} SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON)";
            using (var masterConnection = new SqlConnection(Setup.GetSqlDatabaseConnectionString("master")))
            {
                masterConnection.Open();

                using (var cmdCT = new SqlCommand(script, masterConnection))
                    await cmdCT.ExecuteNonQueryAsync();

                masterConnection.Close();
            }
        }

        /// <summary>
        /// Get the server database rows count
        /// </summary>
        /// <returns></returns>
        public override int GetServerDatabaseRowsCount((string DatabaseName, ProviderType ProviderType, IOrchestrator Orchestrator) t)
        {
            int totalCountRows = 0;

            using (var serverDbCtx = new AdventureWorksContext(t))
            {
                totalCountRows += serverDbCtx.Address.Count();
                totalCountRows += serverDbCtx.Customer.Count();
                totalCountRows += serverDbCtx.CustomerAddress.Count();
                totalCountRows += serverDbCtx.Employee.Count();
                totalCountRows += serverDbCtx.EmployeeAddress.Count();
                totalCountRows += serverDbCtx.Log.Count();
                totalCountRows += serverDbCtx.Posts.Count();
                totalCountRows += serverDbCtx.PostTag.Count();
                totalCountRows += serverDbCtx.PricesList.Count();
                totalCountRows += serverDbCtx.PricesListCategory.Count();
                totalCountRows += serverDbCtx.PricesListDetail.Count();
                totalCountRows += serverDbCtx.Product.Count();
                totalCountRows += serverDbCtx.ProductCategory.Count();
                totalCountRows += serverDbCtx.ProductModel.Count();
                totalCountRows += serverDbCtx.SalesOrderDetail.Count();
                totalCountRows += serverDbCtx.SalesOrderHeader.Count();
                totalCountRows += serverDbCtx.Sql.Count();
                totalCountRows += serverDbCtx.Tags.Count();
            }

            return totalCountRows;
        }


    }
}
