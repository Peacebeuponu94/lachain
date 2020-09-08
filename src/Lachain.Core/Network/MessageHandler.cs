using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using Lachain.Logger;
using Lachain.Core.Blockchain.Interface;
using Lachain.Core.Blockchain.Pool;
using Lachain.Core.Consensus;
using Lachain.Networking;
using Lachain.Networking.Hub;
using Lachain.Proto;
using Lachain.Storage.State;
using Lachain.Utility.Utils;

namespace Lachain.Core.Network
{
    public class MessageHandler : IMessageHandler
    {
        private static readonly ILogger<MessageHandler> Logger = LoggerFactory.GetLoggerForClass<MessageHandler>();

        private readonly IBlockSynchronizer _blockSynchronizer;
        private readonly ITransactionPool _transactionPool;
        private readonly IStateManager _stateManager;
        private readonly IConsensusManager _consensusManager;
        private readonly INetworkManager _networkManager;
        private readonly IPeerManager _peerManager;

        /*
         * TODO: message queue is a hack. We should design additional layer for storing/persisting consensus messages
         */
        private readonly IDictionary<long, List<Tuple<ConsensusMessage, ECDSAPublicKey>>> _queuedMessages =
            new ConcurrentDictionary<long, List<Tuple<ConsensusMessage, ECDSAPublicKey>>>();

        public MessageHandler(
            IBlockSynchronizer blockSynchronizer,
            ITransactionPool transactionPool,
            IStateManager stateManager,
            IConsensusManager consensusManager,
            IBlockManager blockManager,
            INetworkManager networkManager,
            IPeerManager peerManager
        )
        {
            _blockSynchronizer = blockSynchronizer;
            _transactionPool = transactionPool;
            _stateManager = stateManager;
            _consensusManager = consensusManager;
            _networkManager = networkManager;
            _peerManager = peerManager;
            blockManager.OnBlockPersisted += BlockManagerOnBlockPersisted;
            transactionPool.TransactionAdded += TransactionPoolOnTransactionAdded;
            _networkManager.OnPingRequest += OnPingRequest;
            _networkManager.OnPingReply += OnPingReply;
            _networkManager.OnGetBlocksByHashesRequest += OnGetBlocksByHashesRequest;
            _networkManager.OnGetBlocksByHashesReply += OnGetBlocksByHashesReply;
            _networkManager.OnGetBlocksByHeightRangeRequest += OnGetBlocksByHeightRangeRequest;
            _networkManager.OnGetPeersRequest += OnGetPeersRequest;
            _networkManager.OnGetBlocksByHeightRangeReply += OnGetBlocksByHeightRangeReply;
            _networkManager.OnGetPeersReply += OnGetPeersReply;
            _networkManager.OnGetTransactionsByHashesRequest += OnGetTransactionsByHashesRequest;
            _networkManager.OnGetTransactionsByHashesReply += OnGetTransactionsByHashesReply;
            _networkManager.OnConsensusMessage += OnConsensusMessage;
        }

        private void TransactionPoolOnTransactionAdded(object sender, TransactionReceipt e)
        {
            _networkManager.BroadcastLocalTransaction(e);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void BlockManagerOnBlockPersisted(object sender, Block e)
        {
            var era = (long) e.Header.Index + 1;
            if (!_queuedMessages.TryGetValue(era, out var messages)) return;
            _queuedMessages.Remove(era);
            foreach (var (message, key) in messages)
            {
                OnConsensusMessage(this, (message, key));
            }
        }

        private void OnPingRequest(object sender, (PingRequest request, Action<PingReply> callback) @event)
        {
            Logger.LogTrace("Start processing PingRequest");
            var (_, callback) = @event;
            var reply = new PingReply
            {
                Timestamp = TimeUtils.CurrentTimeMillis(),
                BlockHeight = _stateManager.LastApprovedSnapshot.Blocks.GetTotalBlockHeight()
            };
            Logger.LogTrace("Finished processing PingRequest");
            callback(reply);
        }

        private void OnPingReply(object sender, (PingReply reply, ECDSAPublicKey publicKey) @event)
        {
            Logger.LogTrace("Start processing PingReply");
            var (reply, publicKey) = @event;
            _blockSynchronizer.HandlePeerHasBlocks(reply.BlockHeight, publicKey);
            Logger.LogTrace("Finished processing PingReply");
        }

        private void OnGetBlocksByHashesRequest(object sender,
            (GetBlocksByHashesRequest request, Action<GetBlocksByHashesReply> callback) @event)
        {
            Logger.LogTrace("Start processing GetBlocksByHashesRequest");
            var (request, callback) = @event;
            var reply = new GetBlocksByHashesReply
            {
                Blocks = {_stateManager.LastApprovedSnapshot.Blocks.GetBlocksByHashes(request.BlockHashes)}
            };
            callback(reply);
            Logger.LogTrace("Finished processing GetBlocksByHashesRequest");
        }

        private void OnGetBlocksByHashesReply(object sender,
            (GetBlocksByHashesReply reply, ECDSAPublicKey publicKey) @event)
        {
            Logger.LogTrace("Start processing GetBlocksByHashesReply");
            var (reply, publicKey) = @event;
            var orderedBlocks = reply.Blocks.OrderBy(block => block.Header.Index).ToArray();
            foreach (var block in orderedBlocks)
            {
                if (!_blockSynchronizer.HandleBlockFromPeer(block, publicKey))
                    break;
            }

            _peerManager.UpdatePeerTimestamp(publicKey);
            Logger.LogTrace("Finished processing GetBlocksByHashesReply");
        }

        private void OnGetBlocksByHeightRangeRequest(object sender,
            (GetBlocksByHeightRangeRequest request, Action<GetBlocksByHeightRangeReply> callback) @event)
        {
            Logger.LogTrace("Start processing GetBlocksByHeightRangeRequest");
            var (request, callback) = @event;
            var blockHashes = _stateManager.LastApprovedSnapshot.Blocks
                .GetBlocksByHeightRange(request.FromHeight, request.ToHeight - request.FromHeight + 1)
                .Select(block => block.Hash);
            Logger.LogTrace("Finished processing GetBlocksByHeightRangeRequest");
            callback(new GetBlocksByHeightRangeReply {BlockHashes = {blockHashes}});
        }

        private void OnGetBlocksByHeightRangeReply(object sender,
            (GetBlocksByHeightRangeReply reply, Action<GetBlocksByHashesRequest> callback) @event)
        {
            Logger.LogTrace("Start processing GetBlocksByHeightRangeReply");
            var (reply, callback) = @event;
            var request = new GetBlocksByHashesRequest {BlockHashes = {reply.BlockHashes}};
            Logger.LogTrace("Finished processing GetBlocksByHeightRangeReply");
            callback(request);
        }

        private void OnGetTransactionsByHashesRequest(object sender,
            (GetTransactionsByHashesRequest request, Action<GetTransactionsByHashesReply> callback) @event)
        {
            var (request, callback) = @event;
            Logger.LogTrace($"Get request for {request.TransactionHashes.Count} transactions");
            var txs = request.TransactionHashes
                .Select(txHash => _stateManager.LastApprovedSnapshot.Transactions.GetTransactionByHash(txHash) ??
                                  _transactionPool.GetByHash(txHash))
                .Where(tx => tx != null)
                .Select(tx => tx!)
                .ToList();
            Logger.LogTrace($"Replying request with {txs.Count} transactions");
            if (txs.Count == 0) return;
            callback(new GetTransactionsByHashesReply {Transactions = {txs}});
        }

        private void OnGetPeersRequest(object sender,
            (GetPeersRequest request, Action<GetPeersReply> callback) @event)
        {
            var (_, callback) = @event;
            var (peers, publicKeys) = _peerManager.GetPeersToBroadcast();
            Logger.LogTrace($"Got {publicKeys.Length} public keys and {peers.Length} peers to broadcast");
            foreach (var peer in peers)
                if (_networkManager.IsSelfConnect(IPAddress.Parse(peer.Host)))
                    peer.Host = _peerManager.GetExternalIp();
            Logger.LogTrace("Finished processing GetPeersRequest");
            callback(new GetPeersReply
            {
                Peers = {peers},
                PublicKeys = {publicKeys}
            });
        }

        private void OnGetTransactionsByHashesReply(object sender,
            (GetTransactionsByHashesReply reply, ECDSAPublicKey publicKey) @event)
        {
            Logger.LogTrace("Start processing GetTransactionsByHashesReply");
            var (reply, publicKey) = @event;
            _blockSynchronizer.HandleTransactionsFromPeer(reply.Transactions, publicKey);
            _peerManager.UpdatePeerTimestamp(publicKey);
            Logger.LogTrace("Finished processing GetTransactionsByHashesReply");
        }

        private void OnGetPeersReply(object sender,
            (GetPeersReply reply, ECDSAPublicKey publicKey, Func<ECDSAPublicKey, ClientWorker?> connect) @event)
        {
            Logger.LogTrace("Start processing GetPeersReply");
            foreach (var t in @event.reply.Peers)
            {
                t.Host = _networkManager.CheckLocalConnection(t.Host);
            }

            var peers = _peerManager.HandlePeersFromPeer(@event.reply.Peers, @event.reply.PublicKeys);
            _peerManager.UpdatePeerTimestamp(@event.publicKey);
            foreach (var peer in peers)
                @event.connect(peer.PublicKey!);
            Logger.LogTrace("Finished processing GetPeersReply");
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void OnConsensusMessage(object sender, (ConsensusMessage message, ECDSAPublicKey publicKey) @event)
        {
            var (message, publicKey) = @event;
            try
            {
                _consensusManager.Dispatch(message, publicKey);
            }
            catch (ConsensusStateNotPresentException)
            {
                _queuedMessages.ComputeIfAbsent(
                        message.Validator.Era,
                        x => new List<Tuple<ConsensusMessage, ECDSAPublicKey>>()
                    )
                    .Add(new Tuple<ConsensusMessage, ECDSAPublicKey>(message, publicKey));

                Logger.LogTrace("Queued message too far in future...");
            }
        }
    }
}