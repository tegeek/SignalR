using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Client.Tests
{
    public class HubConnectionTestsConnectionLifecycle
    {
        // This tactic (using names and a dictionary) allows non-serializable data (like a Func) to be used in a theory AND get it to show in the new hierarchical view in Test Explorer as separate tests you can run individually.
        private static IDictionary<string, Func<HubConnection, Task>> MethodsThatRequireActiveConnection = new Dictionary<string, Func<HubConnection, Task>>()
        {
            { nameof(HubConnection.StopAsync), (connection) => connection.StopAsync() },
            { nameof(HubConnection.InvokeAsync), (connection) => connection.InvokeAsync("Foo") },
            { nameof(HubConnection.SendAsync), (connection) => connection.SendAsync("Foo") },
            { nameof(HubConnection.StreamAsChannelAsync), (connection) => connection.StreamAsChannelAsync<object>("Foo") },
        };

        public static IEnumerable<object[]> MethodsNamesThatRequireActiveConnection => MethodsThatRequireActiveConnection.Keys.Select(k => new object[] { k });

        [Fact]
        public async Task StartAsyncStartsTheUnderlyingConnection()
        {
            var testConnection = new TestConnection();
            var connection = new HubConnection(() => testConnection, new JsonHubProtocol());

            try
            {
                await connection.StartAsync();

                Assert.True(testConnection.Started.IsCompleted);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task StartAsyncFailsIfAlreadyStarting()
        {
            // Set up StartAsync to wait on the syncPoint when starting
            var testConnection = new TestConnection(onStart: SyncPoint.Create(out var syncPoint));
            var connection = new HubConnection(() => testConnection, new JsonHubProtocol());

            try
            {
                var firstStart = connection.StartAsync().OrTimeout();

                // Wait for us to be in IConnection.StartAsync
                await syncPoint.WaitForSyncPoint();

                // Try starting again
                var secondStart = connection.StartAsync().OrTimeout();

                // Release the sync point
                syncPoint.Continue();

                // First start should finish fine
                await firstStart;

                // Second start should have thrown
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => secondStart);
                Assert.Equal($"The '{nameof(HubConnection.StartAsync)}' method cannot be called if the connection has already been started.", ex.Message);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [Theory]
        [MemberData(nameof(MethodsNamesThatRequireActiveConnection))]
        public async Task MethodsThatRequireStartedConnectionFailIfConnectionNotYetStarted(string name)
        {
            var method = MethodsThatRequireActiveConnection[name];

            var testConnection = new TestConnection();
            var connection = new HubConnection(() => testConnection, new JsonHubProtocol());

            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => method(connection));
                Assert.Equal($"The '{name}' method cannot be called if the connection is not active", ex.Message);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [Theory]
        [MemberData(nameof(MethodsNamesThatRequireActiveConnection))]
        public async Task MethodsThatRequireStartedConnectionWaitForStartIfConnectionIsCurrentlyStarting(string name)
        {
            var method = MethodsThatRequireActiveConnection[name];

            // Set up StartAsync to wait on the syncPoint when starting
            var testConnection = new TestConnection(onStart: SyncPoint.Create(out var syncPoint));
            var connection = new HubConnection(() => testConnection, new JsonHubProtocol());

            try
            {
                // Start, and wait for the sync point to be hit
                var startTask = connection.StartAsync().OrTimeout();
                await syncPoint.WaitForSyncPoint();

                // Run the method, but it will be waiting for the lock
                var targetTask = method(connection).OrTimeout();

                // Release the SyncPoint
                syncPoint.Continue();

                // Wait for start to finish
                await startTask;

                // We need some special logic to ensure InvokeAsync completes.
                if (string.Equals(name, nameof(HubConnection.InvokeAsync)))
                {
                    // Dump the handshake message
                    _ = await testConnection.SentMessages.ReadAsync();

                    // We need to "complete" the invocation
                    var message = await testConnection.ReadSentTextMessageAsync();
                    var json = JObject.Parse(message.Substring(0, message.Length - 1)); // Gotta remove the record separator.
                    await testConnection.ReceiveJsonMessage(new
                    {
                        type = HubProtocolConstants.CompletionMessageType,
                        invocationId = json["invocationId"],
                    });
                }

                // Wait for the method to complete, with an expected error.
                await targetTask;
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task StopAsyncStopsConnection()
        {
            var testConnection = new TestConnection();
            var connection = new HubConnection(() => testConnection, new JsonHubProtocol());

            await connection.StartAsync();
            Assert.True(testConnection.Started.IsCompleted);

            await connection.StopAsync();
            Assert.True(testConnection.Disposed.IsCompleted);
        }
    }
}
