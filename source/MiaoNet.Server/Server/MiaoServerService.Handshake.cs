using System.Buffers.Binary;
using MiaoNet.Shared;
using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

partial class MiaoServerService
{
    private async Task HandlePendingConnectionAsync(IPendingNetworkConnection pendingConnection, CancellationToken token)
    {
        string addr = pendingConnection.RemoteAddress;
        INetworkConnection? networkConnection = null;
        HandshakeResult? handshakeResult;

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(options.HandshakeTimeout);
        var localToken = cts.Token;
        try
        {
            networkConnection = await pendingConnection.CompleteAsync(localToken);
            if (networkConnection is null)
            {
                logger.LogInformation(AppEvents.Connection, "{addr} is not a MiaoNet client.", addr);
                return;
            }
            bool result;

            // we've finished TLS handshake and MiaoNet client check
            // now do version check
            result = await DoVersionCheckAsync(networkConnection, localToken);
            if (!result)
            {
                networkConnection.Shutdown();
                networkConnection.Dispose();
                return;
            }

            // version check finished, now make our own handshake
            handshakeResult = await DoHandshakeAsync(networkConnection, localToken);
            if (handshakeResult is null)
            {
                networkConnection.Shutdown();
                networkConnection.Dispose();
                return;
            }
        }
        catch (OperationCanceledException e)
        when (e.CancellationToken == cts.Token)
        {
            networkConnection?.Dispose();
            pendingConnection.Dispose();
            logger.LogInformation(AppEvents.Connection, "{addr} Handshake timeouted.", addr);
            return;
        }
        catch (Exception e)
        {
            networkConnection?.Dispose();
            pendingConnection.Dispose();
            logger.LogError(
                AppEvents.Connection, e,
                "Error when completing pending connection({addr}).",
                addr
            );
            return;
        }
        finally
        {
            cts.Dispose();
        }

        // now it's not "pending" for us
        await HandleConnectionAsync(networkConnection, handshakeResult);

        // maybe we could improve our serialization implement...
        // this is ugly
        async Task<bool> DoVersionCheckAsync(INetworkConnection networkConnection, CancellationToken token)
        {
            var stream = networkConnection.Stream;

            var version = Connection.Version;
            ushort major = (ushort)version.Major, minor = (ushort)version.Minor, build = (ushort)version.Build;
            ushort majorClient, minorClient, buildClient;

            const int VersionLength = 3 * sizeof(ushort);
            byte[] buffer = pool.Rent(VersionLength);
            bool passed;
            try
            {
                var memory = buffer.AsMemory(0, VersionLength);

                await stream.ReadExactlyAsync(memory, token);
                var span = memory.Span;
                majorClient = BinaryPrimitives.ReadUInt16LittleEndian(span[0..2]);
                minorClient = BinaryPrimitives.ReadUInt16LittleEndian(span[2..4]);
                buildClient = BinaryPrimitives.ReadUInt16LittleEndian(span[4..6]);

                passed = Connection.IsVersionCompatible(
                    new Version(majorClient, minorClient, buildClient),
                    version
                );
            }
            finally
            {
                pool.Return(buffer);
            }

            buffer = pool.Rent(1 + VersionLength);
            try
            {
                var memory = buffer.AsMemory(0, 1 + VersionLength);
                var span = memory.Span;
                if (!passed)
                {
                    span[0] = 0;
                    BinaryPrimitives.WriteUInt16LittleEndian(span[1..3], major);
                    BinaryPrimitives.WriteUInt16LittleEndian(span[3..5], minor);
                    BinaryPrimitives.WriteUInt16LittleEndian(span[5..7], build);
                    logger.LogInformation(
                        AppEvents.Connection,
                        "{addr} version {v1}.{v2}.{v3} does not match current version.",
                        networkConnection.RemoteAddress, majorClient, minorClient, buildClient
                    );
                    await stream.WriteAsync(memory[0..(1 + VersionLength)], token);
                    return false;
                }
                else
                {
                    span[0] = 1;
                    await stream.WriteAsync(memory[0..1], token);
                    return true;
                }
            }
            finally
            {
                pool.Return(buffer);
            }
        }

        async Task<HandshakeResult?> DoHandshakeAsync(INetworkConnection networkConnection, CancellationToken token)
        {
            var stream = networkConnection.Stream;
            ushort size;

            var buffer = pool.Rent(sizeof(ushort));
            try
            {
                var memory = buffer.AsMemory(0, sizeof(ushort));
                await stream.ReadExactlyAsync(memory, token);
                size = BinaryPrimitives.ReadUInt16LittleEndian(memory.Span);
            }
            finally
            {
                pool.Return(buffer);
            }

            HandshakeData handshakeData;
            buffer = pool.Rent(size);
            try
            {
                var memory = buffer.AsMemory(0, size);
                await stream.ReadExactlyAsync(memory, token);
                var span = memory.Span;
                RefBinaryReader reader = new(span);
                handshakeData = reader.Read<HandshakeData>();
            }
            finally
            {
                pool.Return(buffer);
            }

            var authResult = await authenticator.AuthenticateAsync(
                handshakeData.AuthenticationData,
                handshakeData.IsAuthorize,
                token
            );

            // accounts frozen from the admin panel may not log in until the freeze expires
            if (!authResult.IsFailed
                && temporaryFreezeStore.TryGetFrozenUntil(authResult.PlayerInfo.AuthID, out var frozenUntil))
            {
                authResult = new(
                    AuthenticationResultType.Suspended,
                    new SuspensionInfo(
                        authResult.PlayerInfo.Name, null,
                        $"你已被临时冻结，解冻时间：{frozenUntil.ToLocalTime():yyyy-MM-dd HH:mm}",
                        frozenUntil.UtcDateTime
                    )
                );
            }

            string? failedReason = authResult.IsFailed
                ? authResult.SuspendMessage
                    ?? (authResult.Suspension is { } s ? $"你已被封禁，解封时间：{s.Until.ToLocalTime():yyyy-MM-dd HH:mm}" : null)
                : null;
            HandshakeAckData ack = new(authResult.Type, authResult.TokenData, failedReason);

            MemoryStream ms = new(32);
            ms.Seek(2, SeekOrigin.Begin);
            RefBinaryWriter writer = new(ms);
            writer.Write(ack);
            ushort ackSize = (ushort)(ms.Position - sizeof(ushort));
            ms.Seek(0, SeekOrigin.Begin);
            writer.Write(ackSize);
            Memory<byte> memoryToSend = ms.GetBuffer().AsMemory(0, ackSize + sizeof(ushort));
            await stream.WriteAsync(memoryToSend, token);

            return authResult.IsFailed ? null : new HandshakeResult(authResult.PlayerInfo, handshakeData, ack);
        }
    }

}
