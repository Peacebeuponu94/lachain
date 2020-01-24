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
    public class MockReliableBroadcastTest
    {
        private int N = 10;
        private int F = 3;
        private int sender = 0;
            
        private DeliveryService _deliveryService;
        private IConsensusProtocol[] _broadcasts;
        private IConsensusBroadcaster[] _broadcasters;
        private ProtocolInvoker<ReliableBroadcastId, EncryptedShare>[] _resultInterceptors;
        private Random _rnd;
        private IWallet[] _wallets;
        
        [SetUp]
        public void SetUp()
        {        
            _deliveryService = new DeliveryService();
            _broadcasts = new IConsensusProtocol[N];
            _broadcasters = new IConsensusBroadcaster[N];
            _resultInterceptors = new ProtocolInvoker<ReliableBroadcastId, EncryptedShare>[N];
            _rnd = new Random();
            _wallets = new IWallet[N];
            
            Mcl.Init();
            for (var i = 0; i < N; ++i)
            {
                _wallets[i] = new Wallet(N, F);
                _broadcasters[i] = new BroadcastSimulator(i, _wallets[i], _deliveryService, mixMessages: false);
                _resultInterceptors[i] = new ProtocolInvoker<ReliableBroadcastId, EncryptedShare>();
            }
                       
        }
        
        private void SetUpAllHonest()
        {
            for (uint i = 0; i < N; ++i)
            {
                _broadcasts[i] = new MockReliableBroadcast(new ReliableBroadcastId(sender, 0), _wallets[i], _broadcasters[i]);
                _broadcasters[i].RegisterProtocols(new[] {_broadcasts[i], _resultInterceptors[i]});
            }
        }
        
        public void Run(int n, DeliveryServiceMode mode, double repeatProbability=.0)
        {
            N = n;
            SetUpAllHonest();
            _deliveryService.Mode = mode;
            _deliveryService.RepeatProbability = repeatProbability;
            
            var share = new EncryptedShare(G1.Generator, new byte[]{}, G2.Generator, sender);
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

        [Test]
        [Repeat(100)]
        public void TestRandom10()
        {
            Run(10, DeliveryServiceMode.TAKE_RANDOM);
        }
        
        [Test]
        [Repeat(100)]
        public void TestLast10()
        {
            Run(10, DeliveryServiceMode.TAKE_LAST);
        }
        
        [Test]
        [Repeat(100)]
        public void TestRandomWithRepeat10()
        {
            Run(10, DeliveryServiceMode.TAKE_RANDOM, 0.5);
        }
        
    }
}