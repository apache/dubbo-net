/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements. See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License. You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using DubboNet.DubboService;
using DubboNet.DubboService.DataModle;
using System;
using System.Net;
using System.Threading.Tasks;
#if !NETFRAMEWORK
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using System.Threading;
#endif

internal static class BinaryTriplePackageSmoke
{
    internal static async Task Run()
    {
        var options = new DubboActuatorSuiteConf
        { TripleWireMode = TripleWireMode.Hessian2Wrapper, DubboRequestTimeout = 3000 };
        var invocation = new DubboInvocation("Service", "echo", new[] { "java.lang.String" }, new object[] { "hello" });
#if NETFRAMEWORK
        // Standard can be loaded by Framework, but cannot manufacture modern
        // HTTP/2/trailers APIs. Verify its explicit failure before any network send.
        using (var suite = new TripleDubboActuatorSuite("127.0.0.1", 1, options))
        {
            var result = await suite.SendQuery(invocation);
            if (result.QuerySuccess || !result.ErrorMeaasge.Contains("requires"))
                throw new InvalidOperationException("Framework binary Triple capability failure was not explicit.");
        }
#else
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(config => config.Listen(IPAddress.Loopback, 0,
            endpoint => endpoint.Protocols = HttpProtocols.Http2));
        var app = builder.Build();
        app.Run(async context =>
        {
            if (context.Request.Protocol != "HTTP/2" || context.Request.ContentType != "application/grpc+proto" ||
                context.Request.Path.Value != "/Service/$invoke")
                throw new InvalidOperationException("NuGet binary smoke used the wrong protocol.");
            await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
            context.Response.ContentType = "application/grpc+proto";
            context.Response.DeclareTrailer("grpc-status");
            // Independent Apache Java golden payload, not a production encoder.
            byte[] response = Convert.FromHexString("00000000210a086865737369616e341203026f6b1a106a6176612e6c616e672e537472696e67");
            await context.Response.Body.WriteAsync(response, context.RequestAborted);
            context.Response.AppendTrailer("grpc-status", "0");
        });
        await app.StartAsync();
        try
        {
            using var suite = new TripleDubboActuatorSuite("127.0.0.1", new Uri(app.Urls.Single()).Port, options);
            var result = await suite.SendQuery(invocation);
            if (!result.QuerySuccess || result.Result != "\"ok\"" || result.GrpcStatusCode != 0 || result.ResponseHttpVersion.Major != 2)
                throw new InvalidOperationException("NuGet binary Triple round trip failed: " + result.ErrorMeaasge);
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await app.StopAsync(stop.Token);
            await app.DisposeAsync();
        }
#endif
    }
}
