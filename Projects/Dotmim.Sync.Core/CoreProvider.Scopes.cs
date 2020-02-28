﻿using Dotmim.Sync.Enumerations;
using Dotmim.Sync.Messages;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dotmim.Sync
{
    public abstract partial class CoreProvider
    {

        /// <summary>
        /// Ensure the scope is created on the local provider.
        /// The scope contains all about last sync, schema and scope and local / remote timestamp 
        /// </summary>
        public virtual async Task<SyncContext> EnsureClientScopeAsync(SyncContext context, string scopeInfoTableName, string scopeName,
                             DbConnection connection, DbTransaction transaction,
                             CancellationToken cancellationToken, IProgress<ProgressArgs> progress = null)
        {

            var scopeBuilder = this.GetScopeBuilder();
            var scopeInfoBuilder = scopeBuilder.CreateScopeInfoBuilder(scopeInfoTableName, connection, transaction);

            var needToCreateScopeInfoTable = scopeInfoBuilder.NeedToCreateClientScopeInfoTable();

            // create the scope info table if needed
            if (needToCreateScopeInfoTable)
                scopeInfoBuilder.CreateClientScopeInfoTable();
       
            return context;
        }

        /// <summary>
        /// Ensure the scope history is created on the remote provider.
        /// Contains all history for each client scope
        /// </summary>
        public virtual async Task<SyncContext> EnsureServerHistoryScopeAsync(SyncContext context, string scopeInfoTableName, string scopeName,
                             DbConnection connection, DbTransaction transaction,
                             CancellationToken cancellationToken, IProgress<ProgressArgs> progress = null)
        {

            var scopeBuilder = this.GetScopeBuilder();
            var scopeInfoBuilder = scopeBuilder.CreateScopeInfoBuilder(scopeInfoTableName, connection, transaction);

            var need = scopeInfoBuilder.NeedToCreateServerHistoryScopeInfoTable();

            // create the scope info table if needed
            if (need)
                scopeInfoBuilder.CreateServerHistoryScopeInfoTable();

            return context;
        }


        /// <summary>
        /// Ensure the scope is created on the remote provider.
        /// The scope contains schema and last clean metadatas
        /// </summary>
        public virtual async Task<SyncContext> EnsureServerScopeAsync(SyncContext context, string scopeInfoTableName, string scopeName,
                             DbConnection connection, DbTransaction transaction,
                             CancellationToken cancellationToken, IProgress<ProgressArgs> progress = null)
        {

            var scopeBuilder = this.GetScopeBuilder();
            var scopeInfoBuilder = scopeBuilder.CreateScopeInfoBuilder(scopeInfoTableName, connection, transaction);

            var needToCreateScopeInfoTable = scopeInfoBuilder.NeedToCreateServerScopeInfoTable();

            // create the scope info table if needed
            if (needToCreateScopeInfoTable)
                scopeInfoBuilder.CreateServerScopeInfoTable();

            return context;

        }

        /// <summary>
        /// Get Client scope information
        /// </summary>
        public virtual async Task<(SyncContext, ScopeInfo)> GetClientScopeAsync(SyncContext context, string scopeInfoTableName, string scopeName,
                             DbConnection connection, DbTransaction transaction,
                             CancellationToken cancellationToken, IProgress<ProgressArgs> progress = null)
        {

            var scopes = new List<ScopeInfo>();

            var scopeBuilder = this.GetScopeBuilder();
            var scopeInfoBuilder = scopeBuilder.CreateScopeInfoBuilder(scopeInfoTableName, connection, transaction);

            // get all scopes shared by all (identified by scopeName)
            scopes = scopeInfoBuilder.GetAllClientScopes(scopeName);

            // If no scope found, create it on the local provider
            if (scopes == null || scopes.Count <= 0)
            {
                scopes = new List<ScopeInfo>();

                // create a new scope id for the current owner (could be server or client as well)
                var scope = new ScopeInfo
                {
                    Id = Guid.NewGuid(),
                    Name = scopeName,
                    IsNewScope = true,
                    LastSync = null,
                };

                scope = scopeInfoBuilder.InsertOrUpdateClientScopeInfo(scope);
                scopes.Add(scope);
            }
            else
            {
                //check if we have alread a good last sync. if no, treat it as new
                scopes.ForEach(sc => sc.IsNewScope = sc.LastSync == null);
            }

            // get first scope
            var localScope = scopes.FirstOrDefault();

            // Progress & Interceptor
            context.SyncStage = SyncStage.ScopeLoading;
            var scopeArgs = new ScopeArgs(context, localScope, connection, transaction);
            this.ReportProgress(context, progress, scopeArgs);
            await this.InterceptAsync(scopeArgs).ConfigureAwait(false);

            return (context, localScope);

        }



        /// <summary>
        /// Get Client scope information
        /// </summary>
        public virtual async Task<(SyncContext, ServerScopeInfo)> GetServerScopeAsync(SyncContext context, string scopeInfoTableName, string scopeName,
                             DbConnection connection, DbTransaction transaction,
                             CancellationToken cancellationToken, IProgress<ProgressArgs> progress = null)
        {

            var scopeBuilder = this.GetScopeBuilder();
            var scopeInfoBuilder = scopeBuilder.CreateScopeInfoBuilder(scopeInfoTableName, connection, transaction);

            // get all scopes shared by all (identified by scopeName)
            var serverScopes = scopeInfoBuilder.GetAllServerScopes(scopeName);

            // If no scope found, create it on the local provider
            if (serverScopes == null || serverScopes.Count <= 0)
            {
                serverScopes = new List<ServerScopeInfo>();

                // create a new scope id for the current owner (could be server or client as well)
                var scope = new ServerScopeInfo { Name = scopeName  };

                scope = scopeInfoBuilder.InsertOrUpdateServerScopeInfo(scope);
                serverScopes.Add(scope);
            }
           

            // get first scope
            var localScope = serverScopes.FirstOrDefault();

            // Progress & Interceptor
            context.SyncStage = SyncStage.ScopeLoading;
            var scopeArgs = new ServerScopeArgs(context, localScope, connection, transaction);
            this.ReportProgress(context, progress, scopeArgs);
            await this.InterceptAsync(scopeArgs).ConfigureAwait(false);

            return (context, localScope);

        }



        /// <summary>
        /// Write scope in the local data source
        /// </summary>
        public virtual async Task<SyncContext> WriteClientScopeAsync(SyncContext context, string scopeInfoTableName, ScopeInfo scope,
                             DbConnection connection, DbTransaction transaction,
                             CancellationToken cancellationToken, IProgress<ProgressArgs> progress = null)
        {

            var scopeBuilder = this.GetScopeBuilder();
            var scopeInfoBuilder = scopeBuilder.CreateScopeInfoBuilder(scopeInfoTableName, connection, transaction);

            scopeInfoBuilder.InsertOrUpdateClientScopeInfo(scope);

            // Progress & Interceptor
            context.SyncStage = SyncStage.ScopeSaved;
            var scopeArgs = new ScopeArgs(context, scope, connection, transaction);
            this.ReportProgress(context, progress, scopeArgs);
            await this.InterceptAsync(scopeArgs).ConfigureAwait(false);

            return context;

        }

        /// <summary>
        /// Write scope in the remote data source
        /// </summary>
        public virtual async Task<SyncContext> WriteServerScopeAsync(SyncContext context, string scopeInfoTableName, ServerScopeInfo scope,
                             DbConnection connection, DbTransaction transaction,
                             CancellationToken cancellationToken, IProgress<ProgressArgs> progress = null)
        {

            var scopeBuilder = this.GetScopeBuilder();
            var scopeInfoBuilder = scopeBuilder.CreateScopeInfoBuilder(scopeInfoTableName, connection, transaction);

            scopeInfoBuilder.InsertOrUpdateServerScopeInfo(scope);

            // Progress & Interceptor
            context.SyncStage = SyncStage.ScopeSaved;
            var scopeArgs = new ServerScopeArgs(context, scope, connection, transaction);
            this.ReportProgress(context, progress, scopeArgs);
            await this.InterceptAsync(scopeArgs).ConfigureAwait(false);

            return context;

        }

        /// <summary>
        /// Write scope history in the remote data source
        /// </summary>
        public virtual async Task<SyncContext> WriteServerHistoryScopeAsync(SyncContext context, string scopeInfoTableName, ServerHistoryScopeInfo scope,
                             DbConnection connection, DbTransaction transaction,
                             CancellationToken cancellationToken, IProgress<ProgressArgs> progress = null)
        {

            var scopeBuilder = this.GetScopeBuilder();
            var scopeInfoBuilder = scopeBuilder.CreateScopeInfoBuilder(scopeInfoTableName, connection, transaction);

            scopeInfoBuilder.InsertOrUpdateServerHistoryScopeInfo(scope);

            // Progress & Interceptor
            context.SyncStage = SyncStage.ScopeSaved;
            var scopeArgs = new ServerHistoryScopeArgs(context, scope, connection, transaction);
            this.ReportProgress(context, progress, scopeArgs);
            await this.InterceptAsync(scopeArgs).ConfigureAwait(false);

            return context;

        }

    }
}
