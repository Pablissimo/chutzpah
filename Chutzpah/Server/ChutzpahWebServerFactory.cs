﻿using Chutzpah.Exceptions;
using Chutzpah.Models;
using Chutzpah.Server.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Internal.Networking;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Chutzpah.Server
{
    public class ChutzpahWebServerFactory : IChutzpahWebServerFactory
    {
        readonly IFileProbe fileProbe;

        public ChutzpahWebServerFactory(IFileProbe fileProbe)
        {
            this.fileProbe = fileProbe;
        }

        public IChutzpahWebServerHost CreateServer(ChutzpahWebServerConfiguration configuration, IChutzpahWebServerHost activeWebServerHost)
        {
            if (activeWebServerHost != null 
                && activeWebServerHost.IsRunning
                && activeWebServerHost.RootPath.Equals(configuration.RootPath, StringComparison.OrdinalIgnoreCase))
            {
                // If the requested server is already running just re-use it
                return activeWebServerHost;
            }

            var builtInDependencyFolder = fileProbe.BuiltInDependencyDirectory;


            return BuildHost(configuration.RootPath, configuration.DefaultPort.Value, builtInDependencyFolder);
        }

        private ChutzpahWebServerHost BuildHost(string rootPath, int defaultPort, string builtInDependencyFolder)
        {
            var attemptLimit = Constants.WebServerCreationAttemptLimit;
            var success = false;

            do
            {
                // We can try multiple times to build the webserver. The reason is there is a possible race condition where
                // between when we find a free port and when we start the server that port may have been taken. To mitigate this we
                // can retry to hopefully avoid this issue.
                attemptLimit--;
                var port = FindFreePort(defaultPort);

                try
                {
                    ChutzpahTracer.TraceInformation("Creating Web Server Host at path {0} and port {1}", rootPath, port);
                    var host = new WebHostBuilder()
                       .UseUrls($"http://localhost:{port}")
                       .UseContentRoot(rootPath)
                       .UseWebRoot("")
                       .UseKestrel()
                       .Configure((app) =>
                       {
                           var env = (IHostingEnvironment)app.ApplicationServices.GetService(typeof(IHostingEnvironment));
                           app.UseStaticFiles(new StaticFileOptions { FileProvider = new ChutzpahServerFileProvider(env.ContentRootPath, builtInDependencyFolder) });
                           app.Run(async (context) =>
                           {
                               await context.Response.WriteAsync("Chutzpah Web Server");
                           });
                       })
                       .Build();

                    host.Start();
                    success = true;

                    return ChutzpahWebServerHost.Create(host, rootPath, port);
                }
                catch (Exception ex) when ((ex is UvException || ex is IOException) && attemptLimit > 0)
                {
                    ChutzpahTracer.TraceError(ex, "Unable to create web server host at path {0} and port {1}. Trying again...", rootPath, port);
                }
            }
            while (!success && attemptLimit > 0);


            throw new ChutzpahException("Failed to create web server. This should never be hit!");
        }




        public static int FindFreePort(int initialPort)
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                try
                {
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, initialPort));
                }
                catch (SocketException)
                {
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                }

                return ((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

    }
}
