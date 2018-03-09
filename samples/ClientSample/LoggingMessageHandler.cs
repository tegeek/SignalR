// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ClientSample
{
    internal class LoggingMessageHandler : DelegatingHandler
    {
        public LoggingMessageHandler(HttpMessageHandler innerHandler) : base(innerHandler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Console.WriteLine("Send: {0} {1}", request.Method, request.RequestUri);
            var result = await base.SendAsync(request, cancellationToken);
            Console.WriteLine("Recv: {0} {1}", (int)result.StatusCode, request.RequestUri);
            return result;
        }
    }
}