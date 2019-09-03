﻿using System;
using System.Linq;
using NUnit.Framework;
using Org.BouncyCastle.Crypto.Digests;
using Phorkus.Consensus;
using Phorkus.Consensus.CommonCoin.ThresholdSignature;
using Phorkus.Consensus.Messages;
using Phorkus.Consensus.ReliableBroadcast;
using Phorkus.Consensus.TPKE;
using Phorkus.Crypto.MCL.BLS12_381;
using Phorkus.Proto;

namespace Phorkus.ConsensusTest
{
    [TestFixture]
    public class ReliableBroadcastTest
    {
        private const int N = 8;
        private const int F = 2;
        private int sender = 0;
            
        private PlayerSet _playerSet;
        private IConsensusProtocol[] _broadcasts;
        private IConsensusBroadcaster[] _broadcasters;
        private ProtocolInvoker<ReliableBroadcastId, EncryptedShare>[] _resultInterceptors;
        private Random _rnd;
        private IWallet[] _wallets;
        
        [SetUp]
        public void SetUp()
        {        
            _playerSet = new PlayerSet();
            _broadcasts = new IConsensusProtocol[N];
            _broadcasters = new IConsensusBroadcaster[N];
            _resultInterceptors = new ProtocolInvoker<ReliableBroadcastId, EncryptedShare>[N];
            _rnd = new Random();
            _wallets = new IWallet[N];
            
            Mcl.Init();
            for (var i = 0; i < N; ++i)
            {
                _wallets[i] = new Wallet(N, F);
                _broadcasters[i] = new BroadcastSimulator(i, _wallets[i], _playerSet, mixMessages: false);
                _resultInterceptors[i] = new ProtocolInvoker<ReliableBroadcastId, EncryptedShare>();
            }
                       
        }
        
        private void SetUpAllHonest()
        {
            for (uint i = 0; i < N; ++i)
            {
                _broadcasts[i] = new ReliableBroadcast(new ReliableBroadcastId(sender, 0), _wallets[i], _broadcasters[i]);
                _broadcasters[i].RegisterProtocols(new[] {_broadcasts[i], _resultInterceptors[i]});
            }
        }
        
        [Test]
        public void Run()
        {
//            ConsensusMessage consensusMessage = new ConsensusMessage();
//            MessageEnvelope envelope = new MessageEnvelope(consensusMessage);
//            _reliableBroadcast.ProcessMessage(envelope);
            var share = new EncryptedShare(G1.Generator, null, G2.Generator, sender);
            SetUpAllHonest();
            for (var i = 0; i < N; ++i)
            {
                _broadcasters[i].InternalRequest(new ProtocolRequest<ReliableBroadcastId, EncryptedShare>(
                    _resultInterceptors[i].Id, _broadcasts[i].Id as ReliableBroadcastId, i == sender ? share : null
                ));
            }

            for (var i = 0; i < N; ++i)
            {
                _broadcasts[i].WaitFinish();
            }

            for (var i = 0; i < N; ++i)
            {
                Assert.IsTrue(_broadcasts[i].Terminated, $"protocol {i} did not terminated");
            }
        }
        
    }
}