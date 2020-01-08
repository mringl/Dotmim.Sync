﻿using System;
using System.Collections.Generic;
using Dotmim.Sync.Batch;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using Newtonsoft.Json;
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.Serialization;
using Dotmim.Sync.Data.Surrogate;
using Dotmim.Sync.Data;
using Dotmim.Sync.Messages;
using Dotmim.Sync.Web.Client;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Dotmim.Sync.Cache;
using System.IO.Compression;
using System.Text;

namespace Dotmim.Sync.Web.Server
{

    /// <summary>
    /// Class used when you have to deal with a Web Server
    /// </summary>
    public class WebProxyServerOrchestrator
    {
        private static WebProxyServerOrchestrator defaultInstance = new WebProxyServerOrchestrator();

        /// <summary>
        /// Default constructor for DI
        /// </summary>
        public WebProxyServerOrchestrator() { }

        /// <summary>
        /// Create a new WebProxyServerProvider with a first instance of an in memory CoreProvider
        /// Use this method to create your WebProxyServerProvider if you don't use the DI stuff from ASP.NET
        /// </summary>
        public static WebProxyServerOrchestrator Create(HttpContext context, CoreProvider provider, SyncSetup setup, WebServerOptions options = null)
        {
            if (!TryGetHeaderValue(context.Request.Headers, "dotmim-sync-session-id", out var sessionId))
                throw new HttpHeaderMissingExceptiopn("dotmim-sync-session-id");

            // Check if we have already a cached Sync Memory provider
            var syncMemoryOrchestrator = GetCachedOrchestrator(context, sessionId);

            // we don't have any provider for this session id, so create it
            if (syncMemoryOrchestrator == null)
                AddNewOrchestratorToCache(context, provider, setup, sessionId, options);

            return defaultInstance;
        }


        public static WebProxyServerOrchestrator Create(HttpContext context, WebServerOrchestrator provider)
        {
            if (!TryGetHeaderValue(context.Request.Headers, "dotmim-sync-session-id", out var sessionId))
                throw new HttpHeaderMissingExceptiopn("dotmim-sync-session-id");

            // Check if we have already a cached Sync Memory provider
            var syncMemoryOrchestrator = GetCachedOrchestrator(context, sessionId);

            // we don't have any provider for this session id, so create it
            if (syncMemoryOrchestrator == null)
                AddNewWebServerOrchestratorToCache(context, provider, sessionId);

            return defaultInstance;
        }


        /// <summary>
        /// Retrieve from cache the selected provider depending on the session id
        /// </summary>
        public WebServerOrchestrator GetLocalOrchestrator(HttpContext context)
        {
            if (!TryGetHeaderValue(context.Request.Headers, "dotmim-sync-session-id", out var sessionId))
                return null;

            var webServerOrchestrator = GetCachedOrchestrator(context, sessionId);

            if (webServerOrchestrator != null)
                return webServerOrchestrator;

            return null;
        }

        /// <summary>
        /// Call this method to handle requests on the server, sent by the client
        /// </summary>
        public Task HandleRequestAsync(HttpContext context) =>
            HandleRequestAsync(context, null, CancellationToken.None);

        /// <summary>
        /// Call this method to handle requests on the server, sent by the client
        /// </summary>
        public Task HandleRequestAsync(HttpContext context, Action<RemoteOrchestrator> action) =>
            HandleRequestAsync(context, action, CancellationToken.None);

        /// <summary>
        /// Call this method to handle requests on the server, sent by the client
        /// </summary>
        public Task HandleRequestAsync(HttpContext context, CancellationToken token) =>
            HandleRequestAsync(context, null, token);

        /// <summary>
        /// Call this method to handle requests on the server, sent by the client
        /// </summary>
        public async Task HandleRequestAsync(HttpContext context, Action<RemoteOrchestrator> action, CancellationToken cancellationToken)
        {
            var httpRequest = context.Request;
            var httpResponse = context.Response;
            var streamArray = GetBody(httpRequest);
            var serAndsizeString = string.Empty;
            var converter = string.Empty;

            // Get the serialization and batch size format
            if (TryGetHeaderValue(context.Request.Headers, "dotmim-sync-serialization-format", out var vs))
                serAndsizeString = vs.ToLowerInvariant();

            // Get the serialization and batch size format
            if (TryGetHeaderValue(context.Request.Headers, "dotmim-sync-converter", out var cs))
                converter = cs.ToLowerInvariant();

            if (!TryGetHeaderValue(context.Request.Headers, "dotmim-sync-session-id", out var sessionId))
                throw new HttpHeaderMissingExceptiopn("dotmim-sync-session-id");

            if (!TryGetHeaderValue(context.Request.Headers, "dotmim-sync-step", out string iStep))
                throw new HttpHeaderMissingExceptiopn("dotmim-sync-step");

            var step = (HttpStep)Convert.ToInt32(iStep);
            WebServerOrchestrator remoteOrchestrator = null;

            try
            {
                var cache = context.RequestServices.GetService<IMemoryCache>();

                // get cached provider instance if not defined byt web proxy server provider
                if (remoteOrchestrator == null)
                    remoteOrchestrator = GetCachedOrchestrator(context, sessionId);

                if (remoteOrchestrator == null)
                    remoteOrchestrator = AddNewOrchestratorToCacheFromDI(context, sessionId);

                // action from user if available
                action?.Invoke(remoteOrchestrator);

                // Get the serializer and batchsize
                (var clientBatchSize, var clientSerializerFactory) = GetClientSerializer(serAndsizeString, remoteOrchestrator);

                // Get converter used by client
                // Can be null
                var clientConverter = GetClientConverter(converter, remoteOrchestrator);
                remoteOrchestrator.ClientConverter = clientConverter;

                byte[] binaryData = null;
                switch (step)
                {
                    case HttpStep.EnsureScopes:
                        var m1 = clientSerializerFactory.GetSerializer<HttpMessageEnsureScopesRequest>().Deserialize(streamArray);
                        var s1 = await remoteOrchestrator.EnsureScopeAsync(m1, cancellationToken).ConfigureAwait(false);
                        binaryData = clientSerializerFactory.GetSerializer<HttpMessageEnsureScopesResponse>().Serialize(s1);
                        break;
                    case HttpStep.SendChanges:
                        var m2 = clientSerializerFactory.GetSerializer<HttpMessageSendChangesRequest>().Deserialize(streamArray);
                        var s2 = await remoteOrchestrator.ApplyThenGetChangesAsync(m2, clientBatchSize, cancellationToken).ConfigureAwait(false);
                        binaryData = clientSerializerFactory.GetSerializer<HttpMessageSendChangesResponse>().Serialize(s2);
                        break;
                    case HttpStep.GetChanges:
                        var m3 = clientSerializerFactory.GetSerializer<HttpMessageGetMoreChangesRequest>().Deserialize(streamArray);
                        var s3 = remoteOrchestrator.GetMoreChanges(m3, cancellationToken);
                        binaryData = clientSerializerFactory.GetSerializer<HttpMessageSendChangesResponse>().Serialize(s3);
                        break;
                }

                // Save orchestrator again
                cache.Set(sessionId, remoteOrchestrator, TimeSpan.FromHours(1));

                // Adding the serialization format used and session id
                httpResponse.Headers.Add("dotmim-sync-session-id", sessionId.ToString());
                httpResponse.Headers.Add("dotmim-sync-serialization-format", clientSerializerFactory.Key);

                // data to send back, as the response
                byte[] data;

                // Compress data if client accept Gzip / Deflate
                string encoding = httpRequest.Headers["Accept-Encoding"];

                if (!string.IsNullOrEmpty(encoding) && (encoding.Contains("gzip") || encoding.Contains("deflate")))
                {
                    using (var writeSteam = new MemoryStream())
                    {
                        using (var compress = new GZipStream(writeSteam, CompressionMode.Compress))
                        {
                            compress.Write(binaryData, 0, binaryData.Length);
                        }

                        data = writeSteam.ToArray();
                    }

                    httpResponse.Headers.Add("Content-Encoding", "gzip");
                }
                else
                {
                    data = binaryData;
                }


                await GetBody(httpResponse).WriteAsync(data, 0, data.Length).ConfigureAwait(false);

            }
            catch (Exception ex)
            {
                await WriteExceptionAsync(httpResponse, ex);
            }
            finally
            {
                //if (httpMessage != null && httpMessage.Step == HttpStep.EndSession)
                //    Cleanup(context.RequestServices.GetService(typeof(IMemoryCache)), syncSessionId);
            }
        }

        /// <summary>
        /// Get an instance of SyncMemoryProvider depending on session id. If the entry for session id does not exists, create a new one
        /// </summary>
        private static WebServerOrchestrator GetCachedOrchestrator(HttpContext context, string syncSessionId)
        {
            WebServerOrchestrator remoteOrchestrator;

            var cache = context.RequestServices.GetService<IMemoryCache>();
            if (cache == null)
                throw new HttpCacheNotConfiguredException();

            if (string.IsNullOrWhiteSpace(syncSessionId))
                throw new ArgumentNullException(nameof(syncSessionId));

            // get the sync provider associated with the session id
            remoteOrchestrator = cache.Get(syncSessionId) as WebServerOrchestrator;

            return remoteOrchestrator;
        }

        /// <summary>
        /// Add a new instance of SyncMemoryProvider, created by DI
        /// </summary>
        /// <returns></returns>
        private static WebServerOrchestrator AddNewOrchestratorToCacheFromDI(HttpContext context, string syncSessionId)
        {
            var cache = context.RequestServices.GetService<IMemoryCache>();

            if (cache == null)
                throw new HttpCacheNotConfiguredException();

            var remoteOrchestrator = DependencyInjection.GetNewOrchestrator();
            cache.Set(syncSessionId, remoteOrchestrator, TimeSpan.FromHours(1));

            return remoteOrchestrator;
        }


        private static WebServerOrchestrator AddNewOrchestratorToCache(HttpContext context, CoreProvider provider, SyncSetup setup, string sessionId, WebServerOptions options = null)
        {
            WebServerOrchestrator remoteOrchestrator;
            var cache = context.RequestServices.GetService<IMemoryCache>();

            if (cache == null)
                throw new HttpCacheNotConfiguredException();

            remoteOrchestrator = new WebServerOrchestrator(provider, options, setup);

            cache.Set(sessionId, remoteOrchestrator, TimeSpan.FromHours(1));
            return remoteOrchestrator;
        }
        private static WebServerOrchestrator AddNewWebServerOrchestratorToCache(HttpContext context, WebServerOrchestrator webServerOrchestrator, string sessionId)
        {
            var cache = context.RequestServices.GetService<IMemoryCache>();

            if (cache == null)
                throw new HttpCacheNotConfiguredException();

            cache.Set(sessionId, webServerOrchestrator, TimeSpan.FromHours(1));
            return webServerOrchestrator;
        }



        private (int clientBatchSize, ISerializerFactory clientSerializer) GetClientSerializer(string serAndsizeString, WebServerOrchestrator serverOrchestrator)
        {
            try
            {
                if (string.IsNullOrEmpty(serAndsizeString))
                    throw new Exception();

                var serAndsize = JsonConvert.DeserializeAnonymousType(serAndsizeString, new { f = "", s = 0 });

                var clientBatchSize = serAndsize.s;
                var clientSerializerFactory = serverOrchestrator.Options.Serializers[serAndsize.f];

                if (clientSerializerFactory == null)
                    throw new ArgumentNullException();

                return (clientBatchSize, clientSerializerFactory);
            }
            catch
            {
                throw new HttpSerializerNotConfiguredException(serverOrchestrator.Options.Serializers.Select(sf => sf.Key));
            }
        }

        private IConverter GetClientConverter(string cs, WebServerOrchestrator serverOrchestrator)
        {
            try
            {
                if (string.IsNullOrEmpty(cs))
                    return null;

                var clientConverter = serverOrchestrator.Options.Converters.First(c => c.Key == cs);

                return clientConverter;
            }
            catch
            {
                throw new HttpConverterNotConfiguredException(serverOrchestrator.Options.Converters.Select(sf => sf.Key));
            }
        }



        /// <summary>
        /// Clean up memory cache object for specified session id
        /// </summary>
        private static void Cleanup(object memoryCache, string syncSessionId)
        {
            if (memoryCache == null || string.IsNullOrWhiteSpace(syncSessionId)) return;
            Task.Run(() =>
            {
                try
                {
                    (memoryCache as IMemoryCache)?.Remove(syncSessionId);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            });
        }

        /// <summary>
        /// Write exception to output message
        /// </summary>
        public async Task WriteExceptionAsync(HttpResponse httpResponse, Exception ex)
        {
            // Check if it's an unknown error, not managed (yet)
            if (!(ex is SyncException syncException))
                syncException = new SyncException(ex);


            var webException = new WebSyncException
            {
                Message = syncException.Message,
                SyncStage = syncException.SyncStage,
                TypeName = syncException.TypeName,
                DataSource = syncException.DataSource,
                InitialCatalog = syncException.InitialCatalog,
                Number = syncException.Number,
                Side = syncException.Side
            };

            var webXMessage = JsonConvert.SerializeObject(webException);

            httpResponse.StatusCode = StatusCodes.Status400BadRequest;
            httpResponse.ContentLength = webXMessage.Length;
            await httpResponse.WriteAsync(webXMessage);
        }


        public static bool TryGetHeaderValue(IHeaderDictionary n, string key, out string header)
        {
            if (n.TryGetValue(key, out var vs))
            {
                header = vs[0];
                return true;
            }

            header = null;
            return false;
        }

        public Stream GetBody(HttpRequest r) => r.Body;
        public Stream GetBody(HttpResponse r) => r.Body;

    }

}
