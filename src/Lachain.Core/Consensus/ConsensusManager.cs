using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Lachain.Logger;
using Lachain.Consensus;
using Lachain.Consensus.Messages;
using Lachain.Consensus.RootProtocol;
using Lachain.Core.Blockchain.Interface;
using Lachain.Core.Blockchain.Validators;
using Lachain.Core.Vault;
using Lachain.Networking;
using Lachain.Proto;
using Lachain.Storage.Repositories;
using Lachain.Utility.Utils;

namespace Lachain.Core.Consensus
{
    public class ConsensusManager : IConsensusManager
    {
        private static readonly ILogger<ConsensusManager> Logger = LoggerFactory.GetLoggerForClass<ConsensusManager>();
        private readonly IMessageDeliverer _messageDeliverer;
        private readonly IValidatorManager _validatorManager;
        private readonly IValidatorAttendanceRepository _validatorAttendanceRepository;
        private readonly IBlockProducer _blockProducer;
        private readonly IBlockchainContext _blockchainContext;
        private bool _terminated;
        private readonly IPrivateWallet _privateWallet;

        private readonly IDictionary<long, List<(ConsensusMessage message, ECDSAPublicKey from)>> _postponedMessages
            = new Dictionary<long, List<(ConsensusMessage message, ECDSAPublicKey from)>>();

        private long CurrentEra { get; set; } = -1;

        private readonly Dictionary<long, EraBroadcaster> _eras = new Dictionary<long, EraBroadcaster>();
        private readonly object _blockPersistedLock = new object();

        public ConsensusManager(
            IMessageDeliverer messageDeliverer,
            IValidatorManager validatorManager,
            IBlockProducer blockProducer,
            IBlockManager blockManager,
            IBlockchainContext blockchainContext,
            IPrivateWallet privateWallet,
            IValidatorAttendanceRepository validatorAttendanceRepository
        )
        {
            _messageDeliverer = messageDeliverer;
            _validatorManager = validatorManager;
            _blockProducer = blockProducer;
            _blockchainContext = blockchainContext;
            _privateWallet = privateWallet;
            _validatorAttendanceRepository = validatorAttendanceRepository;
            _terminated = false;

            blockManager.OnBlockPersisted += BlockManagerOnOnBlockPersisted;
        }

        private void BlockManagerOnOnBlockPersisted(object sender, Block e)
        {
            Logger.LogDebug($"Block {e.Header.Index} is persisted, terminating corresponding era");
            if ((long) e.Header.Index >= CurrentEra)
            {
                AdvanceEra((long) e.Header.Index);
            }

            lock (_blockPersistedLock)
            {
                Monitor.PulseAll(_blockPersistedLock);
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void AdvanceEra(long newEra)
        {
            if (newEra < CurrentEra)
            {
                throw new InvalidOperationException($"Cannot advance backwards from era {CurrentEra} to era {newEra}");
            }

            for (var i = CurrentEra; i <= newEra; ++i)
            {
                if (!IsValidatorForEra(i)) continue;
                var broadcaster = EnsureEra(i);
                broadcaster?.Terminate();
                _eras.Remove(i);
            }

            CurrentEra = newEra;
        }

        private bool IsValidatorForEra(long era)
        {
            if (era <= 0) return false;
            return _validatorManager.IsValidatorForBlock(_privateWallet.EcdsaKeyPair.PublicKey, era);
        }


        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Dispatch(ConsensusMessage message, ECDSAPublicKey from)
        {
            var era = message.Validator.Era;
            if (CurrentEra == -1)
                Logger.LogWarning($"Consensus has not been started yet, skipping message with era {era}");
            else if (era < CurrentEra)
                Logger.LogTrace($"Skipped message for era {era} since we already advanced to {CurrentEra}");
            else if (era > CurrentEra)
                _postponedMessages
                    .PutIfAbsent(era, new List<(ConsensusMessage message, ECDSAPublicKey from)>())
                    .Add((message, from));
            else
            {
                if (!IsValidatorForEra(era))
                {
                    Logger.LogWarning($"Skipped message for era {era} since we are not validator for this era");
                    return;
                }

                var fromIndex = _validatorManager.GetValidatorIndex(from, era - 1);
                EnsureEra(era)?.Dispatch(message, fromIndex);
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Start(long startingEra)
        {
            CurrentEra = startingEra;
            new Thread(Run).Start();
        }

        private void Run()
        {
            try
            {
                ulong lastBlock = 0;
                const ulong minBlockInterval = 000;
                for (;; CurrentEra += 1)
                {
                    var now = TimeUtils.CurrentTimeMillis();
                    if (lastBlock + minBlockInterval > now)
                    {
                        Thread.Sleep(TimeSpan.FromMilliseconds(lastBlock + minBlockInterval - now));
                    }

                    if ((long) _blockchainContext.CurrentBlockHeight >= CurrentEra)
                    {
                        AdvanceEra((long) _blockchainContext.CurrentBlockHeight);
                        continue;
                    }

                    var weAreValidator = _validatorManager
                        .GetValidators(CurrentEra - 1)
                        .EcdsaPublicKeySet
                        .Contains(_privateWallet.EcdsaKeyPair.PublicKey);

                    if (!weAreValidator)
                    {
                        Logger.LogWarning($"We are not validator for era {CurrentEra}, waiting");
                        while ((long) _blockchainContext.CurrentBlockHeight < CurrentEra)
                        {
                            lock (_blockPersistedLock)
                            {
                                Monitor.Wait(_blockPersistedLock);
                            }
                        }
                    }
                    else
                    {
                        var broadcaster = EnsureEra(CurrentEra) ?? throw new InvalidOperationException();
                        var rootId = new RootProtocolId(CurrentEra);
                        broadcaster.InternalRequest(
                            new ProtocolRequest<RootProtocolId, IBlockProducer>(rootId, rootId, _blockProducer)
                        );

                        if (_postponedMessages.TryGetValue(CurrentEra, out var savedMessages))
                        {
                            Logger.LogDebug(
                                $"Processing {savedMessages.Count} postponed messages for era {CurrentEra}");
                            foreach (var (message, from) in savedMessages)
                            {
                                var fromIndex = _validatorManager.GetValidatorIndex(from, CurrentEra - 1);
                                broadcaster.Dispatch(message, fromIndex);
                            }

                            _postponedMessages.Remove(CurrentEra);
                        }

                        broadcaster.WaitFinish();
                        broadcaster.Terminate();
                        _eras.Remove(CurrentEra);
                        Logger.LogDebug("Root protocol finished, waiting for new era...");
                        lastBlock = TimeUtils.CurrentTimeMillis();
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogCritical($"Fatal error in consensus, exiting: {e}");
                Environment.Exit(1);
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Terminate()
        {
            _terminated = true;
        }

        /**
         * Initialize consensus broadcaster for era if necessary. May throw if era is too far in the past or future
         */
        [MethodImpl(MethodImplOptions.Synchronized)]
        private EraBroadcaster? EnsureEra(long era)
        {
            if (era <= 0) return null;
            if (_eras.ContainsKey(era)) return _eras[era];
            Logger.LogDebug($"Creating broadcaster for era {era}");
            if (_terminated)
            {
                Logger.LogWarning($"Broadcaster for era {era} not created since consensus is terminated");
                return null;
            }

            Logger.LogDebug($"Created broadcaster for era {era}");
            return _eras[era] = new EraBroadcaster(era, _messageDeliverer, _validatorManager, _privateWallet, _validatorAttendanceRepository);
        }
    }
}