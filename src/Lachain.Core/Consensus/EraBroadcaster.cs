using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lachain.Logger;
using Lachain.Consensus;
using Lachain.Consensus.BinaryAgreement;
using Lachain.Consensus.CommonCoin;
using Lachain.Consensus.CommonSubset;
using Lachain.Consensus.HoneyBadger;
using Lachain.Consensus.Messages;
using Lachain.Consensus.ReliableBroadcast;
using Lachain.Consensus.RootProtocol;
using Lachain.Core.Blockchain.Hardfork;
using Lachain.Core.Blockchain.SystemContracts;
using Lachain.Core.Blockchain.Validators;
using Lachain.Core.Vault;
using Lachain.Networking;
using Lachain.Proto;
using Lachain.Storage.Repositories;
using MessageEnvelope = Lachain.Consensus.Messages.MessageEnvelope;

namespace Lachain.Core.Consensus
{
    /**
     * Stores and dispatches messages between set of protocol of one player within one era (block)
     */
    public class EraBroadcaster : IConsensusBroadcaster
    {
        private static readonly ILogger<EraBroadcaster> Logger = LoggerFactory.GetLoggerForClass<EraBroadcaster>();
        
        private readonly long _era;
        private readonly IConsensusMessageDeliverer _consensusMessageDeliverer;
        private readonly IMessageFactory _messageFactory;
        private readonly IPrivateWallet _wallet;
        private readonly IValidatorAttendanceRepository _validatorAttendanceRepository;
        private bool _terminated;
        private int _myIdx;
        private IPublicConsensusKeySet? _validators;

        public bool Ready => _validators != null;

        /**
         * Registered callbacks, identifying that one protocol requires result from another
         */
        private readonly IDictionary<IProtocolIdentifier, IProtocolIdentifier> _callback =
            new ConcurrentDictionary<IProtocolIdentifier, IProtocolIdentifier>();

        /**
         * Registry of all protocols for this era
         */
        private readonly IDictionary<IProtocolIdentifier, IConsensusProtocol> _registry =
            new ConcurrentDictionary<IProtocolIdentifier, IConsensusProtocol>();

        public EraBroadcaster(
            long era, IConsensusMessageDeliverer consensusMessageDeliverer,
            IPrivateWallet wallet, IValidatorAttendanceRepository validatorAttendanceRepository
        )
        {
            _consensusMessageDeliverer = consensusMessageDeliverer;
            _messageFactory = new MessageFactory(wallet.EcdsaKeyPair);
            _wallet = wallet;
            _terminated = false;
            _era = era;
            _myIdx = -1;
            _validatorAttendanceRepository = validatorAttendanceRepository;
        }

        public void SetValidatorKeySet(IPublicConsensusKeySet keySet)
        {
            _validators = keySet;
            _myIdx = _validators.GetValidatorIndex(_wallet.EcdsaKeyPair.PublicKey);
        }

        public void RegisterProtocols(IEnumerable<IConsensusProtocol> protocols)
        {
            foreach (var protocol in protocols)
            {
                _registry[protocol.Id] = protocol;
            }
        }

        public void Broadcast(ConsensusMessage message)
        {
            message.Validator = new Validator {Era = _era};
            if (_terminated)
            {
                Logger.LogTrace($"Era {_era} is already finished, skipping Broadcast");
                return;
            }

            var payload = _messageFactory.ConsensusMessage(message);
            foreach (var publicKey in _validators.EcdsaPublicKeySet)
            {
                if (publicKey.Equals(_wallet.EcdsaKeyPair.PublicKey))
                {
                    Dispatch(message, GetMyId());
                }
                else
                {
                    _consensusMessageDeliverer.SendTo(publicKey, payload);
                }
            }
        }

        public void SendToValidator(ConsensusMessage message, int index)
        {
            message.Validator = new Validator {Era = _era};
            if (_terminated)
            {
                Logger.LogTrace($"Era {_era} is already finished, skipping SendToValidator");
                return;
            }

            if (index < 0)
            {
                throw new ArgumentException("Validator index must be positive", nameof(index));
            }

            if (index == GetMyId())
            {
                Dispatch(message, index);
                return;
            }

            var payload = _messageFactory.ConsensusMessage(message);
            _consensusMessageDeliverer.SendTo(_validators.EcdsaPublicKeySet[index], payload);
        }

        public void Dispatch(ConsensusMessage message, int from)
        {
            if (_terminated)
            {
                Logger.LogTrace($"Era {_era} is already finished, skipping Dispatch");
                return;
            }

            if (message.Validator.Era != _era)
            {
                throw new InvalidOperationException(
                    $"Message for era {message.Validator.Era} dispatched to era {_era}");
            }

            switch (message.PayloadCase)
            {
                case ConsensusMessage.PayloadOneofCase.Bval:
                    var idBval = new BinaryBroadcastId(message.Validator.Era, message.Bval.Agreement,
                        message.Bval.Epoch);
                    EnsureProtocol(idBval)?.ReceiveMessage(new MessageEnvelope(message, from));
                    break;
                case ConsensusMessage.PayloadOneofCase.Aux:
                    var idAux = new BinaryBroadcastId(message.Validator.Era, message.Aux.Agreement, message.Aux.Epoch);
                    EnsureProtocol(idAux)?.ReceiveMessage(new MessageEnvelope(message, from));
                    break;
                case ConsensusMessage.PayloadOneofCase.Conf:
                    var idConf = new BinaryBroadcastId(message.Validator.Era, message.Conf.Agreement,
                        message.Conf.Epoch);
                    EnsureProtocol(idConf)?.ReceiveMessage(new MessageEnvelope(message, from));
                    break;
                case ConsensusMessage.PayloadOneofCase.Coin:
                    var idCoin = new CoinId(message.Validator.Era, message.Coin.Agreement, message.Coin.Epoch);
                    EnsureProtocol(idCoin)?.ReceiveMessage(new MessageEnvelope(message, from));
                    break;
                case ConsensusMessage.PayloadOneofCase.Decrypted:
                    var hbbftId = new HoneyBadgerId((int) message.Validator.Era);
                    EnsureProtocol(hbbftId)?.ReceiveMessage(new MessageEnvelope(message, from));
                    break;
                case ConsensusMessage.PayloadOneofCase.ValMessage:
                    var reliableBroadcastId = new ReliableBroadcastId(message.ValMessage.SenderId, (int) message.Validator.Era);
                    EnsureProtocol(reliableBroadcastId)?.ReceiveMessage(new MessageEnvelope(message, from));
                    break;
                case ConsensusMessage.PayloadOneofCase.EchoMessage:
                    var rbIdEchoMsg = new ReliableBroadcastId(message.EchoMessage.SenderId, (int) message.Validator.Era);
                    EnsureProtocol(rbIdEchoMsg)?.ReceiveMessage(new MessageEnvelope(message, from));
                    break;
                case ConsensusMessage.PayloadOneofCase.ReadyMessage:
                    var rbIdReadyMsg = new ReliableBroadcastId(message.ReadyMessage.SenderId, (int) message.Validator.Era);
                    EnsureProtocol(rbIdReadyMsg)?.ReceiveMessage(new MessageEnvelope(message, from));
                    break;
                case ConsensusMessage.PayloadOneofCase.SignedHeaderMessage:
                    var rootId = new RootProtocolId(message.Validator.Era);
                    EnsureProtocol(rootId)?.ReceiveMessage(new MessageEnvelope(message, from));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown message type {message}");
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void InternalRequest<TId, TInputType>(ProtocolRequest<TId, TInputType> request)
            where TId : IProtocolIdentifier
        {
            if (_terminated)
            {
                Logger.LogTrace($"Era {_era} is already finished, skipping InternalRequest");
                return;
            }

            if (request.From != null)
            {
                if (_callback.TryGetValue(request.To, out var existingCallback))
                {
                    throw new InvalidOperationException(
                        $"Cannot have two requests from different protocols ({request.From}, " +
                        $"{existingCallback}) to one protocol {request.To}"
                    );
                }

                _callback[request.To] = request.From;
            }

            Logger.LogTrace($"Protocol {request.From} requested result from protocol {request.To}");
            EnsureProtocol(request.To);
            
            if (_registry.TryGetValue(request.To, out var protocol))
                protocol?.ReceiveMessage(new MessageEnvelope(request, GetMyId()));
        }

        public void InternalResponse<TId, TResultType>(ProtocolResult<TId, TResultType> result)
            where TId : IProtocolIdentifier
        {
            Logger.LogTrace($"Protocol {result.From} returned result");
            if (_terminated)
            {
                Logger.LogTrace($"Era {_era} is already finished, skipping InternalResponse");
                return;
            }

            if (_callback.TryGetValue(result.From, out var senderId))
            {
                if (!_registry.TryGetValue(senderId, out var cbProtocol))
                {
                    Logger.LogWarning($"There is no protocol registered to get result from {senderId}");
                }
                else
                {
                    cbProtocol?.ReceiveMessage(new MessageEnvelope(result, GetMyId()));
                    Logger.LogTrace($"Result from protocol {result.From} delivered to {senderId}");
                }
            }

            // message is also delivered to self
        //    Logger.LogTrace($"Result from protocol {result.From} delivered to itself");
            if (_registry.TryGetValue(result.From, out var protocol))
                protocol?.ReceiveMessage(new MessageEnvelope(result, GetMyId()));
        }

        public int GetMyId()
        {
            return _myIdx;
        }

        public int GetIdByPublicKey(ECDSAPublicKey publicKey)
        {
            return _validators.GetValidatorIndex(publicKey);
        }

        public IConsensusProtocol? GetProtocolById(IProtocolIdentifier id)
        {
            return _registry.TryGetValue(id, out var value) ? value : null;
        }

        public void Terminate()
        {
            if (_terminated) return;
            _terminated = true;
            foreach (var protocol in _registry)
            {
                protocol.Value.Terminate();
            }

            _registry.Clear();
            _callback.Clear();
        }

        // Each ProtocolId is created only once to prevent spamming, Protocols are mapped against ProtocolId, so each
        // Protocol will also be created only once, after achieving result, Protocol terminate and no longer process any
        // messages. So if the required protocol (lets say 'parent protocol') that will use the result of newly created
        // protocols (lets say 'child protocol') is not created before 'child protocol' returns result, then the
        // 'parent protocol' will never be able to use their results and will get stuck
        // For ReliableBroadcast, this is not a problem, because it waits for at least F + 1 inputs from validators.
        // But some protocol (CommonCoin) may have problem if spammed from malicious validators.
        // BinaryAgreement is created only from InternalRequest, not from ExternalMessage, so it is safe as well.
        // For each protocol, the corresponding 'parent protocol' is stored in _callback dictionary
        [MethodImpl(MethodImplOptions.Synchronized)]
        private IConsensusProtocol? EnsureProtocol(IProtocolIdentifier id)
        {
            ValidateId(id);
            if (_registry.TryGetValue(id, out var existingProtocol)) return existingProtocol;
            Logger.LogTrace($"Creating protocol {id} on demand");
            if (_terminated)
            {
                Logger.LogTrace($"Protocol {id} not created since broadcaster is terminated");
                return null;
            }

            switch (id)
            {
                case BinaryBroadcastId bbId:
                    if (!ValidateBinaryBroadcastId(bbId))
                        return null;
                    var bb = new BinaryBroadcast(bbId, _validators, this);
                    RegisterProtocols(new[] {bb});
                    return bb;
                case CoinId coinId:
                    if (!ValidateCoinId(coinId))
                        return null;
                    var coin = new CommonCoin(
                        coinId, _validators,
                        _wallet.GetThresholdSignatureKeyForBlock((ulong) _era - 1) ??
                        throw new InvalidOperationException($"No TS keys present for era {_era}"),
                        this
                    );
                    RegisterProtocols(new[] {coin});
                    return coin;
                case ReliableBroadcastId rbcId:
                    if (!ValidateSenderId((long) rbcId.SenderId))
                        return null;
                    var rbc = new ReliableBroadcast(rbcId, _validators, this);
                    RegisterProtocols(new[] {rbc});
                    return rbc;
                case BinaryAgreementId baId:
                    var ba = new BinaryAgreement(baId, _validators, this);
                    RegisterProtocols(new[] {ba});
                    return ba;
                case CommonSubsetId acsId:
                    var acs = new CommonSubset(acsId, _validators, this);
                    RegisterProtocols(new[] {acs});
                    return acs;
                case HoneyBadgerId hbId:
                    var hb = new HoneyBadger(
                        hbId, _validators,
                        _wallet.GetTpkePrivateKeyForBlock((ulong) _era - 1)
                        ?? throw new InvalidOperationException($"No TPKE keys present for era {_era}"),
                        this
                    );
                    RegisterProtocols(new[] {hb});
                    return hb;
                case RootProtocolId rootId:
                    var root = new RootProtocol(rootId, _validators, _wallet.EcdsaKeyPair.PrivateKey, 
                        this, _validatorAttendanceRepository, StakingContract.CycleDuration,
                        HardforkHeights.IsHardfork_9Active((ulong)_era));
                    RegisterProtocols(new[] {root});
                    return root;
                default:
                    throw new Exception($"Unknown protocol type {id}");
            }
        }

        private void ValidateId(IProtocolIdentifier id)
        {
            if (id.Era != _era)
                throw new InvalidOperationException($"Era mismatched, expected {_era} got message with {id.Era}");
        }
        
        // There are separate instance of ReliableBroadcast for each validator.
        // Check if the SenderId is one of the validator's id before creating ReliableBroadcastId
        // Sender id is basically validator's id, so it must be between 0 and N-1 inclusive
        private bool ValidateSenderId(long senderId)
        {
            if (_validators is null)
            {
                Logger.LogWarning("We don't have validators");
                return false;
            }
            if (senderId < 0 || senderId >= _validators.N)
            {
                Logger.LogWarning($"Invalid sender id in consensus message: {senderId}. N: {_validators.N}");
                return false;
            }
            return true;
        }

        // CommonCoin returns result immediately after getting a valid input and then terminates
        // This input could be via network from another validator or its own generated
        // If the 'parent protocol' (mentioned above) is not created when this protocol is requested
        // then the 'parent protocol' will not get the result and will get stuck'
        // So check if the 'parent protocol' exists
        private bool ValidateCoinId(CoinId coinId)
        {
            if (coinId.Agreement == -1 && coinId.Epoch == 0)
            {
                // This type of coinId is created from RootProtocol or via network from another validator
                return _callback.TryGetValue(coinId, out var _);
            }
            else if (ValidateSenderId(coinId.Agreement))
            {
                // BinaryAgreement requests such CommonCoin
                if (!_callback.TryGetValue(coinId, out var binaryAgreementId) ||
                    !_registry.TryGetValue(binaryAgreementId, out var binaryAgreement) || 
                        binaryAgreement.Terminated)
                    return false;
                return true;
            }
            else 
                return false;
        }

        // BinaryBroadcast needs at least F + 1 responses from validators to reach result
        // So malicious validators are not a threat in terms of reaching wrong result or
        // Creating BinaryBroadcast too early so that its 'parent protocol', BinaryAgreement
        // does not get the result. But we need to stop spam creation of this protocol as it
        // can be created too many times with too many epochs
        // BinaryAgreement creates BinaryBroadcast sequentially, for each even epoch using the 
        // result of previous CommonCoin as estimation. Different honest validators can start
        // the protocol in different time and broadcast due to network latency, but none of them
        // will reach a verdict without at least F + 1 response, so we can assume that if a 
        // BinaryBroadcast protocol is created and broadcasted by an honest validator, then that
        // honest validator reached a valid verdict with at least F + 1 response in a previously
        // created BinaryBroadcast protocol, and so the current validator (this node) has also
        // reached the same verdict in the previous BinaryBroadcast protocol.
        // So we can check if the previous BinaryBroadcast protocol has terminated, if so, then 
        // we can create another BinaryBroadcast protocol
        private bool ValidateBinaryBroadcastId(BinaryBroadcastId binaryBroadcastId)
        {
            if (!ValidateSenderId(binaryBroadcastId.Agreement) || binaryBroadcastId.Epoch < 0)
                return false;
            else if (binaryBroadcastId.Epoch > 0 && (binaryBroadcastId.Epoch & 1) == 0) // positive and even
            {
                var previousBinaryBroadcastId = 
                    new BinaryBroadcastId(binaryBroadcastId.Era, binaryBroadcastId.Agreement, binaryBroadcastId.Epoch - 2);
                if (!_registry.TryGetValue(previousBinaryBroadcastId, out var previousBinaryBroadcast) ||
                    !previousBinaryBroadcast.Terminated)
                    return false;
                return true;
            }
            else 
                return true;
        }

        public bool WaitFinish(TimeSpan timeout)
        {
            return EnsureProtocol(new RootProtocolId(_era))?.WaitFinish(timeout) ?? true;
        }

        public IDictionary<IProtocolIdentifier, IConsensusProtocol> GetRegistry()
        {
            return _registry;
        }
    }
}