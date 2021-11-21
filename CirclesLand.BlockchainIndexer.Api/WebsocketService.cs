﻿using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CirclesLand.BlockchainIndexer.Util;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CirclesLand.BlockchainIndexer.Api
{
    public class WebsocketService : IMiddleware
    {
        public static readonly ConcurrentDictionary<int, ConnectedWebsocketClient> Clients = new();

        private static int _socketCounter = 0;
        private static bool _serverIsRunning = true;

        private static CancellationTokenRegistration _appShutdownHandler;
        private readonly IHostApplicationLifetime _hostLifetime;
        private readonly ILogger<WebsocketService> _logger;

        public WebsocketService(IHostApplicationLifetime hostLifetime, ILogger<WebsocketService> logger)
        {
            // gracefully close all websockets during shutdown (only register on first instantiation)
            if (_appShutdownHandler.Token.Equals(CancellationToken.None))
            {
                _appShutdownHandler = hostLifetime.ApplicationStopping.Register(ApplicationShutdownHandler);
            }

            _hostLifetime = hostLifetime;
            _logger = logger;
        }

        public static async Task BroadcastMessage(string messageString)
        {
            foreach (var connectedClient in Clients.Values)
            {
                try
                {
                    await connectedClient.SendMessage(messageString);
                }
                catch (Exception e)
                {
                    Logger.LogError(e.Message);
                    Logger.LogError(e.StackTrace);
                }
            }
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            try
            {
                if (!_serverIsRunning)
                {
                    context.Response.StatusCode = 503;
                    return;
                }

                if (context.WebSockets.IsWebSocketRequest)
                {
                    var socketId = Interlocked.Increment(ref _socketCounter);
                    var socket = await context.WebSockets.AcceptWebSocketAsync();
                    
                    var client = new ConnectedWebsocketClient(
                        socketId,
                        socket);

                    _logger.Log(LogLevel.Information,
                        $"Established websocket connection {socketId} with {context.Connection.RemoteIpAddress}:{context.Connection.RemotePort} ..");
                    
                    if (!Clients.TryAdd(socketId, client))
                    {
                        throw new Exception("Couldn't register the connection at the server.");
                    }

                    try
                    {
                        await Task.Run(async () =>
                        {
                            // Read from the websocket only to keep it alive.
                            var buffer = new byte[2048];
                            while (!context.RequestAborted.IsCancellationRequested)
                            {
                                await socket.ReceiveAsync(buffer, context.RequestAborted);
                                await client.SendMessage("This service doesn't process any incoming messages.");
                            }
                        }, context.RequestAborted);
                    }
                    catch (Exception e)
                    {
                        _logger.Log(LogLevel.Warning,
                            "A websocket connection experienced an error: " + e.Message);
                        _logger.Log(LogLevel.Trace,
                            "A websocket connection experienced an error: " + e.Message + "\n" + e.StackTrace);
                    }
                    finally
                    {
                        _logger.Log(LogLevel.Information,
                            $"Removing websocket connection {socketId}");
                        Clients.TryRemove(socketId, out _);
                    }
                }
                else
                {
                    // Just a info message for http calls
                    if (context.Request.Headers["Accept"][0].Contains("text/html"))
                    {
                        await context.Response.WriteAsync("CirclesLand.TransactionIndexer WS endpoint.");
                    }
                }
            }
            catch (Exception ex)
            {
                // HTTP 500 Internal server error
                context.Response.StatusCode = 500;
                Logger.Log("A connection experienced an error: " + ex.Message);
            }
            finally
            {
                // if this middleware didn't handle the request, pass it on
                if (!context.Response.HasStarted)
                {
                    await next(context);
                }
            }
        }

        // event-handlers are the sole case where async void is valid
        private void ApplicationShutdownHandler()
        {
            _serverIsRunning = false;
        }
    }
}