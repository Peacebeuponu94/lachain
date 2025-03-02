﻿using System;
using System.IO;
using System.Reflection;
using Lachain.Core.Blockchain.VM;
using Lachain.Core.CLI;
using Lachain.Core.Config;
using Lachain.Core.DI;
using Lachain.Core.DI.Modules;
using Lachain.Core.DI.SimpleInjector;
using Lachain.Proto;
using Lachain.Storage.State;
using Lachain.Utility;
using Lachain.Utility.Utils;
using Lachain.UtilityTest;
using NUnit.Framework;

namespace Lachain.CoreTest.IntegrationTests
{
    [Ignore("Need top recompile contracts")]
    public class VirtualMachineTest
    {
        private IContainer? _container;

        public VirtualMachineTest()
        {
            var containerBuilder = new SimpleInjectorContainerBuilder(new ConfigManager(
                Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config.json"),
                new RunOptions()
            ));

            containerBuilder.RegisterModule<BlockchainModule>();
            containerBuilder.RegisterModule<ConfigModule>();
            containerBuilder.RegisterModule<StorageModule>();

            _container = containerBuilder.Build();
        }

        [SetUp]
        public void Setup()
        {
            _container?.Dispose();
            TestUtils.DeleteTestChainData();

            var containerBuilder = new SimpleInjectorContainerBuilder(new ConfigManager(
                Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config.json"),
                new RunOptions()
            ));

            containerBuilder.RegisterModule<BlockchainModule>();
            containerBuilder.RegisterModule<ConfigModule>();
            containerBuilder.RegisterModule<StorageModule>();

            _container = containerBuilder.Build();
        }

        [TearDown]
        public void Teardown()
        {
            _container?.Dispose();
            TestUtils.DeleteTestChainData();
        }


        [Test]
        public void Test_VirtualMachine_InvokeMulmodContract()
        {
            var assembly = Assembly.GetExecutingAssembly();

            var resourceMulmod = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.Mulmod.wasm");
            if (resourceMulmod is null)
                Assert.Fail("Failed to read script from resources");
            var mulmodCode = new byte[resourceMulmod!.Length];
            resourceMulmod!.Read(mulmodCode, 0, (int)resourceMulmod!.Length);

            var resourceNewtonRaphson = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.NewtonRaphson.wasm");
            if (resourceNewtonRaphson is null)
                Assert.Fail("Failed to read script from resources");
            var newtonRaphsonCode = new byte[resourceNewtonRaphson!.Length];
            resourceNewtonRaphson!.Read(newtonRaphsonCode, 0, (int)resourceNewtonRaphson!.Length);

            var stateManager = _container.Resolve<IStateManager>();

            // NewtonRaphson
            var newtonRaphsonAddress = "0x9531d91b4bc5df4ddc11bc864875c8ad6425c36b".HexToBytes().ToUInt160();
            var newtonRaphsonContract = new Contract
            (
                newtonRaphsonAddress,
                newtonRaphsonCode
            );
            if (!VirtualMachine.VerifyContract(newtonRaphsonContract.ByteCode,  true))
                throw new Exception("Unable to validate smart-contract code");

            // Mulmod
            var mulmodAddress = UInt160Utils.Zero;
            var mulmodContract = new Contract
            (
                mulmodAddress,
                mulmodCode
            );
            if (!VirtualMachine.VerifyContract(mulmodContract.ByteCode,  true))
                throw new Exception("Unable to validate smart-contract code");

            var snapshot = stateManager.NewSnapshot();
            snapshot.Contracts.AddContract(UInt160Utils.Zero, newtonRaphsonContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, mulmodContract);
            stateManager.Approve();

            for (var i = 0; i < 1; ++i)
            {
                var currentTime = TimeUtils.CurrentTimeMillis();
                var currentSnapshot = stateManager.NewSnapshot();
                currentSnapshot.Balances.AddBalance(UInt160Utils.Zero, 100.ToUInt256().ToMoney());
                var transactionReceipt = new TransactionReceipt();
                transactionReceipt.Transaction = new Transaction();
                transactionReceipt.Transaction.Value = 0.ToUInt256();

                var sender = UInt160Utils.Zero;
                var block = new Block();
                block.Header = new BlockHeader();
                block.Header.Index = 0;
                block.Hash = 0.ToUInt256();
                block.Timestamp = (ulong)DateTimeOffset.Now.ToUnixTimeSeconds();
                currentSnapshot.Blocks.AddBlock(block);
                var context = new InvocationContext(sender, currentSnapshot, transactionReceipt);

                {
                    Console.WriteLine($"\nMulmod: mulDivLachain(...)");
                    var input = "0xaa9a091200000000fffffffffffffffffffffffffffff830000000000000000000000000000000000000000000000000000000000000000001487bee1c17ddb45ce0bae0000000000000000000000000000000000000000101487bee1c17ddb45ce0bae0".HexToBytes();
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(mulmodContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }

                stateManager.Approve();
            exit_mark:
                var elapsedTime = TimeUtils.CurrentTimeMillis() - currentTime;
                Console.WriteLine("Elapsed Time: " + elapsedTime + "ms");
            }
        }

        [Test]
        public void Test_VirtualMachine_InvokeUniswapContract()
        {
            var assembly = Assembly.GetExecutingAssembly();

            var resourceLRC20_1 = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.LRC20_1.wasm");
            if (resourceLRC20_1 is null)
                Assert.Fail("Failed to read script from resources");
            var lrc20_1Code = new byte[resourceLRC20_1!.Length];
            resourceLRC20_1!.Read(lrc20_1Code, 0, (int)resourceLRC20_1!.Length);

            var resourceLRC20_2 = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.LRC20_2.wasm");
            if (resourceLRC20_2 is null)
                Assert.Fail("Failed to read script from resources");
            var lrc20_2Code = new byte[resourceLRC20_2!.Length];
            resourceLRC20_2!.Read(lrc20_2Code, 0, (int)resourceLRC20_2!.Length);

            var resourceFactory = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.UniswapV3Factory.wasm");
            if (resourceFactory is null)
                Assert.Fail("Failed to read script from resources");
            var factoryCode = new byte[resourceFactory!.Length];
            resourceFactory!.Read(factoryCode, 0, (int)resourceFactory!.Length);

            var resourceSwap = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.Swap.wasm");
            if (resourceSwap is null)
                Assert.Fail("Failed to read script from resources");
            var swapCode = new byte[resourceSwap!.Length];
            resourceSwap!.Read(swapCode, 0, (int)resourceSwap!.Length);

            var resourceTickMath = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.TickMath.wasm");
            if (resourceTickMath is null)
                Assert.Fail("Failed to read script from resources");
            var tickMathCode = new byte[resourceTickMath!.Length];
            resourceTickMath!.Read(tickMathCode, 0, (int)resourceTickMath!.Length);

            var resourceSwapMath = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.SwapMath.wasm");
            if (resourceSwapMath is null)
                Assert.Fail("Failed to read script from resources");
            var swapMathCode = new byte[resourceSwapMath!.Length];
            resourceSwapMath!.Read(swapMathCode, 0, (int)resourceSwapMath!.Length);

            var resourceFullMath = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.FullMath.wasm");
            if (resourceFullMath is null)
                Assert.Fail("Failed to read script from resources");
            var fullMathCode = new byte[resourceFullMath!.Length];
            resourceFullMath!.Read(fullMathCode, 0, (int)resourceFullMath!.Length);

            var resourceNewtonRaphson = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.NewtonRaphson.wasm");
            if (resourceNewtonRaphson is null)
                Assert.Fail("Failed to read script from resources");
            var newtonRaphsonCode = new byte[resourceNewtonRaphson!.Length];
            resourceNewtonRaphson!.Read(newtonRaphsonCode, 0, (int)resourceNewtonRaphson!.Length);

            var resourceUniswapV3PoolActions = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.UniswapV3PoolActions.wasm");
            if (resourceUniswapV3PoolActions is null)
                Assert.Fail("Failed to read script from resources");
            var uniswapV3PoolActionsCode = new byte[resourceUniswapV3PoolActions!.Length];
            resourceUniswapV3PoolActions!.Read(uniswapV3PoolActionsCode, 0, (int)resourceUniswapV3PoolActions!.Length);

            var stateManager = _container.Resolve<IStateManager>();

            // LRC20_1
            var lrc20_1Address = "0xfd893ce89186fc6861d339cb6ab5d75458e3daf3".HexToBytes().ToUInt160();
            var lrc20_1Contract = new Contract
            (
                lrc20_1Address,
                lrc20_1Code
            );
            if (!VirtualMachine.VerifyContract(lrc20_1Contract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // LRC20_2
            var lrc20_2Address = "0xfd893ce89186fc5451d339cb6ab5d75458e3daf3".HexToBytes().ToUInt160();
            var lrc20_2Contract = new Contract
            (
                lrc20_2Address,
                lrc20_2Code
            );
            if (!VirtualMachine.VerifyContract(lrc20_2Contract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // Factory
            var factoryAddress = "0x6bc32575adc8754886dc283c2c8ac54b1bd93195".HexToBytes().ToUInt160();
            var factoryContract = new Contract
            (
                factoryAddress,
                factoryCode
            );
            if (!VirtualMachine.VerifyContract(factoryContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // Swap
            var swapAddress = "0x6bc32575adc8754886bc145c2c8ac54b1bd93195".HexToBytes().ToUInt160();
            var swapContract = new Contract
            (
                swapAddress,
                swapCode
            );
            if (!VirtualMachine.VerifyContract(swapContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // TickMath
            var tickMathAddress = "0x9531d91b4bc58a2c5c14bc864875c8ad6425c36b".HexToBytes().ToUInt160();
            var tickMathContract = new Contract
            (
                tickMathAddress,
                tickMathCode
            );
            if (!VirtualMachine.VerifyContract(tickMathContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // SwapMath
            var swapMathAddress = "0x9531d91b4bc58a4d5c14bc864875c8ad6425c36b".HexToBytes().ToUInt160();
            var swapMathContract = new Contract
            (
                swapMathAddress,
                swapMathCode
            );
            if (!VirtualMachine.VerifyContract(swapMathContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // FullMath
            var fullMathAddress = "0x9531d91b4bc58a4ddc11bc864875c8ad6425c36b".HexToBytes().ToUInt160();
            var fullMathContract = new Contract
            (
                fullMathAddress,
                fullMathCode
            );
            if (!VirtualMachine.VerifyContract(fullMathContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // NewtonRaphson
            var newtonRaphsonAddress = "0x9531d91b4bc5df4ddc11bc864875c8ad6425c36b".HexToBytes().ToUInt160();
            var newtonRaphsonContract = new Contract
            (
                newtonRaphsonAddress,
                newtonRaphsonCode
            );
            if (!VirtualMachine.VerifyContract(newtonRaphsonContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // UniswapV3PoolActions
            var uniswapV3PoolActionsAddress = "0x6bc32564fff8754886bc11dc4d8ac54b1bd93195".HexToBytes().ToUInt160();
            var uniswapV3PoolActionsContract = new Contract
            (
                uniswapV3PoolActionsAddress,
                uniswapV3PoolActionsCode
            );
            if (!VirtualMachine.VerifyContract(uniswapV3PoolActionsContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            var recipientAddress = "0xfdcd3ce43186fc6861d339cb6ab5d75458e3daf3".HexToBytes().ToUInt160();
            var poolAddress = "0x5c00bd4aca04a9057c09b20b05f723f2e23deb65".HexToBytes().ToUInt160();

            var snapshot = stateManager.NewSnapshot();
            snapshot.Contracts.AddContract(UInt160Utils.Zero, lrc20_1Contract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, lrc20_2Contract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, factoryContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, swapContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, tickMathContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, swapMathContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, fullMathContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, newtonRaphsonContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, uniswapV3PoolActionsContract);
            stateManager.Approve();

            for (var i = 0; i < 1; ++i)
            {
                var currentTime = TimeUtils.CurrentTimeMillis();
                var currentSnapshot = stateManager.NewSnapshot();
                currentSnapshot.Balances.AddBalance(UInt160Utils.Zero, 100.ToUInt256().ToMoney());
                var transactionReceipt = new TransactionReceipt();
                transactionReceipt.Transaction = new Transaction();
                transactionReceipt.Transaction.Value = 0.ToUInt256();

                var block = new Block();
                block.Header = new BlockHeader();
                block.Header.Index = 0;
                block.Hash = 0.ToUInt256();
                block.Timestamp = (ulong)DateTimeOffset.Now.ToUnixTimeSeconds();
                currentSnapshot.Blocks.AddBlock(block);
                var context = new InvocationContext(recipientAddress, currentSnapshot, transactionReceipt);

                {
                    Console.WriteLine($"\nLRC20_1: mint({recipientAddress.ToHex()},{1000})");
                    var input = ContractEncoder.Encode("mint(address,uint256)", recipientAddress, 1000.ToUInt256());
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_1Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_1: approve({swapAddress.ToHex()},{1000})");
                    var input = ContractEncoder.Encode("approve(address,uint256)", swapAddress, 1000.ToUInt256());
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_1Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_2: mint({recipientAddress.ToHex()},{1000})");
                    var input = ContractEncoder.Encode("mint(address,uint256)", recipientAddress, 1000.ToUInt256());
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_2Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_2: approve({swapAddress.ToHex()},{1000})");
                    var input = ContractEncoder.Encode("approve(address,uint256)", swapAddress, 1000.ToUInt256());
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_2Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nFactory: init()");
                    var input = ContractEncoder.Encode("init()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(factoryContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nFactory: createPool({lrc20_1Address.ToHex()},{lrc20_2Address.ToHex()},{500})");
                    var input = ContractEncoder.Encode("createPool(address,address,uint24)", lrc20_1Address, lrc20_2Address, 500.ToUInt256());
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(factoryContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nSwap: initialize({poolAddress.ToHex()},79228162514264337593543950336)");
                    var input = ContractEncoder.Encode("initialize(address,uint160)", poolAddress, "0x00000000000000000000000001".HexToBytes().ToUInt256(true));
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(swapContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_1: balanceOf({recipientAddress.ToHex()})");
                    var input = ContractEncoder.Encode("balanceOf(address)", recipientAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_1Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_1: balanceOf({poolAddress.ToHex()})");
                    var input = ContractEncoder.Encode("balanceOf(address)", poolAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_1Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_2: balanceOf({recipientAddress.ToHex()})");
                    var input = ContractEncoder.Encode("balanceOf(address)", recipientAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_2Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_2: balanceOf({poolAddress.ToHex()})");
                    var input = ContractEncoder.Encode("balanceOf(address)", poolAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_2Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nSwap: mint({poolAddress.ToHex()},{recipientAddress.ToHex()},{-100}, {100}, {2000})");
                    var input = "0x7b4f53270000000000000000000000005c00bd4aca04a9057c09b20b05f723f2e23deb65000000000000000000000000f3dae35854d7b56acb39d36168fc8631e43ccdfdffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff9c000000000000000000000000000000000000000000000000000000000000006400000000000000000000000000000000000000000000000000000000000007d0".HexToBytes();//ContractEncoder.Encode("mint(address,address,int24,int24,uint128)", poolAddress, recipientAddress, 100.ToUInt256(), 100.ToUInt256(), 2000.ToUInt256());
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(swapContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_1: balanceOf({recipientAddress.ToHex()})");
                    var input = ContractEncoder.Encode("balanceOf(address)", recipientAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_1Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_1: balanceOf({poolAddress.ToHex()})");
                    var input = ContractEncoder.Encode("balanceOf(address)", poolAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_1Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_2: balanceOf({recipientAddress.ToHex()})");
                    var input = ContractEncoder.Encode("balanceOf(address)", recipientAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_2Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_2: balanceOf({poolAddress.ToHex()})");
                    var input = ContractEncoder.Encode("balanceOf(address)", poolAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_2Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nSwap: swapExact1For0({poolAddress.ToHex()},{5},{recipientAddress.ToHex()},1461446703485210103287273052203988822378723970341");
                    var input = ContractEncoder.Encode("swapExact1For0(address,uint256,address,uint160)", poolAddress, 5.ToUInt256(), recipientAddress, "0xFFFD8963EFD1FC6A506488495D951D5263988D25".HexToBytes().ToUInt256(true));
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(swapContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_1: balanceOf({recipientAddress.ToHex()})");
                    var input = ContractEncoder.Encode("balanceOf(address)", recipientAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_1Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_1: balanceOf({poolAddress.ToHex()})");
                    var input = ContractEncoder.Encode("balanceOf(address)", poolAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_1Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_2: balanceOf({recipientAddress.ToHex()})");
                    var input = ContractEncoder.Encode("balanceOf(address)", recipientAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_2Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nLRC20_2: balanceOf({poolAddress.ToHex()})");
                    var input = ContractEncoder.Encode("balanceOf(address)", poolAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(lrc20_2Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }

                stateManager.Approve();
            exit_mark:
                var elapsedTime = TimeUtils.CurrentTimeMillis() - currentTime;
                Console.WriteLine("Elapsed Time: " + elapsedTime + "ms");
            }
        }


        [Test]
        public void Test_VirtualMachine_InvokeContract()
        {
            var stateManager = _container.Resolve<IStateManager>();

            // UniswapFactory
            var uniswapFactoryAddress = UInt160Utils.Zero;
            var uniswapFactoryContract = new Contract
            (
                uniswapFactoryAddress,
                "0061736D0100000001480B60037F7F7F0060027F7F0060057F7F7F7F7F017F60017F006000017F60000060017F017F60047E7E7E7F017F60077E7E7E7E7E7E7F017F60037E7E7E017F60057E7E7E7E7F017F02BD010903656E761063727970746F5F6B656363616B323536000003656E760C6C6F61645F73746F72616765000103656E760F696E766F6B655F636F6E7472616374000203656E760A7365745F72657475726E000103656E760B73797374656D5F68616C74000303656E760C736176655F73746F72616765000103656E76156765745F7472616E736665727265645F66756E6473000303656E760D6765745F63616C6C5F73697A65000403656E760F636F70795F63616C6C5F76616C75650000030C0B01050600000708090A07050405017001010105030100020608017F01418080040B071202066D656D6F7279020005737461727400130A9F1C0B1C00034020004200370300200041086A21002001417F6A22010D000B0B2E004100410036028080044100410036028480044100410036028C800441003F0041107441F0FF7B6A36028880040BA60101047F418080042101024003400240200128020C0D002001280208220220004F0D020B200128020022010D000B41002101410028020821020B02402002200041076A41787122036B22024118490D00200120036A41106A22002001280200220436020002402004450D00200420003602040B2000200241706A3602082000410036020C2000200136020420012000360200200120033602080B2001410136020C200141106A0B2D002000411F6A21000340200120002D00003A0000200141016A21012000417F6A21002002417F6A22020D000B0B2D002001411F6A21010340200120002D00003A00002001417F6A2101200041016A21002002417F6A22020D000B0BDC0101027F230041E0006B22042400200441406A22052400200541286A2001370300200541206A2000370300200541306A20023E0200200541186A4200370300200542003703102005420037030820054202370300200541382004220441C0006A1000200441206A41186A200441C0006A41186A2903003703002004200441C0006A41106A2903003703302004200441C0006A41086A29030037032820042004290340370320200441206A200410012003200441086A290300370308200320042903003703002003200441106A3502003E0210200441E0006A240041000BCB0B04047F047E027F027E2300220721080240024002402000200242FFFFFFFF0F8384200184500D00200741406A220722092400200741286A2001370300200741206A2000370300200741306A20023E0200200741186A4200370300200742003703102007420037030820074202370300200941606A2209220A240020074138200910002009290300210B200941086A290300210C200941106A290300210D200941186A290300210E200A41606A220722092400200741186A200E3703002007200D3703102007200C3703082007200B370300200941606A2209220F24002007200910012009290300200941106A35020084200941086A2903008450450D014124100B220A41E6A68B1C360200200F41606A2207220924002007200137030820072000370300200720023E02102007200A41046A4114100D200941606A2209220724002009200437030820092003370300200920053E0210200741606A2207220F2400200741186A4200370300200742003703102007420037030820074200370300200F41706A220F22102400200F420037030020094124200A2007200F1002450D0241004100100341011004000B41004100100341011004000B41004100100341011004000B201041406A220722092400200741286A2001370300200741206A2000370300200741306A20023E0200200741186A4200370300200742003703102007420037030820074202370300200941606A2209220A240020074138200910002009290300210B200941086A290300210C200941106A290300210D200941186A290300210E200A41606A220922072400200941186A200E3703002009200D3703102009200C3703082009200B370300200741606A2207220A24002007200437030820072003370300200720053E0210200A41606A220A220F2400200A41041009200A2007290308370308200A2007290300370300200A20073502103E02102009200A1005200F41406A220722092400200741286A2004370300200741206A2003370300200741306A20053E0200200741186A4200370300200742003703102007420037030820074203370300200941606A2209220A240020074138200910002009290300210B200941086A290300210C200941106A290300210D200941186A290300210E200A41606A220922072400200941186A200E3703002009200D3703102009200C3703082009200B370300200741606A2207220A24002007200137030820072000370300200720023E0210200A41606A220A220F2400200A41041009200A2007290308370308200A2007290300370300200A20073502103E02102009200A1005200F41606A220722092400200741186A4200370300200742003703102007420037030820074201370300200941606A2209220A2400200720091001200941186A2903002111200941106A290300210D200941086A290300210E2009290300210B200A41606A220722092400200741186A4200370300200742003703102007420037030820074201370300200941606A2209220A2400200941186A2011200D200B42017C220C200B54220F200E200FAD7C2212200E54200C200B5A1BAD7C220B200D54AD7C220D3703002009200B370310200920123703082009200C370300200720091005200A41406A220722092400200741386A200D370300200741306A200B370300200741286A2012370300200741206A200C370300200741186A4200370300200742003703102007420037030820074204370300200941606A2209220A2400200741C000200910002009290300210B200941086A290300210C200941106A290300210D200941186A290300210E200A41606A220922072400200941186A200E3703002009200D3703102009200C3703082009200B370300200741606A2207220A24002007200137030820072000370300200720023E0210200A41606A220A2400200A41041009200A2007290308370308200A2007290300370300200A20073502103E02102009200A10052006200437030820062003370300200620053E02102008240041000B8B0201047F230041C0006B220324002003220441386A4200370300200442003703302004420037032820044200370320200441206A20041001024002402004290300200441106A35020084200441086A290300844200520D002000200242FFFFFFFF0F83842001844200520D0141004100100341011004000B41004100100341011004000B200341606A220522032400200541186A4200370300200542003703102005420037030820054200370300200341606A2203220624002003200137030820032000370300200320023E0210200641606A220624002006410410092006200329030837030820062003290300370300200620033502103E0210200520061005200441C0006A240041000BE70101027F230041E0006B22052400200541406A22062400200641386A2003370300200641306A2002370300200641286A2001370300200641206A2000370300200641186A4200370300200642003703102006420037030820064204370300200641C0002005220541C0006A1000200541206A41186A200541C0006A41186A2903003703002005200541C0006A41106A2903003703302005200541C0006A41086A29030037032820052005290340370320200541206A200510012004200541086A290300370308200420052903003703002004200541106A3502003E0210200541E0006A240041000BDC0101027F230041E0006B22042400200441406A22052400200541286A2001370300200541206A2000370300200541306A20023E0200200541186A4200370300200542003703102005420037030820054203370300200541382004220441C0006A1000200441206A41186A200441C0006A41186A2903003703002004200441C0006A41106A2903003703302004200441C0006A41086A29030037032820042004290340370320200441206A200410012003200441086A290300370308200320042903003703002003200441106A3502003E0210200441E0006A240041000BCD0602047F037E230041A0026B22002400200041086A100602400240024002400240024002400240024002400240024002402000290308200041186A29030084200041106A290300200041206A29030084844200520D00100A41001007220136020441002001100B22023602084100200120021008200141034D0D0C4100200228020022033602002001417C6A2101200241046A21020240024002400240200341F7C58BBB014A0D00200341AACB99857C460D02200341D394FEF100460D010C100B0240200341F8C58BBB01460D00200341D9EE91C003460D0320034186E4FF9506470D1020014120490D052002200041286A4118100C2000290328200041306A290300200041386A350200200041C0006A100E450D0641004100100341011004000B20014120490D062002200041D8006A4118100C2001AD423F580D07200041D8006A41106A3502002104200041D8006A41086A290300210520002903582106200241206A200041F0006A4118100C2006200520042000290370200041F0006A41086A290300200041F0006A41106A35020020004188016A100F450D0841004100100341011004000B20014120490D082002200041A0016A4118100C20002903A001200041A8016A290300200041B0016A3502001010450D0941004100100341011004000B20014120490D092002200041B8016A4120100C20002903B801200041C0016A290300200041C8016A290300200041D0016A290300200041D8016A1011450D0A41004100100341011004000B20014120490D0A2002200041F0016A4118100C20002903F001200041F8016A29030020004180026A35020020004188026A1012450D0B41004100100341011004000B41004100100341011004000B200041A0026A240041030F0B200041C0006A4120100B22004114100D20004120100341001004000B200041A0026A240041030F0B200041A0026A240041030F0B20004188016A4120100B22004114100D20004120100341001004000B200041A0026A240041030F0B41004100100341001004000B200041A0026A240041030F0B200041D8016A4120100B22004114100D20004120100341001004000B200041A0026A240041030F0B20004188026A4120100B22004114100D20004120100341001004000B41004100100341011004000B00740970726F647563657273010C70726F6365737365642D62790105636C616E675431302E302E3120286769743A2F2F6769746875622E636F6D2F6C6C766D2F6C6C766D2D70726F6A65637420623661313733343336373838653638333239636335653965653066363531623630336136333765332900F403046E616D6501EC0314001063727970746F5F6B656363616B323536010C6C6F61645F73746F72616765020F696E766F6B655F636F6E7472616374030A7365745F72657475726E040B73797374656D5F68616C74050C736176655F73746F7261676506156765745F7472616E736665727265645F66756E6473070D6765745F63616C6C5F73697A65080F636F70795F63616C6C5F76616C756509085F5F627A65726F380A0B5F5F696E69745F686561700B085F5F6D616C6C6F630C0B5F5F62653332746F6C654E0D0B5F5F6C654E746F626533320E33736F6C3A3A66756E6374696F6E3A3A556E6973776170466163746F72793A3A67657445786368616E67655F5F616464726573730F3E736F6C3A3A66756E6374696F6E3A3A556E6973776170466163746F72793A3A63726561746545786368616E67655F5F616464726573735F616464726573731039736F6C3A3A66756E6374696F6E3A3A556E6973776170466163746F72793A3A696E697469616C697A65466163746F72795F5F616464726573731136736F6C3A3A66756E6374696F6E3A3A556E6973776170466163746F72793A3A676574546F6B656E5769746849645F5F75696E743235361230736F6C3A3A66756E6374696F6E3A3A556E6973776170466163746F72793A3A676574546F6B656E5F5F6164647265737313057374617274"
                    .HexToBytes()
            );
            if (!VirtualMachine.VerifyContract(uniswapFactoryContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // UniswapExchange
            var uniswapExchangeAddress = "0x6bc32575acb8754886dc283c2c8ac54b1bd93195".HexToBytes().ToUInt160();
            var uniswapExchangeContract = new Contract
            (
                uniswapExchangeAddress,
                "0061736D0100000001C6021A60017F0060027F7F0060057F7F7F7F7F017F6000017F60037F7F7F0060000060017F017F60047F7F7F7F0060047F7E7E7F0060037F7F7F017F60057E7E7E7E7F017F600D7E7E7E7E7E7E7E7E7E7E7E7E7F017F60037E7E7E017F60107E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7F017F60137E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7F017F60147E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7F017F601A7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7F017F600C7E7E7E7E7E7E7E7E7E7E7E7F017F60097E7E7E7E7E7E7E7E7F017F60127E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7F7F017F60177E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7E7F017F60087E7E7E7E7E7E7E7F017F600A7E7E7E7E7E7E7E7E7E7E017F600B7E7E7E7E7E7E7E7E7E7E7F017F60047E7E7E7F017F60077E7E7E7E7E7E7F017F029E020E03656E760B6765745F61646472657373000003656E760C6C6F61645F73746F72616765000103656E760F696E766F6B655F636F6E7472616374000203656E760F6765745F72657475726E5F73697A65000303656E7611636F70795F72657475726E5F76616C7565000403656E760A7365745F72657475726E000103656E760B73797374656D5F68616C74000003656E760A6765745F73656E646572000003656E760C736176655F73746F72616765000103656E76156765745F7472616E736665727265645F66756E6473000003656E761063727970746F5F6B656363616B323536000403656E760977726974655F6C6F67000103656E760D6765745F63616C6C5F73697A65000303656E760F636F70795F63616C6C5F76616C75650004032F2E040105060404070808090A0B0C0D0E0F10110E0B12121213100B1415160D0E160A0A1714150F1414180A190E11050405017001010105030100020608017F01418080040B071202066D656D6F72790200057374617274003B0AC7A7032E2E0002402002450D000340200020012D00003A0000200041016A2100200141016A21012002417F6A22020D000B0B0B1C00034020004200370300200041086A21002001417F6A22010D000B0B2E004100410036028080044100410036028480044100410036028C800441003F0041107441F0FF7B6A36028880040BA60101047F418080042101024003400240200128020C0D002001280208220220004F0D020B200128020022010D000B41002101410028020821020B02402002200041076A41787122036B22024118490D00200120036A41106A22002001280200220436020002402004450D00200420003602040B2000200241706A3602082000410036020C2000200136020420012000360200200120033602080B2001410136020C200141106A0B2D002000411F6A21000340200120002D00003A0000200141016A21012000417F6A21002002417F6A22020D000B0B2D002001411F6A21010340200120002D00003A00002001417F6A2101200041016A21002002417F6A22020D000B0BE80204077F017E037F027E200341027420006A417C6A21042003210502400340200522064101480D012006417F6A2105200428020021072004417C6A21042007450D000B0B200341027420016A417C6A21042003210502400340200522084101480D012008417F6A2105200428020021072004417C6A21042007450D000B0B024020034101480D002001417C6A21094100210A4200210B4100210C41002105410021010340200A20084E210D024002402005200520084822076A220E2001200A20064E6A22014B0D004200210F200B21100C010B2000200C200D6A4102746A21042009200520076A4102746A21054200210F200E21070340200F4280808080107C200F200B200535020020043502007E7C2210200B541B210F200441046A21042005417C6A21052010210B2007417F6A220720014A0D000B0B200C200D6A210C2002200A4102746A20103E02002010422088200F84210B200E2105200A41016A220A2003470D000B0B0B5301017E02402003450D000240200341C00071450D0020012003413F71AD862102420021010C010B200141C00020036BAD8820022003AD220486842102200120048621010B20002002370308200020013703000B5301017E02402003450D000240200341C00071450D0020022003413F71AD882101420021020C010B200241C00020036BAD8620012003AD220488842101200220048821020B20002002370308200020013703000B7E01017F200120006C220141086A10112203200036020420032000360200200341086A2100024002402002417F460D002001450D010340200020022D00003A0000200041016A2100200241016A21022001417F6A22010D000C020B0B2001450D000340200041003A0000200041016A21002001417F6A22010D000B0B20030BC60404037F037E037F017E230041206B220521062005240002400240024002402000200284200120038484500D00200541606A22052207240020051000200541106A350200210820052903002109200541086A290300210A41241011220B41F0C08A8C03360200200741606A2205220724002005200A37030820052009370300200520083E02102005200B41046A41141013200741606A220522072400200541186A4200370300200542003703102005420037030820054206370300200741606A2207220C2400200520071001200741106A350200210820072903002109200741086A290300210A200C41606A2207220524002007200A37030820072009370300200720083E0210200541606A2205220C2400200541186A4200370300200542003703102005420037030820054200370300200C41706A220C220D2400200C420037030020074124200B2005200C10020D011003220541086A101122072005360200200741046A2005360200200741086A220B4100200510042007280200411F4D0D02200B200641201012200641186A2903002108200641106A2903002109200641086A290300210A2006290300210E200D41606A2205240020002001200220034290CE00420042004200200E200A20092008200510192207450D03200641206A240020070F0B41004100100541011006000B41004100100541011006000B200641206A240041030F0B20042005290300370300200441186A200541186A2903003703002004200541106A2903003703102004200541086A290300370308200641206A240041000BD00E04047F047E017F057E230041B0016B220D210E200D24004100210F02402004200684200520078484500D002008200A842009200B8484420052210F0B02400240024002400240024002400240200F450D00200D41606A220D2210240020042005200620072000200120022003200D1023220F0D01200D41186A2903002104200D41106A2903002106200D41086A2903002105200D2903002107201041606A220D22102400200720052006200442E807420042004200200D1023220F0D02200D41186A2903002104200D41106A2903002105200D41086A2903002106200D2903002111201041606A220D2210240020082009200A200B2000200120022003200D1022220F0D03200D41186A2903002107200D41106A2903002100200D41086A2903002101200D2903002102201041606A220D22102400200220012000200742E507420042004200200D1023220F0D05200D2903002212200D41106A290300220B84200D41086A2903002213200D41186A2903002203842207844200510D04024002402012420185200B842007844200520D00200E41F0006A41186A4200370300200E4190016A41186A2004370300200E201137039001200E420037038001200E4200370378200E4200370370200E200637039801200E20053703A0010C010B024020112012852005200B8522078420062013852004200385220084844200520D00200E4190016A41186A4200370300200E41F0006A41186A4200370300200E42003703A001200E420037039801200E420137039001200E420037038001200E4200370378200E42003703700C010B0240024002402011200584200620048484500D00201120125A200620135A20062013511B2005200B5A200420035A20042003511B2007200084501B0D010B200E4190016A41186A4200370300200E41F0006A41186A2004370300200E42003703A001200E420037039801200E420037039001200E2011370370200E20063703780C010B42002114200E41E0006A200B2003200379200B7942C0007C20034200521B20137920127942C0007C20134200521B4280017C200B2003844200521B20047920057942C0007C20044200521B20067920117942C0007C20064200521B4280017C20052004844200521B7DA7220D1015200E41306A20122013418001200D6B220F1016200E41C0006A20122013200D41807F6A22151015200E41D0006A20122013200D1015200E41206A4201420020151015200E41106A42014200200F1016200E42014200200D1015200E41106A41086A290300200E41206A41086A290300200D41800149220F1B4200200D1B2108200E290310200E290320200F1B4200200D1B2109200E41086A2903004200200F1B210A200E2903004200200F1B21160240200E2903504200200F1B2202201158200E41D0006A41086A2903004200200F1B220020065820002006511B200E290360200E29033084200E290340200F1B200B200D1B2201200558200E41E0006A41086A290300200E41306A41086A29030084200E41C0006A41086A290300200F1B2003200D1B220720045820072004511B2001200585200720048584501B0D002016420188200A423F8684211620024201882000423F86842102200A4201882009423F8684210A20004201882001423F8684210020094201882008423F8684210920014201882007423F8684210120084201882108200742018821070B420021174200211842002119024003402011201254200620135420062013511B2005200B54200420035420042003511B2005200B85200420038584501B0D010240201120025A200620005A2006200051220D1B200520015A200420075A20042007511B2005200185200420078584501B450D00200420077D2005200154AD7D200520017D22052011200254220F2006200054200D1BAD221A54AD7D2104200620007D200FAD7D210620142016842114201120027D21112017200A84211720182009842118201920088421192005201A7D21050B2016420188200A423F8684211620024201882000423F86842102200A4201882009423F8684210A20004201882001423F8684210020094201882008423F8684210920014201882007423F8684210120084201882108200742018821070C000B0B200E4190016A41186A2019370300200E41F0006A41186A2004370300200E201437039001200E2011370370200E201737039801200E2006370378200E20183703A0010B200E2005370380010B4101450D06200E4190016A41186A2903002104200E4190016A41106A2903002106200E4190016A41086A2903002105200E290390012107201041606A220D240020072005200620044201420042004200200D1024220F450D07200E41B0016A2400200F0F0B41004100100541011006000B200E41B0016A2400200F0F0B200E41B0016A2400200F0F0B200E41B0016A2400200F0F0B41004100100541011006000B200E41B0016A2400200F0F0B200E41B0016A240041000F0B200C200D290300370300200C41186A200D41186A290300370300200C200D41106A290300370310200C200D41086A290300370308200E41B0016A240041000BB50702057F037E230041C0006B220324002003220441206A41186A4200370300200442003703302004420037032820044207370320200441206A20041001410021054100210602402004290300200441106A35020084200441086A290300844200520D00200341606A220622032400200641186A4200370300200642003703102006420037030820064206370300200341606A2207220324002006200710012007290300200741106A35020084200741086A290300845021060B02402006450D002000200242FFFFFFFF0F838420018442005221050B02402005450D00200341606A22032205240020031007200341106A350200210820032903002109200341086A290300210A200541606A220522032400200541186A4200370300200542003703102005420037030820054207370300200341606A2203220624002003200A37030820032009370300200320083E0210200641606A22062207240020064104100F2006200329030837030820062003290300370300200620033502103E0210200520061008200741606A220522032400200541186A4200370300200542003703102005420037030820054206370300200341606A2203220624002003200137030820032000370300200320023E0210200641606A22062207240020064104100F2006200329030837030820062003290300370300200620033502103E0210200520061008200741606A220322052400200341186A4200370300200342003703102003420037030820034203370300200541606A220522062400200541186A42A0E085BBB7AE9AB7D500370300200542808080808080C098D6003703102005420037030820054200370300200320051008200641606A220322052400200341186A4200370300200342003703102003420037030820034204370300200541606A220522062400200541186A428080C4B1D5A592A7D500370300200542003703102005420037030820054200370300200320051008200641606A220322052400200341186A4200370300200342003703102003420037030820034205370300200541606A22052400200541186A4200370300200542003703102005420037030820054212370300200320051008200441C0006A240041000F0B410F4101410010172205280200413F6A41607141246A22071011220441A0F38DC600360200200341706A220322062400200341203602002003200441046A4104101320052802002103200641706A22062400200620033602002006200441246A41041013200441C4006A200541086A2003100E20042007100541011006000B9A0202037F037E230041206B221024002010221141086A1000410021120240200C201129030885200E201141186A3502008542FFFFFFFF0F8384200D201141106A2903008584500D00200C200E42FFFFFFFF0F8384200D8442005221120B024002402012450D00201041606A22102212240020101007201041086A290300211320102903002114201041106A3502002115201241606A221024002000200120022003200420052006200720082009200A200B201420132015200C200D200E2010101C2212450D01201141206A240020120F0B41004100100541011006000B200F2010290300370300200F41186A201041186A290300370300200F201041106A290300370310200F201041086A290300370308201141206A240041000BC10904027F037E037F017E230041D0016B22132400201322144198016A100020144198016A41106A350200211520144198016A41086A2903002116201429039801211741241011221841F0C08A8C0336020020142016370388012014201737038001201420153E02900120144180016A201841046A41141013201441E0006A41186A4200370300201442003703702014420037036820144206370360201441E0006A201441C0006A1001201441086A41186A4200370300201442003703182014420037031020144200370308201442003703002014201441C0006A41086A2903003703342014201429034037032C2014201441C0006A41106A3502003E023C024002400240024002400240024002402014412C6A41242018201441086A201410020D001003221841086A101122192018360200201941046A2018360200201941086A221A4100201810042019280200411F4D0D01201A201441B0016A41201012201441B0016A41186A2903002115201441B0016A41106A2903002116201441B0016A41086A290300211720142903B001211B201341606A2218221324002000200120022003201B2017201620154290CE004200420042002018101922190D0220042018290300221B5A2005201841086A29030022165A20052016511B2006201841106A29030022175A2007201841186A29030022155A20072015511B2006201785200720158584501B450D03201341606A221922182400201920103703082019200F370300201920113E0210201841606A221822132400201841186A2003370300201820023703102018200137030820182000370300201341706A2213221A2400201342003703002019410041002018201310020D04201A41606A22182213240020181000201841106A350200210020182903002101201841086A290300210241E4001011221941A3F0CAEB7D360200201341606A2218221324002018200D3703082018200C3703002018200E3E02102018201941046A41141013201341606A2218221324002018200237030820182001370300201820003E02102018201941246A41141013201341606A221822132400201841186A201537030020182017370310201820163703082018201B3703002018201941C4006A41201013201341606A221822132400201841186A4200370300201842003703102018420037030820184206370300201341606A2213221A2400201820131001201341106A350200210020132903002101201341086A2903002102201A41606A2213221824002013200237030820132001370300201320003E0210201841606A2218221A2400201841186A4200370300201842003703102018420037030820184200370300201A41706A221A2400201A4200370300201341E40020192018201A10020D051003221841086A101122192018360200201941046A2018360200201941086A22134100201810042019280200411F4D0D06201341186A2903004200520D0741004100100541011006000B41004100100541011006000B201441D0016A240041030F0B201441D0016A240020190F0B41004100100541011006000B41004100100541011006000B41004100100541011006000B201441D0016A240041030F0B2012201B3703002012201637030820122017370310201241186A2015370300201441D0016A240041000BC00402057F067E230041B0016B221424004124101122154186E4FF9506360200201422162011370388012016201037038001201620123E02900120164180016A201541046A41141013201641E0006A41186A4200370300201642003703702016420037036820164207370360201641E0006A201641C0006A1001201641086A41186A4200370300201642003703182016420037031020164200370308201642003703002016201641C0006A41086A2903003703342016201629034037032C2016201641D0006A3502003E023C0240024002402016412C6A41242015201641086A201610020D001003221541086A101122172015360200201741046A2015360200201741086A22184100201510042017280200411F4D0D01201820164198016A4118101220164198016A41086A290300211220164198016A41106A35020021102016290398012111201441606A22152217240020151007201541086A29030021192015290300211A201541106A350200211B201741606A22152217240020151007201541086A290300211C2015290300211D201541106A350200211E201741606A221524002000200120022003200420052006200720082009200A200B200C200D200E200F201A2019201B201D201C201E2011201220102015101E2217450D02201641B0016A240020170F0B41004100100541011006000B201641B0016A240041030F0B20132015290300370300201341186A201541186A2903003703002013201541106A2903003703102013201541086A290300370308201641B0016A240041000BD30C04037F037E037F017E230041E0006B221A2400201A221B41086A10004100211C02402016201B290308852018201B41186A3502008542FFFFFFFF0F83842017201B41106A2903008584500D002016201842FFFFFFFF0F8384201784420052211C0B0240024002400240024002400240024002400240201C450D00201A41606A221A221C2400201A1000201A41106A350200211D201A290300211E201A41086A290300211F41241011222041F0C08A8C03360200201C41606A221A221C2400201A201F370308201A201E370300201A201D3E0210201A202041046A41141013201C41606A221A221C2400201A41186A4200370300201A4200370310201A4200370308201A4206370300201C41606A221C22212400201A201C1001201C41106A350200211D201C290300211E201C41086A290300211F202141606A221C221A2400201C201F370308201C201E370300201C201D3E0210201A41606A221A22212400201A41186A4200370300201A4200370310201A4200370308201A4200370300202141706A22212222240020214200370300201C41242020201A202110020D011003221A41086A1011221C201A360200201C41046A201A360200201C41086A22204100201A1004201C280200411F4D0D022020201B41206A41201012201B41206A41186A290300211D201B41206A41106A290300211E201B41206A41086A290300211F201B2903202123202241606A221A2220240020002001200220032023201F201E201D4290CE00420042004200201A1027221C0D03201A290300222320085A201A41086A290300221E20095A201E2009511B201A41106A290300221F200A5A201A41186A290300221D200B5A201D200B511B201F200A85201D200B8584501B450D04202041606A221A22202400201A1000201A41106A350200210B201A2903002109201A41086A290300210A41E4001011221C41A3F0CAEB7D360200202041606A221A22202400201A2011370308201A2010370300201A20123E0210201A201C41046A41141013202041606A221A22202400201A200A370308201A2009370300201A200B3E0210201A201C41246A41141013202041606A221A22202400201A41186A2003370300201A2002370310201A2001370308201A2000370300201A201C41C4006A41201013202041606A221A22202400201A41186A4200370300201A4200370310201A4200370308201A4206370300202041606A222022212400201A20201001202041106A350200210020202903002101202041086A2903002102202141606A2220221A24002020200237030820202001370300202020003E0210201A41606A221A22212400201A41186A4200370300201A4200370310201A4200370308201A4200370300202141706A22212222240020214200370300202041E400201C201A202110020D051003221A41086A1011221C201A360200201C41046A201A360200201C41086A22204100201A1004201C280200411F4D0D06202041186A2903004200510D0741E4001011221C41ADCBDDEE06360200202241606A221A22202400201A41186A2007370300201A2006370310201A2005370308201A2004370300201A201C41046A41201013202041606A221A22202400201A41186A200F370300201A200E370310201A200D370308201A200C370300201A201C41246A41201013202041606A221A22202400201A2014370308201A2013370300201A20153E0210201A201C41C4006A41141013202041606A2220221A24002020201737030820202016370300202020183E0210201A41606A221A22212400201A41186A201D370300201A201F370310201A201E370308201A2023370300202141706A2221240020214200370300202041E400201C201A202110020D081003221A41086A1011221C201A360200201C41046A201A360200201C41086A22204100201A1004201C280200411F4B0D09201B41E0006A240041030F0B41004100100541011006000B41004100100541011006000B201B41E0006A240041030F0B201B41E0006A2400201C0F0B41004100100541011006000B41004100100541011006000B201B41E0006A240041030F0B41004100100541011006000B41004100100541011006000B2020201B41C0006A41201012201941186A201B41C0006A41186A2903003703002019201B41D0006A2903003703102019201B41C0006A41086A2903003703082019201B290340370300201B41E0006A240041000BCE0202037F077E230041206B220C2400200C220D41086A10004100210E02402008200D29030885200A200D41186A3502008542FFFFFFFF0F83842009200D41106A2903008584500D002008200A42FFFFFFFF0F8384200984420052210E0B02400240200E450D00200C41606A220C220E2400200C1009200C41186A290300210F200C41106A2903002110200C41086A2903002111200C2903002112200E41606A220C220E2400200C1007200C41086A2903002113200C2903002114200C41106A3502002115200E41606A220C2400201220112010200F2000200120022003200420052006200720142013201520082009200A200C1020220E450D01200D41206A2400200E0F0B41004100100541011006000B200B200C290300370300200B41186A200C41186A290300370300200B200C41106A290300370310200B200C41086A290300370308200D41206A240041000BC90804027F037E037F057E230041D0016B22132400201322144198016A100020144198016A41106A350200211520144198016A41086A2903002116201429039801211741241011221841F0C08A8C0336020020142016370388012014201737038001201420153E02900120144180016A201841046A41141013201441E0006A41186A4200370300201442003703702014420037036820144206370360201441E0006A201441C0006A1001201441086A41186A4200370300201442003703182014420037031020144200370308201442003703002014201441C0006A41086A2903003703342014201429034037032C2014201441C0006A41106A3502003E023C024002400240024002400240024002402014412C6A41242018201441086A201410020D001003221841086A101122192018360200201941046A2018360200201941086A221A4100201810042019280200411F4D0D01201A201441B0016A41201012201441B0016A41186A2903002115201441B0016A41106A2903002116201441B0016A41086A290300211720142903B001211B201341606A2218221324004290CE0042004200420020002001200220032018102222190D02201841186A290300211C201841106A290300211D201841086A290300211E2018290300211F201341606A2218221A24002000200120022003201F201E201D201C201B2017201620152018102722190D032018290300220020045A201841086A290300221620055A20162005511B201841106A290300221720065A201841186A290300221520075A20152007511B2017200685201520078584501B450D0441C4001011221341A98BF0DC7B360200201A41606A221822192400201820103703082018200F370300201820113E02102018201341046A41141013201941606A221822192400201841186A20153703002018201737031020182016370308201820003703002018201341246A41201013201941606A221822192400201841186A4200370300201842003703102018420037030820184206370300201941606A2219221A2400201820191001201941106A350200210120192903002102201941086A2903002103201A41606A2219221824002019200337030820192002370300201920013E0210201841606A2218221A2400201841186A4200370300201842003703102018420037030820184200370300201A41706A221A2400201A4200370300201941C40020132018201A10020D051003221841086A101122192018360200201941046A2018360200201941086A22134100201810042019280200411F4D0D06201341186A2903004200520D0741004100100541011006000B41004100100541011006000B201441D0016A240041030F0B201441D0016A240020190F0B201441D0016A240020190F0B41004100100541011006000B41004100100541011006000B201441D0016A240041030F0B201220003703002012201637030820122017370310201241186A2015370300201441D0016A240041000B843808027F047E017F047E017F047E027F137E230041E0036B220D2400200D220E4188026A41186A4200370300200E420037039802200E420037039002200E420237038802200E4188026A200E41E8016A100102400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240200E2903E801220F200E41E8016A41106A290300221084200E41E8016A41086A2903002211200E41E8016A41186A29030022128484500D00024020002002842001200384844200510D00200D41606A220D22132400200D1009200D41186A2903002114200D41106A2903002115200D41086A2903002116200D2903002117201341606A220D221824004290CE004200420042002017201620152014200D102222130D02200D41186A2903002119200D41106A290300211A200D41086A290300211B200D290300211C201841606A220D22132400200D1000200D41106A3502002114200D2903002115200D41086A290300211641241011221841F0C08A8C03360200201341606A220D22132400200D2016370308200D2015370300200D20143E0210200D201841046A41141013201341606A220D22132400200D41186A4200370300200D4200370310200D4200370308200D4206370300201341606A2213221D2400200D20131001201341106A350200211420132903002115201341086A2903002116201D41606A2213220D24002013201637030820132015370300201320143E0210200D41606A220D221D2400200D41186A4200370300200D4200370310200D4200370308200D4200370300201D41706A221D221E2400201D4200370300201341242018200D201D10020D031003220D41086A10112213200D360200201341046A200D360200201341086A22184100200D10042013280200411F4D0D042018200E41A8026A41201012200E41A8026A41186A2903002114200E41A8026A41106A2903002115200E41A8026A41086A2903002116200E2903A8022117201E41606A220D22132400200D1009200D41186A290300211F200D41106A2903002120200D41086A2903002121200D2903002122201341606A220D22182400202220212020201F2017201620152014200D10232213450D05200E41E0036A240020130F0B41004100100541011006000B200D41606A220D22132400200D41186A4200370300200D4200370310200D4200370308200D4207370300201341606A221322182400200D201310014100210D4100211D02402013290300201341106A35020084201341086A290300844200510D00201841606A221322182400201341186A4200370300201342003703102013420037030820134206370300201841606A221D221824002013201D1001201D290300201D41106A35020084201D41086A29030084420052211D0B0240201D450D00201841606A220D22182400200D1009200D29030042FF93EBDC0356200D41086A29030022034200522003501B200D41106A2903002201420052200D41186A29030022034200522003501B2001200384501B210D0B200D450D04201841606A220D22132400200D41186A4200370300200D4200370310200D4200370308200D4206370300201341606A2213221D2400200D20131001201341106A350200210320132903002101201341086A29030021024124101122184186E4FF9506360200201D41606A220D22132400200D2002370308200D2001370300200D20033E0210200D201841046A41141013201341606A220D22132400200D41186A4200370300200D4200370310200D4200370308200D4207370300201341606A2213221D2400200D20131001201341106A350200210320132903002101201341086A2903002102201D41606A2213220D24002013200237030820132001370300201320033E0210200D41606A220D221D2400200D41186A4200370300200D4200370310200D4200370308200D4200370300201D41706A221D221E2400201D4200370300201341242018200D201D10020D0A1003220D41086A10112213200D360200201341046A200D360200201341086A22184100200D10042013280200411F4D0D0B2018200E41C8036A41181012200E41C8036A41086A2903002103200E41C8036A41106A3502002101200E2903C8032102201E41606A220D22132400200D10002002200D290300852001200D41106A35020085842003200D41086A290300858450450D0C201341606A220D22132400200D41186A4200370300200D4200370310200D4200370308200D4202370300201341606A221322182400201341186A4200370300201342003703102013420037030820134290CE00370300200D20131008201841606A220D22132400200D1007200D41106A3502002103200D2903002101200D41086A2903002102201341406A220D22132400200D41286A2002370300200D41206A2001370300200D41306A20033E0200200D41186A4200370300200D4200370310200D4200370308200D4200370300201341606A221322182400200D41382013100A20132903002103201341086A2903002101201341106A2903002102201341186A2903002100201841606A220D22132400200D41186A2000370300200D2002370310200D2001370308200D2003370300201341606A221322182400201341186A4200370300201342003703102013420037030820134290CE00370300200D20131008201841606A220D22132400200D1007200D41106A3502002103200D2903002101200D41086A2903002102201341606A220D22182400200D1000200D41106A3502002100200D2903002114200D41086A290300210F41E4001011221341A3F0CAEB7D360200201841606A220D22182400200D2002370308200D2001370300200D20033E0210200D201341046A41141013201841606A220D22182400200D200F370308200D2014370300200D20003E0210200D201341246A41141013201841606A220D22182400200D41186A2007370300200D2006370310200D2005370308200D2004370300200D201341C4006A41201013201841606A220D22182400200D41186A4200370300200D4200370310200D4200370308200D4206370300201841606A2218221D2400200D20181001201841106A350200210320182903002101201841086A2903002102201D41606A2218220D24002018200237030820182001370300201820033E0210200D41606A220D221D2400200D41186A4200370300200D4200370310200D4200370308200D4200370300201D41706A221D2400201D4200370300201841E4002013200D201D10020D0D1003220D41086A10112213200D360200201341046A200D360200201341086A22184100200D10042013280200411F4D0D0E201841186A2903004200510D0F200C4200370310200C4200370308200C4290CE00370300200C41186A4200370300200E41E0036A240041000F0B200E41E0036A240020130F0B41004100100541011006000B200E41E0036A240041030F0B201C201A84201B20198422238422244200510D0C200D41186A2903002114200D41106A2903002117200D41086A2903002116200D290300212102400240201C420185201A842023844200520D00200E41C8026A41186A4200370300200E41E8026A41186A2014370300200E20213703E802200E42003703D802200E42003703D002200E42003703C802200E20163703F002200E20173703F8020C010B02402021201C852017201A852215842016201B852014201985221F84844200520D00200E41E8026A41186A4200370300200E41C8026A41186A4200370300200E42003703F802200E42003703F002200E42013703E802200E42003703D802200E42003703D002200E42003703C8020C010B0240024002402021201784201620148484500D002021201C5A2016201B5A2016201B511B2017201A5A201420195A20142019511B2015201F84501B0D010B200E41E8026A41186A4200370300200E41C8026A41186A2014370300200E42003703F802200E42003703F002200E42003703E802200E20213703C802200E20163703D0020C010B42002125200E41D8016A201A2019201979201A7942C0007C20194200521B201B79201C7942C0007C201B4200521B4280017C201A2019844200521B20147920177942C0007C20144200521B20167920217942C0007C20164200521B4280017C20172014844200521B7DA7220D1015200E41A8016A201C201B418001200D6B22131016200E41B8016A201C201B200D41807F6A221D1015200E41C8016A201C201B200D1015200E4198016A42014200201D1015200E4188016A4201420020131016200E41F8006A42014200200D1015200E4188016A41086A290300200E4198016A41086A290300200D4180014922131B4200200D1B2126200E29038801200E2903980120131B4200200D1B2127200E41F8006A41086A290300420020131B2128200E290378420020131B21290240200E2903C801420020131B2222202158200E41C8016A41086A290300420020131B221F201658201F2016511B200E2903D801200E2903A80184200E2903B80120131B201A200D1B2220201758200E41D8016A41086A290300200E41A8016A41086A29030084200E41B8016A41086A29030020131B2019200D1B221520145820152014511B2020201785201520148584501B0D0020294201882028423F868421292022420188201F423F8684212220284201882027423F86842128201F4201882020423F8684211F20274201882026423F8684212720204201882015423F8684212020264201882126201542018821150B4200212A4200212B4200212C024003402021201C542016201B542016201B511B2017201A54201420195420142019511B2017201A85201420198584501B0D010240202120225A2016201F5A2016201F51220D1B201720205A201420155A20142015511B2017202085201420158584501B450D00201420157D2017202054AD7D201720207D2217202120225422132016201F54200D1BAD222D54AD7D21142016201F7D2013AD7D211620252029842125202120227D2121202A202884212A202B202784212B202C202684212C2017202D7D21170B20294201882028423F868421292022420188201F423F8684212220284201882027423F86842128201F4201882020423F8684211F20274201882026423F8684212720204201882015423F8684212020264201882126201542018821150C000B0B200E41E8026A41186A202C370300200E41C8026A41186A2014370300200E20253703E802200E20213703C802200E202A3703F002200E20163703D002200E202B3703F8020B200E20173703D8020B4101450D0D200E41E8026A41186A2903002114200E41E8026A41106A2903002115200E41E8026A41086A2903002116200E2903E8022117201841606A220D2218240020172016201520144201420042004200200D102422130D0E200D41186A290300212E200D41106A290300212F200D41086A2903002130200D2903002131201841606A220D22132400200D1009200D41186A2903002114200D41106A2903002115200D41086A2903002116200D2903002117201341606A220D221824002017201620152014200F201120102012200D102322130D1020244200510D0F200D41186A2903002114200D41106A2903002117200D41086A2903002116200D290300212102400240201C420185201A842023844200520D00200E4188036A41186A4200370300200E41A8036A41186A2014370300200E20213703A803200E420037039803200E420037039003200E420037038803200E20163703B003200E20173703B8030C010B02402021201C852017201A852215842016201B852014201985221F84844200520D00200E41A8036A41186A4200370300200E4188036A41186A4200370300200E42003703B803200E42003703B003200E42013703A803200E420037039803200E420037039003200E4200370388030C010B0240024002402021201784201620148484500D002021201C5A2016201B5A2016201B511B2017201A5A201420195A20142019511B2015201F84501B0D010B200E41A8036A41186A4200370300200E4188036A41186A2014370300200E42003703B803200E42003703B003200E42003703A803200E202137038803200E2016370390030C010B42002125200E41E8006A201A2019201979201A7942C0007C20194200521B201B79201C7942C0007C201B4200521B4280017C201A2019844200521B20147920177942C0007C20144200521B20167920217942C0007C20164200521B4280017C20172014844200521B7DA7220D1015200E41386A201C201B418001200D6B22131016200E41C8006A201C201B200D41807F6A221D1015200E41D8006A201C201B200D1015200E41286A42014200201D1015200E41186A4201420020131016200E41086A42014200200D1015200E41186A41086A290300200E41286A41086A290300200D4180014922131B4200200D1B2126200E290318200E29032820131B4200200D1B2127200E41086A41086A290300420020131B2128200E290308420020131B21290240200E290358420020131B2222202158200E41D8006A41086A290300420020131B221F201658201F2016511B200E290368200E29033884200E29034820131B201A200D1B2220201758200E41E8006A41086A290300200E41386A41086A29030084200E41C8006A41086A29030020131B2019200D1B221520145820152014511B2020201785201520148584501B0D0020294201882028423F868421292022420188201F423F8684212220284201882027423F86842128201F4201882020423F8684211F20274201882026423F8684212720204201882015423F8684212020264201882126201542018821150B4200212A4200212B4200212C024003402021201C542016201B542016201B511B2017201A54201420195420142019511B2017201A85201420198584501B0D010240202120225A2016201F5A2016201F51220D1B201720205A201420155A20142015511B2017202085201420158584501B450D00201420157D2017202054AD7D201720207D2217202120225422132016201F54200D1BAD222D54AD7D21142016201F7D2013AD7D211620252029842125202120227D2121202A202884212A202B202784212B202C202684212C2017202D7D21170B20294201882028423F868421292022420188201F423F8684212220284201882027423F86842128201F4201882020423F8684211F20274201882026423F8684212720204201882015423F8684212020264201882126201542018821150C000B0B200E41A8036A41186A202C370300200E4188036A41186A2014370300200E20253703A803200E202137038803200E202A3703B003200E201637039003200E202B3703B8030B200E2017370398030B4101450D11200E41C0036A2903002114200E41B8036A2903002115200E41B0036A2903002116200E2903A80321174100210D0240200420315A200520305A20052030511B2006202F5A2007202E5A2007202E511B2006202F852007202E8584501B450D00201720005A201620015A20162001511B201520025A201420035A20142003511B2015200285201420038584501B210D0B0240200D450D00201841606A220D22132400200D1007200D41106A3502002103200D2903002101200D41086A2903002102201341406A220D22132400200D41286A2002370300200D41206A2001370300200D41306A20033E0200200D41186A4200370300200D4200370310200D4200370308200D4200370300201341606A221322182400200D41382013100A20132903002103201341086A2903002101201341106A2903002102201341186A2903002100201841606A220D22132400200D41186A2000370300200D2002370310200D2001370308200D2003370300201341606A221322182400200D20131001201341186A2903002103201341106A2903002101201341086A290300210220132903002100201841606A220D2218240020002002200120032017201620152014200D102422130D02200D41086A2903002103200D41106A2903002101200D41186A2903002102200D2903002100201841606A220D22132400200D1007200D41106A3502002107200D2903002105200D41086A2903002106201341406A220D22132400200D41286A2006370300200D41206A2005370300200D41306A20073E0200200D41186A4200370300200D4200370310200D4200370308200D4200370300201341606A221322182400200D41382013100A20132903002107201341086A2903002105201341106A2903002106201341186A2903002104201841606A220D22132400200D41186A2004370300200D2006370310200D2005370308200D2007370300201341606A221322182400201341186A2002370300201320013703102013200337030820132000370300200D20131008201841606A220D22182400200F2011201020122017201620152014200D102422130D03200D41086A2903002103200D41106A2903002101200D41186A2903002102200D2903002100201841606A220D22132400200D41186A4200370300200D4200370310200D4200370308200D4202370300201341606A221322182400201341186A2002370300201320013703102013200337030820132000370300200D20131008201841606A220D22132400200D1007200D41106A3502002103200D2903002101200D41086A2903002102201341606A220D22182400200D1000200D41106A3502002100200D2903002107200D41086A290300210541E4001011221341A3F0CAEB7D360200201841606A220D22182400200D2002370308200D2001370300200D20033E0210200D201341046A41141013201841606A220D22182400200D2005370308200D2007370300200D20003E0210200D201341246A41141013201841606A220D22182400200D41186A202E370300200D202F370310200D2030370308200D2031370300200D201341C4006A41201013201841606A220D22182400200D41186A4200370300200D4200370310200D4200370308200D4206370300201841606A2218221D2400200D20181001201841106A350200210320182903002101201841086A2903002102201D41606A2218220D24002018200237030820182001370300201820033E0210200D41606A220D221D2400200D41186A4200370300200D4200370310200D4200370308200D4200370300201D41706A221D2400201D4200370300201841E4002013200D201D10020D041003220D41086A10112213200D360200201341046A200D360200201341086A22184100200D10042013280200411F4D0D05201841186A2903004200510D06200C2017370300200C2016370308200C2015370310200C41186A2014370300200E41E0036A240041000F0B41004100100541011006000B410D410141E6001017220D280200413F6A41607141246A220C1011220E41A0F38DC600360200201841706A221322182400201341203602002013200E41046A41041013200D2802002113201841706A22182400201820133602002018200E41246A41041013200E41C4006A200D41086A2013100E200E200C100541011006000B200E41E0036A240020130F0B200E41E0036A240020130F0B41004100100541011006000B200E41E0036A240041030F0B41004100100541011006000B41004100100541011006000B200E41E0036A240041030F0B41004100100541011006000B41004100100541011006000B200E41E0036A240041030F0B41004100100541011006000B41004100100541011006000B200E41E0036A240041000F0B200E41E0036A240020130F0B41004100100541011006000B200E41E0036A240020130F0B200E41E0036A240041000B950201047F23002209210A0240200420005620052001562005200151220B1B2006200256200720035620072003511B2006200285200720038584501B0D002008200020047D3703002008200120057D20002004542209AD7D3703082008200220067D220420092001200554200B1BAD22057D370310200841186A200320077D2002200654AD7D2004200554AD7D370300200A240041000F0B4117410141101017220A280200413F6A41607141246A220C1011220841A0F38DC600360200200941706A220B22092400200B4120360200200B200841046A41041013200A280200210B200941706A220924002009200B3602002009200841246A41041013200841C4006A200A41086A200B100E2008200C100541011006000BEE0D06027F017E017F067E027F0E7E23004190026B2209210A20092400024002400240024020002002842001200384220B84420052220C450D00200A4190016A41186A2007370300200A2000370370200A2001370378200A200237038001200A41F0006A41186A2003370300200A200437039001200A200537039801200A20063703A001200A41F0006A200A4190016A200A41B0016A41081014200C450D01200A41B0016A41186A290300210D200A41C0016A290300210E200A41B0016A41086A290300210F200A2903B0012110024002402000420185200284200B844200520D00200A41D0016A41186A4200370300200A41F0016A41186A200D370300200A20103703F001200A42003703E001200A42003703D801200A42003703D001200A200F3703F801200A200E370380020C010B02402010200085200E200285220B84200F200185200D200385221184844200520D00200A41F0016A41186A4200370300200A41D0016A41186A4200370300200A420037038002200A42003703F801200A42013703F001200A42003703E001200A42003703D801200A42003703D0010C010B024002402010200E84200F200D8484500D00201020005A200F20015A200F2001511B200E20025A200D20035A200D2003511B200B201184501B0D010B200A41F0016A41186A4200370300200A41D0016A41186A200D370300200A420037038002200A42003703F801200A42003703F001200A20103703D001200A200F3703D801200A200E3703E0010C010B42002112200A41E0006A2002200320037920027942C0007C20034200521B20017920007942C0007C20014200521B4280017C20022003844200521B200D79200E7942C0007C200D4200521B200F7920107942C0007C200F4200521B4280017C200E200D844200521B7DA7220C1015200A41306A20002001418001200C6B22131016200A41C0006A20002001200C41807F6A22141015200A41D0006A20002001200C1015200A41206A4201420020141015200A41106A4201420020131016200A42014200200C1015200A41106A41086A290300200A41206A41086A290300200C4180014922131B4200200C1B2115200A290310200A29032020131B4200200C1B2116200A41086A290300420020131B2117200A290300420020131B21180240200A290350420020131B2219201058200A41D0006A41086A290300420020131B221A200F58201A200F511B200A290360200A29033084200A29034020131B2002200C1B221B200E58200A41E0006A41086A290300200A41306A41086A29030084200A41C0006A41086A29030020131B2003200C1B2211200D582011200D511B201B200E852011200D8584501B0D0020184201882017423F868421182019420188201A423F8684211920174201882016423F86842117201A420188201B423F8684211A20164201882015423F86842116201B4201882011423F8684211B20154201882115201142018821110B4200211C4200211D4200211E2010211F200F2120200E2121200D210B02400340201F200054202020015420202001511B2021200254200B200354200B2003511B2021200285200B20038584501B0D010240201F20195A2020201A5A2020201A51220C1B2021201B5A200B20115A200B2011511B2021201B85200B20118584501B450D00200B20117D2021201B54AD7D2021201B7D2221201F20195422132020201A54200C1BAD222254AD7D210B2020201A7D2013AD7D212020122018842112201F20197D211F201C201784211C201D201684211D201E201584211E202120227D21210B20184201882017423F868421182019420188201A423F8684211920174201882016423F86842117201A420188201B423F8684211A20164201882015423F86842116201B4201882011423F8684211B20154201882115201142018821110C000B0B200A41F0016A41186A201E370300200A41D0016A41186A200B370300200A20123703F001200A201F3703D001200A201C3703F801200A20203703D801200A201D37038002200A20213703E0010B41000D02200A2903F001200485200A4180026A29030020068584200A41F0016A41086A290300200585200A41F0016A41186A2903002007858484500D034116410141D0001017220C280200413F6A41607141246A22141011220A41A0F38DC600360200200941706A220822132400200841203602002008200A41046A41041013200C2802002108201341706A22132400201320083602002013200A41246A41041013200A41C4006A200C41086A2008100E200A2014100541011006000B200842003703102008420037030820084200370300200841186A4200370300200A4190026A240041000F0B41004100100541011006000B200A4190026A240041000F0B200820103703002008200F3703082008200E370310200841186A200D370300200A4190026A240041000B940203027F017E027F23002209210A0240200020047C220B200054220C200120057C200CAD7C220420015420042001511B220C200220067C2200200CAD7C2201200254200320077C2000200254AD7C2001200054AD7C220020035420002003511B2001200285200020038584501B0D002008200B3703002008200437030820082001370310200841186A2000370300200A240041000F0B4116410141301017220A280200413F6A41607141246A220D1011220841A0F38DC600360200200941706A220C22092400200C4120360200200C200841046A41041013200A280200210C200941706A220924002009200C3602002009200841246A41041013200841C4006A200A41086A200C100E2008200D100541011006000BB22706027F067E017F037E037F147E230041C0036B22122400201222134180026A41186A420037030020134200370390022013420037038802201342023703800220134180026A201341E0016A1001024002400240024002400240024002400240024002400240024002400240024020132903E0012214201341E0016A41106A290300221584201341E0016A41086A2903002216201341E0016A41186A2903002217842218842219500D00201241606A2212221A240020121000201241106A350200211B2012290300211C201241086A290300211D41241011221E41F0C08A8C03360200201A41606A2212221A24002012201D3703082012201C3703002012201B3E02102012201E41046A41141013201A41606A2212221A2400201241186A4200370300201242003703102012420037030820124206370300201A41606A221A221F24002012201A1001201A41106A350200211B201A290300211C201A41086A290300211D201F41606A221A22122400201A201D370308201A201C370300201A201B3E0210201241606A2212221F2400201241186A4200370300201242003703102012420037030820124200370300201F41706A221F22202400201F4200370300201A4124201E2012201F10020D011003221241086A1011221A2012360200201A41046A2012360200201A41086A221E410020121004201A280200411F4D0D02201E201341A0026A41201012201341A0026A41186A2903002121201341A0026A41106A2903002122201341A0026A41086A290300212320132903A0022124202041606A2212221E240020002001200220034290CE0042004200420020121023221A0D0420194200510D03201241186A290300211B201241106A2903002125201241086A290300211D201229030021260240024020144201852015842018844200520D00201341C0026A41186A4200370300201341E0026A41186A201B370300201320263703E002201342003703D002201342003703C802201342003703C0022013201D3703E802201320253703F0020C010B024020262014852025201585221C84201D201685201B201785222784844200520D00201341E0026A41186A4200370300201341C0026A41186A4200370300201342003703F002201342003703E802201342013703E002201342003703D002201342003703C802201342003703C0020C010B0240024002402026202584201D201B8484500D00202620145A201D20165A201D2016511B202520155A201B20175A201B2017511B201C202784501B0D010B201341E0026A41186A4200370300201341C0026A41186A201B370300201342003703F002201342003703E802201342003703E002201320263703C0022013201D3703C8020C010B42002128201341D0016A2015201720177920157942C0007C20174200521B20167920147942C0007C20164200521B4280017C20152017844200521B201B7920257942C0007C201B4200521B201D7920267942C0007C201D4200521B4280017C2025201B844200521B7DA722121015201341A0016A2014201641800120126B221A1016201341B0016A20142016201241807F6A221F1015201341C0016A201420162012101520134190016A42014200201F101520134180016A42014200201A1016201341F0006A420142002012101520134180016A41086A29030020134190016A41086A290300201241800149221A1B420020121B2129201329038001201329039001201A1B420020121B212A201341F0006A41086A2903004200201A1B212B20132903704200201A1B212C024020132903C0014200201A1B222D202658201341C0016A41086A2903004200201A1B2227201D582027201D511B20132903D00120132903A0018420132903B001201A1B201520121B222E202558201341D0016A41086A290300201341A0016A41086A29030084201341B0016A41086A290300201A1B201720121B221C201B58201C201B511B202E202585201C201B8584501B0D00202C420188202B423F8684212C202D4201882027423F8684212D202B420188202A423F8684212B2027420188202E423F86842127202A4201882029423F8684212A202E420188201C423F8684212E20294201882129201C420188211C0B4200212F4200213042002131024003402026201454201D201654201D2016511B2025201554201B201754201B2017511B2025201585201B20178584501B0D0102402026202D5A201D20275A201D20275122121B2025202E5A201B201C5A201B201C511B2025202E85201B201C8584501B450D00201B201C7D2025202E54AD7D2025202E7D22252026202D54221A201D20275420121BAD223254AD7D211B201D20277D201AAD7D211D2028202C8421282026202D7D2126202F202B84212F2030202A84213020312029842131202520327D21250B202C420188202B423F8684212C202D4201882027423F8684212D202B420188202A423F8684212B2027420188202E423F86842127202A4201882029423F8684212A202E420188201C423F8684212E20294201882129201C420188211C0C000B0B201341E0026A41186A2031370300201341C0026A41186A201B370300201320283703E002201320263703C0022013202F3703E8022013201D3703C802201320303703F0020B201320253703D0020B4101450D05201341E0026A41186A2903002131201341E0026A41106A2903002132201341E0026A41086A290300213320132903E0022134201E41606A2212221E24002000200120022003202420232022202120121023221A0D0720194200510D06201241186A290300211B201241106A2903002125201241086A290300211D201229030021260240024020144201852015842018844200520D0020134180036A41186A4200370300201341A0036A41186A201B370300201320263703A0032013420037039003201342003703880320134200370380032013201D3703A803201320253703B0030C010B024020262014852025201585221C84201D201685201B201785222784844200520D00201341A0036A41186A420037030020134180036A41186A4200370300201342003703B003201342003703A803201342013703A0032013420037039003201342003703880320134200370380030C010B0240024002402026202584201D201B8484500D00202620145A201D20165A201D2016511B202520155A201B20175A201B2017511B201C202784501B0D010B201341A0036A41186A420037030020134180036A41186A201B370300201342003703B003201342003703A803201342003703A00320132026370380032013201D370388030C010B42002118201341E0006A2015201720177920157942C0007C20174200521B20167920147942C0007C20164200521B4280017C20152017844200521B201B7920257942C0007C201B4200521B201D7920267942C0007C201D4200521B4280017C2025201B844200521B7DA722121015201341306A2014201641800120126B221A1016201341C0006A20142016201241807F6A221F1015201341D0006A2014201620121015201341206A42014200201F1015201341106A42014200201A101620134201420020121015201341106A41086A290300201341206A41086A290300201241800149221A1B420020121B212920132903102013290320201A1B420020121B212A201341086A2903004200201A1B212B20132903004200201A1B212C024020132903504200201A1B222D202658201341D0006A41086A2903004200201A1B2227201D582027201D511B20132903602013290330842013290340201A1B201520121B222E202558201341E0006A41086A290300201341306A41086A29030084201341C0006A41086A290300201A1B201720121B221C201B58201C201B511B202E202585201C201B8584501B0D00202C420188202B423F8684212C202D4201882027423F8684212D202B420188202A423F8684212B2027420188202E423F86842127202A4201882029423F8684212A202E420188201C423F8684212E20294201882129201C420188211C0B42002119420021284200212F024003402026201454201D201654201D2016511B2025201554201B201754201B2017511B2025201585201B20178584501B0D0102402026202D5A201D20275A201D20275122121B2025202E5A201B201C5A201B201C511B2025202E85201B201C8584501B450D00201B201C7D2025202E54AD7D2025202E7D22252026202D54221A201D20275420121BAD223054AD7D211B201D20277D201AAD7D211D2018202C8421182026202D7D21262019202B8421192028202A842128202F202984212F202520307D21250B202C420188202B423F8684212C202D4201882027423F8684212D202B420188202A423F8684212B2027420188202E423F86842127202A4201882029423F8684212A202E420188201C423F8684212E20294201882129201C420188211C0C000B0B201341A0036A41186A202F37030020134180036A41186A201B370300201320183703A0032013202637038003201320193703A8032013201D37038803201320283703B0030B20132025370390030B4101450D08201341B8036A290300211B201341B0036A290300211C201341A8036A290300211D20132903A003212D410021120240203420045A203320055A20332005511B203220065A203120075A20312007511B2032200685203120078584501B450D00202D20085A201D20095A201D2009511B201C200A5A201B200B5A201B200B511B201C200A85201B200B8584501B21120B2012450D09201E41606A2212221A240020121007201241106A350200212520122903002127201241086A290300212E201A41406A2212221A2400201241286A202E370300201241206A2027370300201241306A20253E0200201241186A4200370300201242003703102012420037030820124200370300201A41606A221A221E240020124138201A100A201A2903002125201A41086A2903002127201A41106A290300212E201A41186A2903002126201E41606A2212221A2400201241186A20263703002012202E3703102012202737030820122025370300201A41606A221A221E24002012201A1001201A41186A2903002125201A41106A2903002127201A41086A290300212E201A2903002126201E41606A2212221E24002026202E20272025200020012002200320121022221A0D0A201241086A2903002125201241106A2903002127201241186A290300212E20122903002126201E41606A2212221A240020121007201241106A35020021292012290300212A201241086A290300212B201A41406A2212221A2400201241286A202B370300201241206A202A370300201241306A20293E0200201241186A4200370300201242003703102012420037030820124200370300201A41606A221A221E240020124138201A100A201A2903002129201A41086A290300212A201A41106A290300212B201A41186A290300212C201E41606A2212221A2400201241186A202C3703002012202B3703102012202A37030820122029370300201A41606A221A221E2400201A41186A202E370300201A2027370310201A2025370308201A20263703002012201A1008201E41606A2212221E24002014201620152017200020012002200320121022221A0D0B201241086A2903002125201241106A2903002127201241186A290300212E20122903002117201E41606A2212221A2400201241186A4200370300201242003703102012420037030820124202370300201A41606A221A221E2400201A41186A202E370300201A2027370310201A2025370308201A20173703002012201A1008201E41606A2212221A240020121007201241106A350200212520122903002127201241086A290300212E201A41606A221A22122400201A202E370308201A2027370300201A20253E0210201241606A2212221E2400201241186A2031370300201220323703102012203337030820122034370300201E41706A221E221F2400201E4200370300201A410041002012201E10020D0C201F41606A2212221A240020121007201241106A350200212520122903002127201241086A290300212E41C4001011221E41A98BF0DC7B360200201A41606A2212221A24002012202E37030820122027370300201220253E02102012201E41046A41141013201A41606A2212221A2400201241186A201B3703002012201C3703102012201D3703082012202D3703002012201E41246A41201013201A41606A2212221A2400201241186A4200370300201242003703102012420037030820124206370300201A41606A221A221F24002012201A1001201A41106A3502002125201A2903002127201A41086A290300212E201F41606A221A22122400201A202E370308201A2027370300201A20253E0210201241606A2212221F2400201241186A4200370300201242003703102012420037030820124200370300201F41706A221F2400201F4200370300201A41C400201E2012201F10020D0D1003221241086A1011221A2012360200201A41046A2012360200201A41086A221E410020121004201A280200411F4D0D0E201E41186A2903004200520D0F41004100100541011006000B41004100100541011006000B41004100100541011006000B201341C0036A240041030F0B41004100100541011006000B201341C0036A2400201A0F0B201341C0036A240041000F0B41004100100541011006000B201341C0036A2400201A0F0B201341C0036A240041000F0B41004100100541011006000B201341C0036A2400201A0F0B201341C0036A2400201A0F0B41004100100541011006000B41004100100541011006000B201341C0036A240041030F0B201020343703002010203337030820102032370310201041186A20313703002011201C370310201141186A201B3703002011202D3703002011201D370308201341C0036A240041000BA40F02067F087E23004180016B221A2400201A221B41086A10004100211C02402016201B290308852018201B41186A3502008542FFFFFFFF0F83842017201B41106A2903008584500D002016201842FFFFFFFF0F8384201784420052211C0B024002400240024002400240024002400240024002400240201C450D0041241011221D41D9D2A39206360200201A41606A221A221C2400201A41186A2003370300201A2002370310201A2001370308201A2000370300201A201D41046A41201013201C41606A221C221A2400201C2017370308201C2016370300201C20183E0210201A41606A221A221E2400201A41186A4200370300201A4200370310201A4200370308201A4200370300201E41706A221E221F2400201E4200370300201C4124201D201A201E10020D011003221A41086A1011221C201A360200201C41046A201A360200201C41086A221D4100201A1004201C280200411F4D0D02201D201B41206A41201012201B41206A41186A2903002120201B41206A41106A2903002121201B41206A41086A2903002122201B2903202123201F41606A221A221C2400201A1000201A41106A3502002124201A2903002125201A41086A290300212641241011221D41F0C08A8C03360200201C41606A221A221C2400201A2026370308201A2025370300201A20243E0210201A201D41046A41141013201C41606A221A221C2400201A41186A4200370300201A4200370310201A4200370308201A4206370300201C41606A221C221E2400201A201C1001201C41106A3502002124201C2903002125201C41086A2903002126201E41606A221C221A2400201C2026370308201C2025370300201C20243E0210201A41606A221A221E2400201A41186A4200370300201A4200370310201A4200370308201A4200370300201E41706A221E221F2400201E4200370300201C4124201D201A201E10020D031003221A41086A1011221C201A360200201C41046A201A360200201C41086A221D4100201A1004201C280200411F4D0D04201D201B41C0006A41201012201B41C0006A41186A2903002124201B41C0006A41106A2903002125201B41C0006A41086A2903002126201B2903402127201F41606A221A221D2400202320222021202020272026202520244290CE00420042004200201A1019221C0D054100211C02402004201A29030022255A2005201A41086A29030022045A20052004511B2006201A41106A29030022245A2007201A41186A29030022055A20072005511B2006202485200720058584501B450D00200820235A200920225A20092022511B200A20215A200B20205A200B2020511B200A202185200B20208584501B211C0B201C450D06201D41606A221A221D2400201A1000201A41106A3502002107201A2903002106201A41086A290300210B41E4001011221C41A3F0CAEB7D360200201D41606A221A221D2400201A2011370308201A2010370300201A20123E0210201A201C41046A41141013201D41606A221A221D2400201A200B370308201A2006370300201A20073E0210201A201C41246A41141013201D41606A221A221D2400201A41186A2005370300201A2024370310201A2004370308201A2025370300201A201C41C4006A41201013201D41606A221A221D2400201A41186A4200370300201A4200370310201A4200370308201A4206370300201D41606A221D221E2400201A201D1001201D41106A3502002107201D2903002106201D41086A290300210B201E41606A221D221A2400201D200B370308201D2006370300201D20073E0210201A41606A221A221E2400201A41186A4200370300201A4200370310201A4200370308201A4200370300201E41706A221E221F2400201E4200370300201D41E400201C201A201E10020D071003221A41086A1011221C201A360200201C41046A201A360200201C41086A221D4100201A1004201C280200411F4D0D08201D41186A2903004200510D0941E4001011221C418BAED9C103360200201F41606A221A221D2400201A41186A2003370300201A2002370310201A2001370308201A2000370300201A201C41046A41201013201D41606A221A221D2400201A41186A200F370300201A200E370310201A200D370308201A200C370300201A201C41246A41201013201D41606A221A221D2400201A2014370308201A2013370300201A20153E0210201A201C41C4006A41141013201D41606A221D221A2400201D2017370308201D2016370300201D20183E0210201A41606A221A221E2400201A41186A2020370300201A2021370310201A2022370308201A2023370300201E41706A221E2400201E4200370300201D41E400201C201A201E10020D0A1003221A41086A1011221C201A360200201C41046A201A360200201C41086A221D4100201A1004201C280200411F4B0D0B201B4180016A240041030F0B41004100100541011006000B41004100100541011006000B201B4180016A240041030F0B41004100100541011006000B201B4180016A240041030F0B201B4180016A2400201C0F0B41004100100541011006000B41004100100541011006000B201B4180016A240041030F0B41004100100541011006000B41004100100541011006000B201D201B41E0006A41201012201920043703082019202537030020192024370310201941186A2005370300201B4180016A240041000BE20E02047F097E230041B0016B220D210E200D24004100210F02402004200684200520078484500D002008200A842009200B8484420052210F0B0240024002400240024002400240200F450D00200D41606A220D22102400200020012002200342E507420042004200200D1023220F0D01200D41186A2903002100200D41106A2903002101200D41086A2903002102200D2903002103201041606A220D22102400200320022001200020082009200A200B200D1023220F0D02200D41186A2903002108200D41106A290300210A200D41086A2903002109200D290300210B201041606A220D22102400200420052006200742E807420042004200200D1023220F0D03200D41186A2903002104200D41106A2903002106200D41086A2903002105200D2903002107201041606A220D240020072005200620042003200220012000200D1024220F0D05200D2903002211200D41106A290300221284200D41086A2903002213200D41186A2903002200842204844200510D040240024020114201852012842004844200520D00200E41F0006A41186A4200370300200E4190016A41186A2008370300200E200B37039001200E420037038001200E4200370378200E4200370370200E200937039801200E200A3703A0010C010B0240200B201185200A20128522048420092013852008200085220684844200520D00200E4190016A41186A4200370300200E41F0006A41186A4200370300200E42003703A001200E420037039801200E420137039001200E420037038001200E4200370378200E42003703700C010B024002400240200B200A84200920088484500D00200B20115A200920135A20092013511B200A20125A200820005A20082000511B2004200684501B0D010B200E4190016A41186A4200370300200E41F0006A41186A2008370300200E42003703A001200E420037039801200E420037039001200E200B370370200E20093703780C010B42002114200E41E0006A2012200020007920127942C0007C20004200521B20137920117942C0007C20134200521B4280017C20122000844200521B200879200A7942C0007C20084200521B200979200B7942C0007C20094200521B4280017C200A2008844200521B7DA7220D1015200E41306A20112013418001200D6B220F1016200E41C0006A20112013200D41807F6A22101015200E41D0006A20112013200D1015200E41206A4201420020101015200E41106A42014200200F1016200E42014200200D1015200E41106A41086A290300200E41206A41086A290300200D41800149220F1B4200200D1B2101200E290310200E290320200F1B4200200D1B2102200E41086A2903004200200F1B2103200E2903004200200F1B21150240200E2903504200200F1B2207200B58200E41D0006A41086A2903004200200F1B220620095820062009511B200E290360200E29033084200E290340200F1B2012200D1B2205200A58200E41E0006A41086A290300200E41306A41086A29030084200E41C0006A41086A290300200F1B2000200D1B220420085820042008511B2005200A85200420088584501B0D0020154201882003423F8684211520074201882006423F8684210720034201882002423F8684210320064201882005423F8684210620024201882001423F8684210220054201882004423F8684210520014201882101200442018821040B42002116420021174200211802400340200B201154200920135420092013511B200A201254200820005420082000511B200A201285200820008584501B0D010240200B20075A200920065A2009200651220D1B200A20055A200820045A20082004511B200A200585200820048584501B450D00200820047D200A200554AD7D200A20057D220A200B200754220F2009200654200D1BAD221954AD7D2108200920067D200FAD7D210920142015842114200B20077D210B201620038421162017200284211720182001842118200A20197D210A0B20154201882003423F8684211520074201882006423F8684210720034201882002423F8684210320064201882005423F8684210620024201882001423F8684210220054201882004423F8684210520014201882101200442018821040C000B0B200E4190016A41186A2018370300200E41F0006A41186A2008370300200E201437039001200E200B370370200E201637039801200E2009370378200E20173703A0010B200E200A370380010B41010D06200E41B0016A240041000F0B410D410141E6001017220E280200413F6A41607141246A220C1011220F41A0F38DC600360200200D41706A220D22102400200D4120360200200D200F41046A41041013200E280200210D201041706A221024002010200D3602002010200F41246A41041013200F41C4006A200E41086A200D100E200F200C100541011006000B200E41B0016A2400200F0F0B200E41B0016A2400200F0F0B200E41B0016A2400200F0F0B41004100100541011006000B200E41B0016A2400200F0F0B200C200E29039001370300200C41186A200E4190016A41186A290300370300200C200E41A0016A290300370310200C200E4198016A290300370308200E41B0016A240041000B960402057F037E230041B0016B221724004124101122184186E4FF9506360200201722192014370388012019201337038001201920153E02900120194180016A201841046A41141013201941E0006A41186A4200370300201942003703702019420037036820194207370360201941E0006A201941C0006A1001201941086A41186A4200370300201942003703182019420037031020194200370308201942003703002019201941C0006A41086A2903003703342019201929034037032C2019201941D0006A3502003E023C0240024002402019412C6A41242018201941086A201910020D001003221841086A1011221A2018360200201A41046A2018360200201A41086A221B410020181004201A280200411F4D0D01201B20194198016A4118101220194198016A41086A290300211520194198016A41106A35020021132019290398012114201741606A2218221A240020181007201841086A290300211C2018290300211D201841106A350200211E201A41606A221824002000200120022003200420052006200720082009200A200B200C200D200E200F201D201C201E20102011201220142015201320181026221A450D02201941B0016A2400201A0F0B41004100100541011006000B201941B0016A240041030F0B20162018290300370300201641186A201841186A2903003703002016201841106A2903003703102016201841086A290300370308201941B0016A240041000BB80404027F037E017F017E230041C0016B220824002008220941A8016A1007200941A8016A41106A350200210A200941A8016A41086A290300210B20092903A801210C200841406A2208220D2400200841286A200B370300200841206A200C370300200841306A200A3E0200200841186A42003703002008420037031020084200370308200842013703002008413820094188016A100A20094188016A41086A290300210A20094188016A41106A290300210B20094188016A41186A290300210C200929038801210E200D41406A2208220D2400200841286A2001370300200841206A2000370300200841306A20023E0200200841186A200C3703002008200B3703102008200A3703082008200E37030020084138200941E8006A100A200941C8006A41186A200941E8006A41186A2903003703002009200941E8006A41106A2903003703582009200941E8006A41086A29030037035020092009290368370348200941C8006A200941286A1001024002402009290328200941286A41086A290300200941286A41106A290300200941286A41186A2903002003200420052006200941086A102422080D00200941086A41186A2903002103200941086A41106A2903002104200941086A41086A290300210520092903082106200D41606A22082400200810072008290300200841086A290300200841106A3502002000200120022006200520042003102A2208450D01200941C0016A240020080F0B200941C0016A240020080F0B200741013A0000200941C0016A240041000BC80403047F047E017F2300220A210B024002402003200542FFFFFFFF0F8384200484500D002000200242FFFFFFFF0F83842001844200520D0141004100100541011006000B41004100100541011006000B200A41406A220A220C2400200A41286A2001370300200A41206A2000370300200A41306A20023E0200200A41186A4200370300200A4200370310200A4200370308200A4201370300200C41606A220C220D2400200A4138200C100A200C290300210E200C41086A290300210F200C41106A2903002110200C41186A2903002111200D41406A220A220C2400200A41286A2004370300200A41206A2003370300200A41306A20053E0200200A41186A2011370300200A2010370310200A200F370308200A200E370300200C41606A220C220D2400200A4138200C100A200C290300210E200C41086A290300210F200C41106A2903002110200C41186A2903002111200D41606A220A220C2400200A41186A2011370300200A2010370310200A200F370308200A200E370300200C41606A220C220D2400200C41186A2009370300200C2008370310200C2007370308200C2006370300200A200C100841201011210C200D41606A220A220D2400200A41186A2009370300200A2008370310200A2007370308200A2006370300200A200C41201013412010112112200D41606A220A220D2400200A2001370308200A2000370300200A20023E0210200A201241141013412010112112200D41606A220A2400200A2004370308200A2003370300200A20053E0210200A201241141013200C4120100B200B240041000B9A0202037F037E230041206B221024002010221141086A1000410021120240200C201129030885200E201141186A3502008542FFFFFFFF0F8384200D201141106A2903008584500D00200C200E42FFFFFFFF0F8384200D8442005221120B024002402012450D00201041606A22102212240020101007201041086A290300211320102903002114201041106A3502002115201241606A221024002000200120022003200420052006200720082009200A200B201420132015200C200D200E2010102C2212450D01201141206A240020120F0B41004100100541011006000B200F2010290300370300200F41186A201041186A290300370300200F201041106A290300370310200F201041086A290300370308201141206A240041000BC10904027F037E037F017E230041D0016B22132400201322144198016A100020144198016A41106A350200211520144198016A41086A2903002116201429039801211741241011221841F0C08A8C0336020020142016370388012014201737038001201420153E02900120144180016A201841046A41141013201441E0006A41186A4200370300201442003703702014420037036820144206370360201441E0006A201441C0006A1001201441086A41186A4200370300201442003703182014420037031020144200370308201442003703002014201441C0006A41086A2903003703342014201429034037032C2014201441C0006A41106A3502003E023C024002400240024002400240024002402014412C6A41242018201441086A201410020D001003221841086A101122192018360200201941046A2018360200201941086A221A4100201810042019280200411F4D0D01201A201441B0016A41201012201441B0016A41186A2903002115201441B0016A41106A2903002116201441B0016A41086A290300211720142903B001211B201341606A2218221324002000200120022003201B2017201620154290CE004200420042002018102722190D022018290300221B20045A201841086A290300221620055A20162005511B201841106A290300221720065A201841186A290300221520075A20152007511B2017200685201520078584501B450D03201341606A221922182400201920103703082019200F370300201920113E0210201841606A221822132400201841186A201537030020182017370310201820163703082018201B370300201341706A2213221A2400201342003703002019410041002018201310020D04201A41606A22182213240020181000201841106A350200210720182903002105201841086A290300210641E4001011221941A3F0CAEB7D360200201341606A2218221324002018200D3703082018200C3703002018200E3E02102018201941046A41141013201341606A2218221324002018200637030820182005370300201820073E02102018201941246A41141013201341606A221822132400201841186A20033703002018200237031020182001370308201820003703002018201941C4006A41201013201341606A221822132400201841186A4200370300201842003703102018420037030820184206370300201341606A2213221A2400201820131001201341106A350200210020132903002101201341086A2903002102201A41606A2213221824002013200237030820132001370300201320003E0210201841606A2218221A2400201841186A4200370300201842003703102018420037030820184200370300201A41706A221A2400201A4200370300201341E40020192018201A10020D051003221841086A101122192018360200201941046A2018360200201941086A22134100201810042019280200411F4D0D06201341186A2903004200520D0741004100100541011006000B41004100100541011006000B201441D0016A240041030F0B201441D0016A240020190F0B41004100100541011006000B41004100100541011006000B41004100100541011006000B201441D0016A240041030F0B2012201B3703002012201637030820122017370310201241186A2015370300201441D0016A240041000BE50903047F087E017F2300220A210B0240024002402003200542FFFFFFFF0F8384200484500D00200A41406A220A220C2400200A41286A2001370300200A41206A2000370300200A41306A20023E0200200A41186A4200370300200A4200370310200A4200370308200A4200370300200C41606A220C220D2400200A4138200C100A200C290300210E200C41086A290300210F200C41106A2903002110200C41186A2903002111200D41606A220A220C2400200A41186A2011370300200A2010370310200A200F370308200A200E370300200C41606A220C220D2400200A200C1001200C41186A290300210E200C41106A290300210F200C41086A2903002110200C2903002111200D41606A220A220D240020112010200F200E2006200720082009200A1022220C0D01200A41086A290300210E200A41106A290300210F200A41186A2903002110200A2903002111200D41406A220A220C2400200A41286A2001370300200A41206A2000370300200A41306A20023E0200200A41186A4200370300200A4200370310200A4200370308200A4200370300200C41606A220C220D2400200A4138200C100A200C2903002112200C41086A2903002113200C41106A2903002114200C41186A2903002115200D41606A220A220C2400200A41186A2015370300200A2014370310200A2013370308200A2012370300200C41606A220C220D2400200C41186A2010370300200C200F370310200C200E370308200C2011370300200A200C1008200D41406A220A220C2400200A41286A2004370300200A41206A2003370300200A41306A20053E0200200A41186A4200370300200A4200370310200A4200370308200A4200370300200C41606A220C220D2400200A4138200C100A200C290300210E200C41086A290300210F200C41106A2903002110200C41186A2903002111200D41606A220A220C2400200A41186A2011370300200A2010370310200A200F370308200A200E370300200C41606A220C220D2400200A200C1001200C41186A290300210E200C41106A290300210F200C41086A2903002110200C2903002111200D41606A220A220D240020112010200F200E2006200720082009200A1024220C450D02200B2400200C0F0B41004100100541011006000B200B2400200C0F0B200A41086A290300210E200A41106A290300210F200A41186A2903002110200A2903002111200D41406A220A220C2400200A41286A2004370300200A41206A2003370300200A41306A20053E0200200A41186A4200370300200A4200370310200A4200370308200A4200370300200C41606A220C220D2400200A4138200C100A200C2903002112200C41086A2903002113200C41106A2903002114200C41186A2903002115200D41606A220A220C2400200A41186A2015370300200A2014370310200A2013370308200A2012370300200C41606A220C220D2400200C41186A2010370300200C200F370310200C200E370308200C2011370300200A200C100841201011210C200D41606A220A220D2400200A41186A2009370300200A2008370310200A2007370308200A2006370300200A200C41201013412010112116200D41606A220A220D2400200A2001370308200A2000370300200A20023E0210200A201641141013412010112116200D41606A220A2400200A2004370308200A2003370300200A20053E0210200A201641141013200C4120100B200B240041000BC60404037F037E037F017E230041206B220521062005240002400240024002402000200284200120038484500D00200541606A22052207240020051000200541106A350200210820052903002109200541086A290300210A41241011220B41F0C08A8C03360200200741606A2205220724002005200A37030820052009370300200520083E02102005200B41046A41141013200741606A220522072400200541186A4200370300200542003703102005420037030820054206370300200741606A2207220C2400200520071001200741106A350200210820072903002109200741086A290300210A200C41606A2207220524002007200A37030820072009370300200720083E0210200541606A2205220C2400200541186A4200370300200542003703102005420037030820054200370300200C41706A220C220D2400200C420037030020074124200B2005200C10020D011003220541086A101122072005360200200741046A2005360200200741086A220B4100200510042007280200411F4D0D02200B200641201012200641186A2903002108200641106A2903002109200641086A290300210A2006290300210E200D41606A220524002000200120022003200E200A200920084290CE00420042004200200510272207450D03200641206A240020070F0B41004100100541011006000B41004100100541011006000B200641206A240041030F0B20042005290300370300200441186A200541186A2903003703002004200541106A2903003703102004200541086A290300370308200641206A240041000BC60404037F037E037F017E230041206B220521062005240002400240024002402000200284200120038484500D00200541606A22052207240020051000200541106A350200210820052903002109200541086A290300210A41241011220B41F0C08A8C03360200200741606A2205220724002005200A37030820052009370300200520083E02102005200B41046A41141013200741606A220522072400200541186A4200370300200542003703102005420037030820054206370300200741606A2207220C2400200520071001200741106A350200210820072903002109200741086A290300210A200C41606A2207220524002007200A37030820072009370300200720083E0210200541606A2205220C2400200541186A4200370300200542003703102005420037030820054200370300200C41706A220C220D2400200C420037030020074124200B2005200C10020D011003220541086A101122072005360200200741046A2005360200200741086A220B4100200510042007280200411F4D0D02200B200641201012200641186A2903002108200641106A2903002109200641086A290300210A2006290300210E200D41606A2205240020002001200220034290CE00420042004200200E200A20092008200510272207450D03200641206A240020070F0B41004100100541011006000B41004100100541011006000B200641206A240041030F0B20042005290300370300200441186A200541186A2903003703002004200541106A2903003703102004200541086A290300370308200641206A240041000BDA0402047F047E2300220B210C0240024002402000200120022003200420052006200720082009102D220D0D00200B41406A220D220B2400200D41286A2001370300200D41206A2000370300200D41306A20023E0200200D41186A4200370300200D4200370310200D4200370308200D4201370300200B41606A220B220E2400200D4138200B100A200B2903002103200B41086A2903002104200B41106A2903002105200B41186A290300210F200E41606A220D220B2400200D1007200D41106A3502002110200D2903002111200D41086A2903002112200B41406A220D220B2400200D41286A2012370300200D41206A2011370300200D41306A20103E0200200D41186A200F370300200D2005370310200D2004370308200D2003370300200B41606A220B220E2400200D4138200B100A200B2903002103200B41086A2903002104200B41106A2903002105200B41186A290300210F200E41606A220D220B2400200D41186A200F370300200D2005370310200D2004370308200D2003370300200B41606A220B220E2400200D200B1001200B41186A2903002103200B41106A2903002104200B41086A2903002105200B290300210F200E41606A220D220E2400200F2005200420032006200720082009200D1022220B0D01200D41186A2903002106200D41106A2903002107200D41086A2903002108200D2903002109200E41606A220D2400200D1007200020012002200D290300200D41086A290300200D41106A3502002009200820072006102A220D450D02200C2400200D0F0B200C2400200D0F0B200C2400200B0F0B200A41013A0000200C240041000B8E0202037F037E230041206B221724002017221841086A10000240024020102018290308852012201841086A41106A3502008542FFFFFFFF0F83842011201841086A41086A2903008584500D00201741606A22172219240020171007201741086A290300211A2017290300211B201741106A350200211C201941606A221724002000200120022003200420052006200720082009200A200B200C200D200E200F201B201A201C2010201120122013201420152017101E2219450D01201841206A240020190F0B41004100100541011006000B20162017290300370300201641186A201741186A2903003703002016201741106A2903003703102016201741086A290300370308201841206A240041000BB80404027F037E017F017E230041C0016B220824002008220941A8016A1007200941A8016A41106A350200210A200941A8016A41086A290300210B20092903A801210C200841406A2208220D2400200841286A200B370300200841206A200C370300200841306A200A3E0200200841186A42003703002008420037031020084200370308200842013703002008413820094188016A100A20094188016A41086A290300210A20094188016A41106A290300210B20094188016A41186A290300210C200929038801210E200D41406A2208220D2400200841286A2001370300200841206A2000370300200841306A20023E0200200841186A200C3703002008200B3703102008200A3703082008200E37030020084138200941E8006A100A200941C8006A41186A200941E8006A41186A2903003703002009200941E8006A41106A2903003703582009200941E8006A41086A29030037035020092009290368370348200941C8006A200941286A1001024002402009290328200941286A41086A290300200941286A41106A290300200941286A41186A2903002003200420052006200941086A102222080D00200941086A41186A2903002103200941086A41106A2903002104200941086A41086A290300210520092903082106200D41606A22082400200810072008290300200841086A290300200841106A3502002000200120022006200520042003102A2208450D01200941C0016A240020080F0B200941C0016A240020080F0B200741013A0000200941C0016A240041000BC00402057F067E230041B0016B221424004124101122154186E4FF9506360200201422162011370388012016201037038001201620123E02900120164180016A201541046A41141013201641E0006A41186A4200370300201642003703702016420037036820164207370360201641E0006A201641C0006A1001201641086A41186A4200370300201642003703182016420037031020164200370308201642003703002016201641C0006A41086A2903003703342016201629034037032C2016201641D0006A3502003E023C0240024002402016412C6A41242015201641086A201610020D001003221541086A101122172015360200201741046A2015360200201741086A22184100201510042017280200411F4D0D01201820164198016A4118101220164198016A41086A290300211220164198016A41106A35020021102016290398012111201441606A22152217240020151007201541086A29030021192015290300211A201541106A350200211B201741606A22152217240020151007201541086A290300211C2015290300211D201541106A350200211E201741606A221524002000200120022003200420052006200720082009200A200B200C200D200E200F201A2019201B201D201C201E201120122010201510262217450D02201641B0016A240020170F0B41004100100541011006000B201641B0016A240041030F0B20132015290300370300201341186A201541186A2903003703002013201541106A2903003703102013201541086A290300370308201641B0016A240041000B8E0202037F037E230041206B221724002017221841086A10000240024020102018290308852012201841086A41106A3502008542FFFFFFFF0F83842011201841086A41086A2903008584500D00201741606A22172219240020171007201741086A290300211A2017290300211B201741106A350200211C201941606A221724002000200120022003200420052006200720082009200A200B200C200D200E200F201B201A201C201020112012201320142015201710262219450D01201841206A240020190F0B41004100100541011006000B20162017290300370300201641186A201741186A2903003703002016201741106A2903003703102016201741086A290300370308201841206A240041000B960402057F037E230041B0016B221724004124101122184186E4FF9506360200201722192014370388012019201337038001201920153E02900120194180016A201841046A41141013201941E0006A41186A4200370300201942003703702019420037036820194207370360201941E0006A201941C0006A1001201941086A41186A4200370300201942003703182019420037031020194200370308201942003703002019201941C0006A41086A2903003703342019201929034037032C2019201941D0006A3502003E023C0240024002402019412C6A41242018201941086A201910020D001003221841086A1011221A2018360200201A41046A2018360200201A41086A221B410020181004201A280200411F4D0D01201B20194198016A4118101220194198016A41086A290300211520194198016A41106A35020021132019290398012114201741606A2218221A240020181007201841086A290300211C2018290300211D201841106A350200211E201A41606A221824002000200120022003200420052006200720082009200A200B200C200D200E200F201D201C201E2010201120122014201520132018101E221A450D02201941B0016A2400201A0F0B41004100100541011006000B201941B0016A240041030F0B20162018290300370300201641186A201841186A2903003703002016201841106A2903003703102016201841086A290300370308201941B0016A240041000BEC0101027F230041E0006B22042400200441406A22052400200541286A2001370300200541206A2000370300200541306A20023E0200200541186A4200370300200542003703102005420037030820054200370300200541382004220441C0006A100A200441206A41186A200441C0006A41186A2903003703002004200441C0006A41106A2903003703302004200441C0006A41086A29030037032820042004290340370320200441206A20041001200341186A200441186A2903003703002003200441106A2903003703102003200441086A29030037030820032004290300370300200441E0006A240041000BC60404037F037E037F017E230041206B220521062005240002400240024002402000200284200120038484500D00200541606A22052207240020051000200541106A350200210820052903002109200541086A290300210A41241011220B41F0C08A8C03360200200741606A2205220724002005200A37030820052009370300200520083E02102005200B41046A41141013200741606A220522072400200541186A4200370300200542003703102005420037030820054206370300200741606A2207220C2400200520071001200741106A350200210820072903002109200741086A290300210A200C41606A2207220524002007200A37030820072009370300200720083E0210200541606A2205220C2400200541186A4200370300200542003703102005420037030820054200370300200C41706A220C220D2400200C420037030020074124200B2005200C10020D011003220541086A101122072005360200200741046A2005360200200741086A220B4100200510042007280200411F4D0D02200B200641201012200641186A2903002108200641106A2903002109200641086A290300210A2006290300210E200D41606A220524002000200120022003200E200A200920084290CE00420042004200200510192207450D03200641206A240020070F0B41004100100541011006000B41004100100541011006000B200641206A240041030F0B20042005290300370300200441186A200541186A2903003703002004200541106A2903003703102004200541086A290300370308200641206A240041000BF30202037F017E23004180016B22072400200741406A220822092400200841286A2001370300200841206A2000370300200841306A20023E0200200841186A4200370300200842003703102008420037030820084201370300200841382007220741E0006A100A200741E0006A41086A2903002102200741E0006A41106A2903002100200741E0006A41186A29030021012007290360210A200941406A22082400200841286A2004370300200841206A2003370300200841306A20053E0200200841186A200137030020082000370310200820023703082008200A37030020084138200741C0006A100A200741206A41186A200741C0006A41186A2903003703002007200741C0006A41106A2903003703302007200741C0006A41086A29030037032820072007290340370320200741206A20071001200641186A200741186A2903003703002006200741106A2903003703102006200741086A2903003703082006200729030037030020074180016A240041000BEC0904027F037E037F057E230041D0016B22132400201322144198016A100020144198016A41106A350200211520144198016A41086A2903002116201429039801211741241011221841F0C08A8C0336020020142016370388012014201737038001201420153E02900120144180016A201841046A41141013201441E0006A41186A4200370300201442003703702014420037036820144206370360201441E0006A201441C0006A1001201441086A41186A4200370300201442003703182014420037031020144200370308201442003703002014201441C0006A41086A2903003703342014201429034037032C2014201441C0006A41106A3502003E023C0240024002400240024002400240024002402014412C6A41242018201441086A201410020D001003221841086A101122192018360200201941046A2018360200201941086A221A4100201810042019280200411F4D0D01201A201441B0016A41201012201441B0016A41186A2903002115201441B0016A41106A2903002116201441B0016A41086A290300211720142903B001211B201341606A2218221324004290CE0042004200420020042005200620072018102222190D02201841186A290300211C201841106A290300211D201841086A290300211E2018290300211F201341606A2218221324002000200120022003201F201E201D201C201B2017201620152018101922190D03201841186A2903002115201841106A2903002116201841086A29030021172018290300211B201341606A2218221924002004200520062007201B2017201620152018102222130D04024020182903002204201841106A290300220584201841086A2903002206201841186A290300220784844200510D00201941606A2213221824002013200D3703082013200C3703002013200E3E0210201841606A221822192400201841186A2007370300201820053703102018200637030820182004370300201941706A221A22192400201A42003703002013410041002018201A10020D060B41C4001011221341A98BF0DC7B360200201941606A221822192400201820103703082018200F370300201820113E02102018201341046A41141013201941606A221822192400201841186A20033703002018200237031020182001370308201820003703002018201341246A41201013201941606A221822192400201841186A4200370300201842003703102018420037030820184206370300201941606A2219221A2400201820191001201941106A350200210420192903002105201941086A2903002106201A41606A2219221824002019200637030820192005370300201920043E0210201841606A2218221A2400201841186A4200370300201842003703102018420037030820184200370300201A41706A221A2400201A4200370300201941C40020132018201A10020D061003221841086A101122192018360200201941046A2018360200201941086A22134100201810042019280200411F4D0D07201341186A2903004200520D0841004100100541011006000B41004100100541011006000B201441D0016A240041030F0B201441D0016A240020190F0B201441D0016A240020190F0B201441D0016A240020130F0B41004100100541011006000B41004100100541011006000B201441D0016A240041030F0B2012201B3703002012201737030820122016370310201241186A2015370300201441D0016A240041000BCE0202037F077E230041206B220C2400200C220D41086A10004100210E02402008200D29030885200A200D41186A3502008542FFFFFFFF0F83842009200D41106A2903008584500D002008200A42FFFFFFFF0F8384200984420052210E0B02400240200E450D00200C41606A220C220E2400200C1009200C41186A290300210F200C41106A2903002110200C41086A2903002111200C2903002112200E41606A220C220E2400200C1007200C41086A2903002113200C2903002114200C41106A3502002115200E41606A220C24002000200120022003201220112010200F200420052006200720142013201520082009200A200C1039220E450D01200D41206A2400200E0F0B41004100100541011006000B200B200C290300370300200B41186A200C41186A290300370300200B200C41106A290300370310200B200C41086A290300370308200D41206A240041000B867502057F167E23004180206B220021012000240010104100100C2202360278410020021011220336027C410020022003100D024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240200241034D0D004100200328020022043602742002417C6A2102200341046A21030240200441FCA3889E024A0D000240200441A3AF89BE7D4A0D000240200441EABAB4BA7B4A0D000240200441DCEF87BF7A4A0D0020044181FCF4DF78460D15200441F3B7EEDA79470D0420024120490D622003200141B80C6A412010122002AD423F580D11200141B80C6A41186A2903002105200141B80C6A41106A2903002106200141B80C6A41086A290300210720012903B80C2108200341206A200141D80C6A41201012200141D80C6A41186A2903002109200141D80C6A41106A290300210A200141D80C6A41086A290300210B20012903D80C210C200141C81F6A1009200141C81F6A41186A290300210D200141C81F6A41106A290300210E200141C81F6A41086A290300210F20012903C81F2110200141E81F6A1007200141E81F6A41086A2903002111200141E81F6A41106A350200211220012903E81F2113200141901F6A10072010200F200E200D2008200720062005200C200B200A200920132011201220012903901F200141901F6A41086A290300200141901F6A41106A350200200141A81F6A102022040D12200141F80C6A41186A200141A81F6A41186A290300370300200120012903A81F3703F80C2001200141B81F6A2903003703880D2001200141B01F6A2903003703800D41000D130CC0010B200441DDEF87BF7A460D0A20044189BC9D9D7B470D03200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520DA1012002411F4D0DA2012003200141901A6A411810122002AD423F580D1C200141901A6A41106A3502002105200141901A6A41086A290300210620012903901A2107200341206A200141A81A6A41201012200141A81A6A41186A2903002108200141A81A6A41106A2903002109200141A81A6A41086A290300210A20012903A81A210B200141C81F6A100720012903C81F200141C81F6A41086A290300200141C81F6A41106A350200200720062005200B200A20092008102A22040D1D200141013A00CF1A41000D1E0CBE010B0240200441B0978FFA7B4A0D00200441EBBAB4BA7B460D15200441A98BF0DC7B470D03200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D302002411F4D0D31200320014190026A411810122002AD423F580D0720014190026A41106A350200210520014190026A41086A29030021062001290390022107200341206A200141A8026A41201012200141A8026A41186A2903002108200141A8026A41106A2903002109200141A8026A41086A290300210A20012903A802210B200141C81F6A100720012903C81F200141C81F6A41086A290300200141C81F6A41106A350200200720062005200B200A20092008102D22040D08200141013A00CF0241000D090CBD010B200441B1978FFA7B460D0C200441CDEF91997C470D02200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D6D2002411F4D0D6E2003200141D8106A4120101220012903D810200141E0106A290300200141E8106A290300200141F0106A290300200141F8106A102F450D6F41004100100541011006000B0240200441A8C3AF907F4A0D00024020044194EDBEBC7E4A0D00200441A4AF89BE7D460D19200441A3F0CAEB7D460D1720044198ACB4E87D470D03200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D69200141C81F6A41186A4200370300200142003703D81F200142003703D01F200142023703C81F200141C81F6A200141A81F6A1001200141F80F6A41186A200141A81F6A41186A2903003703002001200141A81F6A41106A290300370388102001200141A81F6A41086A29030037038010200120012903A81F3703F80F41010D6A41004100100541011006000B20044195EDBEBC7E460D15200441F381BFCF7E470D02200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D492002411F4D0D4A2003200141D8076A412010122002AD2205423F580D4B200141D8076A41186A2903002106200141D8076A41106A2903002107200141D8076A41086A290300210820012903D8072109200341206A200141F8076A41201012200542DF00580D4C200141F8076A41186A290300210A200141F8076A41106A290300210B200141F8076A41086A290300210C20012903F807210D200341C0006A20014198086A41201012200542FF00580D4D20014198086A41186A290300210E20014198086A41106A290300210F20014198086A41086A29030021102001290398082111200341E0006A200141B8086A412010122005429F01580D4E200141B8086A41186A2903002112200141B8086A41106A2903002113200141B8086A41086A290300211420012903B808211520034180016A200141D8086A41181012200542BF01580D4F200141D8086A41106A3502002105200141D8086A41086A290300211620012903D8082117200341A0016A200141F0086A411810122009200820072006200D200C200B200A20112010200F200E201520142013201220172016200520012903F008200141F0086A41086A290300200141F0086A41106A35020020014188096A1028450D5041004100100541011006000B024020044195DBB9F5004A0D00200441E6A68B1C460D0420044195C797DE00460D12200441A9C3AF907F470D02200141C81F6A1009200141E01F6A2903002105200141C81F6A41106A2903002106200141C81F6A41086A290300210720012903C81F2108200141E81F6A1007200141E81F6A41086A2903002109200141E81F6A41106A350200210A20012903E81F210B200141901F6A10070240200820072006200542014200420042004200420042004200200B2009200A20012903901F200141901F6A41086A290300200141901F6A41106A350200200141A81F6A102022040D00410021040B2004450D4841004100100541011006000B200441F5A5E5DE01460D18200441D4C993EC01460D0420044196DBB9F500470D01200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D45200141C81F6A41186A4200370300200142003703D81F200142003703D01F200142073703C81F200141C81F6A200141A81F6A10012001200141A81F6A41086A2903003703C807200120012903A81F3703C0072001200141A81F6A41106A3502003E02D00741010D4641004100100541011006000B0240200441C1DEC098044A0D000240200441F1EE808F034A0D000240200441A580D9E7024A0D00200441FDA3889E02460D1B20044198B5CCB802470D03200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D89012002411F4D0D8A012003200141B8156A412010122002AD2205423F580D8B01200141B8156A41186A2903002106200141B8156A41106A2903002107200141B8156A41086A290300210820012903B8152109200341206A200141D8156A41201012200542DF00580D8C01200141D8156A41186A290300210A200141D8156A41106A290300210B200141D8156A41086A290300210C20012903D815210D200341C0006A200141F8156A41201012200542FF00580D8D01200141F8156A41186A290300210E200141F8156A41106A290300210F200141F8156A41086A290300211020012903F8152111200341E0006A20014198166A412010122005429F01580D8E0120014198166A41186A290300211220014198166A41106A290300211320014198166A41086A2903002114200129039816211520034180016A200141B8166A41181012200542BF01580D8F01200141B8166A41106A3502002105200141B8166A41086A290300211620012903B8162117200341A0016A200141D0166A411810122009200820072006200D200C200B200A20112010200F200E201520142013201220172016200520012903D016200141D0166A41086A290300200141D0166A41106A350200200141E8166A1034450D900141004100100541011006000B200441A680D9E702460D1E200441F0C08A8C03470D02200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D9D012002411F4D0D9E012003200141D8196A4118101220012903D819200141E0196A290300200141E8196A350200200141F0196A1036450D9F0141004100100541011006000B0240200441EBF1A8F2034A0D00200441F2EE808F03460D0E2004418BAED9C103470D0220024120490DA9012003200141E01B6A412010122002AD2205423F580DAA01200141E01B6A41186A2903002106200141E01B6A41106A2903002107200141E01B6A41086A290300210820012903E01B2109200341206A200141801C6A41201012200542DF00580DAB01200141801C6A41186A2903002105200141801C6A41106A290300210A200141801C6A41086A290300210B20012903801C210C200341C0006A200141A01C6A411810122009200820072006200C200B200A200520012903A01C200141A81C6A290300200141B01C6A350200200141B81C6A103A450DAC0141004100100541011006000B200441ECF1A8F203460D16200441DDC5B5F703470D01200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520DA4012002411F4D0DA5012003200141901B6A411810122002AD423F580DA601200141901B6A41106A3502002105200141901B6A41086A290300210620012903901B2107200341206A200141A81B6A4118101220072006200520012903A81B200141A81B6A41086A290300200141A81B6A41106A350200200141C01B6A1038450DA70141004100100541011006000B0240200441D8D2A392064A0D000240200441B8A0CD8C054A0D00200441C2DEC09804460D0A200441B081D5AE04470D02200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D81012002411F4D0D8201200320014180146A412010122002AD2205423F580D830120014180146A41186A290300210620014180146A41106A290300210720014180146A41086A29030021082001290380142109200341206A200141A0146A41201012200542DF00580D8401200141A0146A41186A290300210A200141A0146A41106A290300210B200141A0146A41086A290300210C20012903A014210D200341C0006A200141C0146A41201012200542FF00580D8501200141C0146A41186A290300210E200141C0146A41106A290300210F200141C0146A41086A290300211020012903C0142111200341E0006A200141E0146A412010122005429F01580D8601200141E0146A41186A2903002105200141E0146A41106A2903002112200141E0146A41086A290300211320012903E014211420034180016A20014180156A411810122009200820072006200D200C200B200A20112010200F200E201420132012200520012903801520014188156A29030020014190156A35020020014198156A1033450D870141004100100541011006000B200441B9A0CD8C05460D0C200441F897C6D705460D0A2004419DEDA9C705470D01200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D23200141C81F6A41186A4200370300200142003703D81F200142003703D01F200142063703C81F200141C81F6A200141A81F6A10012001200141A81F6A41086A290300370350200120012903A81F3703482001200141A81F6A41106A3502003E025841010D2441004100100541011006000B024020044188E5A38D074A0D00200441D9D2A39206460D02200441ADCBDDEE06470D0120024120490D37200320014188046A412010122002AD2205423F580D3820014188046A41186A290300210620014188046A41106A290300210720014188046A41086A29030021082001290388042109200341206A200141A8046A41201012200542DF00580D39200141A8046A41186A2903002105200141A8046A41106A290300210A200141A8046A41086A290300210B20012903A804210C200341C0006A200141C8046A411810122009200820072006200C200B200A200520012903C804200141D0046A290300200141D8046A350200200141E0046A101F450D3A41004100100541011006000B20044189E5A38D07460D1D200441EACBB1E807460D1E0B41004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D1D2002411F4D0D1E2003200141086A412010122001290308200141106A290300200141186A290300200141206A290300200141286A1018450D1F41004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D212002411F4D0D222003200141E0006A411810122001290360200141E8006A290300200141F0006A350200101A450D2341004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D232002411F4D0D242003200141F8006A412010122002AD2205423F580D25200141F8006A41186A2903002106200141F8006A41106A2903002107200141F8006A41086A290300210820012903782109200341206A20014198016A41201012200542DF00580D2620014198016A41186A290300210A20014198016A41106A290300210B20014198016A41086A290300210C200129039801210D200341C0006A200141B8016A41201012200542FF00580D27200141B8016A41186A2903002105200141B8016A41106A290300210E200141B8016A41086A290300210F20012903B8012110200341E0006A200141D8016A411810122009200820072006200D200C200B200A2010200F200E200520012903D801200141D8016A41086A290300200141D8016A41106A350200200141F0016A101B450D2841004100100541011006000B20014180206A240041030F0B2004450DB4010B41004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D272002411F4D0D282003200141D0026A412010122002AD2205423F580D29200141D0026A41186A2903002106200141D0026A41106A2903002107200141D0026A41086A290300210820012903D0022109200341206A200141F0026A41201012200542DF00580D2A200141F0026A41186A290300210A200141F0026A41106A290300210B200141F0026A41086A290300210C20012903F002210D200341C0006A20014190036A41201012200542FF00580D2B20014190036A41186A290300210E20014190036A41106A290300210F20014190036A41086A29030021102001290390032111200341E0006A200141B0036A412010122005429F01580D2C200141B0036A41186A2903002105200141B0036A41106A2903002112200141B0036A41086A290300211320012903B003211420034180016A200141D0036A411810122009200820072006200D200C200B200A20112010200F200E201420132012200520012903D003200141D8036A290300200141E0036A350200200141E8036A101D450D2D41004100100541011006000B20024120490D31200320014180056A412010122002AD2205423F580D3220014180056A41186A290300210620014180056A41106A290300210720014180056A41086A29030021082001290380052109200341206A200141A0056A41201012200542DF00580D33200141A0056A41186A2903002105200141A0056A41106A290300210A200141A0056A41086A290300210B20012903A005210C200341C0006A200141C0056A412010122009200820072006200C200B200A200520012903C005200141C8056A290300200141D0056A290300200141D8056A290300200141E0056A1021450D3441004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D342002411F4D0D35200320014180066A412010122002AD2205423F580D3620014180066A41186A290300210620014180066A41106A290300210720014180066A41086A29030021082001290380062109200341206A200141A0066A41201012200542DF00580D37200141A0066A41186A290300210A200141A0066A41106A290300210B200141A0066A41086A290300210C20012903A006210D200341C0006A200141C0066A41201012200542FF00580D38200141C0066A41186A2903002105200141C0066A41106A290300210E200141C0066A41086A290300210F20012903C0062110200341E0006A200141E0066A412010122009200820072006200D200C200B200A2010200F200E200520012903E006200141E0066A41086A290300200141E0066A41106A290300200141E0066A41186A29030020014180076A200141A0076A1025450D3941004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D442002411F4D0D452003200141A8096A412010122002AD2205423F580D46200141A8096A41186A2903002106200141A8096A41106A2903002107200141A8096A41086A290300210820012903A8092109200341206A200141C8096A41201012200542DF00580D47200141C8096A41186A290300210A200141C8096A41106A290300210B200141C8096A41086A290300210C20012903C809210D200341C0006A200141E8096A41201012200542FF00580D48200141E8096A41186A290300210E200141E8096A41106A290300210F200141E8096A41086A290300211020012903E8092111200341E0006A200141880A6A412010120240024002402005429F01580D00200141880A6A41186A2903002105200141880A6A41106A2903002112200141880A6A41086A290300211320012903880A211420034180016A200141A80A6A41181012200141A80A6A41086A2903002115200141A80A6A41106A350200211620012903A80A2117200141A81F6A1007200141A81F6A41086A2903002118200141A81F6A41106A350200211920012903A81F211A200141E81F6A10072009200820072006200D200C200B200A20112010200F200E2014201320122005201A2018201920012903E81F200141E81F6A41086A290300200141E81F6A41106A350200201720152016200141C81F6A101E22040D01200141C00A6A41186A200141C81F6A41186A290300370300200120012903C81F3703C00A2001200141C81F6A41106A2903003703D00A2001200141C81F6A41086A2903003703C80A41000D020CB1010B20014180206A240041030F0B2004450DAF010B41004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D482002411F4D0D492003200141E00A6A411810122002AD423F580D4A200141E00A6A41106A3502002105200141E00A6A41086A290300210620012903E00A2107200341206A200141F80A6A4120101220072006200520012903F80A200141F80A6A41086A290300200141F80A6A41106A290300200141F80A6A41186A2903002001419F0B6A1029450D4B41004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D4B2002411F4D0D4C2003200141A00B6A412010122002AD2205423F580D4D200141A00B6A41186A2903002106200141A00B6A41106A2903002107200141A00B6A41086A290300210820012903A00B2109200341206A200141C00B6A41201012200542DF00580D4E200141C00B6A41186A290300210A200141C00B6A41106A290300210B200141C00B6A41086A290300210C20012903C00B210D200341C0006A200141E00B6A41201012200542FF00580D4F200141E00B6A41186A2903002105200141E00B6A41106A290300210E200141E00B6A41086A290300210F20012903E00B2110200341E0006A200141800C6A411810122009200820072006200D200C200B200A2010200F200E200520012903800C200141800C6A41086A290300200141800C6A41106A350200200141980C6A102B450D5041004100100541011006000B20014180206A240041030F0B2004450DAD010B41004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D4E2002411F4D0D4F2003200141980D6A412010122002AD2205423F580D50200141980D6A41186A2903002106200141980D6A41106A2903002107200141980D6A41086A290300210820012903980D2109200341206A200141B80D6A41201012024002400240200542DF00580D00200141B80D6A41186A2903002105200141B80D6A41106A290300210A200141B80D6A41086A290300210B20012903B80D210C200341C0006A200141D80D6A41201012200141D80D6A41186A290300210D200141D80D6A41106A290300210E200141D80D6A41086A290300210F20012903D80D2110200141A81F6A1007200141A81F6A41086A2903002111200141A81F6A41106A350200211220012903A81F2113200141E81F6A10072009200820072006200C200B200A20052010200F200E200D20132011201220012903E81F200141E81F6A41086A290300200141E81F6A41106A350200200141C81F6A102C22040D01200141F80D6A41186A200141C81F6A41186A290300370300200120012903C81F3703F80D2001200141C81F6A41106A2903003703880E2001200141C81F6A41086A2903003703800E41000D020CAA010B20014180206A240041030F0B2004450DA8010B41004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D502002411F4D0D512003200141980E6A412010122002AD2205423F580D52200141980E6A41186A2903002106200141980E6A41106A2903002107200141980E6A41086A290300210820012903980E2109200341206A200141B80E6A41201012024002400240200542DF00580D00200141B80E6A41186A2903002105200141B80E6A41106A290300210A200141B80E6A41086A290300210B20012903B80E210C200341C0006A200141D80E6A41201012200141D80E6A41186A290300210D200141D80E6A41106A290300210E200141D80E6A41086A290300210F20012903D80E2110200141A81F6A1007200141A81F6A41086A2903002111200141A81F6A41106A350200211220012903A81F2113200141E81F6A10072009200820072006200C200B200A20052010200F200E200D20132011201220012903E81F200141E81F6A41086A290300200141E81F6A41106A350200200141C81F6A101C22040D01200141F80E6A41186A200141C81F6A41186A290300370300200120012903C81F3703F80E2001200141C81F6A41106A2903003703880F2001200141C81F6A41086A2903003703800F41000D020CA8010B20014180206A240041030F0B2004450DA6010B41004100100541011006000B20024120490D522003200141980F6A412010120240024002402002AD423F580D00200141980F6A41186A2903002105200141980F6A41106A2903002106200141980F6A41086A290300210720012903980F2108200341206A200141B80F6A41201012200141B80F6A41186A2903002109200141B80F6A41106A290300210A200141B80F6A41086A290300210B20012903B80F210C200141C81F6A1009200141C81F6A41186A290300210D200141C81F6A41106A290300210E200141C81F6A41086A290300210F20012903C81F2110200141E81F6A1007200141E81F6A41086A2903002111200141E81F6A41106A350200211220012903E81F2113200141901F6A100720082007200620052010200F200E200D200C200B200A200920132011201220012903901F200141901F6A41086A290300200141901F6A41106A350200200141A81F6A103922040D01200141D80F6A41186A200141A81F6A41186A290300370300200120012903A81F3703D80F2001200141B81F6A2903003703E80F2001200141B01F6A2903003703E00F41000D020CA6010B20014180206A240041030F0B2004450DA4010B41004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D542002411F4D0D55200320014198106A41201012200129039810200141A0106A290300200141A8106A290300200141B0106A290300200141B8106A102E450D5641004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D592002411F4D0D5A200320014198116A411810122002AD2205423F580D5B20014198116A41106A350200210620014198116A41086A29030021072001290398112108200341206A200141B0116A41181012200542DF00580D5C200141B0116A41106A3502002105200141B0116A41086A290300210920012903B011210A200341C0006A200141C8116A41201012200820072006200A2009200520012903C811200141D0116A290300200141D8116A290300200141E0116A290300200141EF116A1030450D5D41004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D5D2002411F4D0D5E2003200141F0116A412010122002AD2205423F580D5F200141F0116A41186A2903002106200141F0116A41106A2903002107200141F0116A41086A290300210820012903F0112109200341206A20014190126A41201012200542DF00580D6020014190126A41186A290300210A20014190126A41106A290300210B20014190126A41086A290300210C200129039012210D200341C0006A200141B0126A41201012200542FF00580D61200141B0126A41186A290300210E200141B0126A41106A290300210F200141B0126A41086A290300211020012903B0122111200341E0006A200141D0126A412010122005429F01580D62200141D0126A41186A2903002112200141D0126A41106A2903002113200141D0126A41086A290300211420012903D012211520034180016A200141F0126A41181012200542BF01580D63200141F0126A41106A3502002105200141F0126A41086A290300211620012903F0122117200341A0016A20014188136A411810122009200820072006200D200C200B200A20112010200F200E201520142013201220172016200520012903881320014188136A41086A29030020014188136A41106A350200200141A0136A1031450D6441004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D642002411F4D0D652003200141C0136A411810122002AD423F580D66200141C0136A41106A3502002105200141C0136A41086A290300210620012903C0132107200341206A200141D8136A4120101220072006200520012903D813200141D8136A41086A290300200141D8136A41106A290300200141D8136A41186A290300200141FF136A1032450D6741004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D762002411F4D0D77200320014188176A412010122002AD2205423F580D7820014188176A41186A290300210620014188176A41106A290300210720014188176A41086A29030021082001290388172109200341206A200141A8176A41201012200542DF00580D79200141A8176A41186A290300210A200141A8176A41106A290300210B200141A8176A41086A290300210C20012903A817210D200341C0006A200141C8176A41201012200542FF00580D7A200141C8176A41186A290300210E200141C8176A41106A290300210F200141C8176A41086A290300211020012903C8172111200341E0006A200141E8176A412010122005429F01580D7B200141E8176A41186A2903002112200141E8176A41106A2903002113200141E8176A41086A290300211420012903E817211520034180016A20014188186A41181012200542BF01580D7C20014188186A41106A350200210520014188186A41086A29030021162001290388182117200341A0016A200141A0186A411810122009200820072006200D200C200B200A20112010200F200E201520142013201220172016200520012903A018200141A0186A41086A290300200141A0186A41106A350200200141B8186A1035450D7D41004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D7D2002411F4D0D7E2003200141D8186A412010122002AD2205423F580D7F200141D8186A41186A2903002106200141D8186A41106A2903002107200141D8186A41086A290300210820012903D8182109200341206A200141F8186A41201012200542DF00580D8001200141F8186A41186A2903002105200141F8186A41106A290300210A200141F8186A41086A290300210B20012903F818210C200341C0006A20014198196A412010122009200820072006200C200B200A2005200129039819200141A0196A290300200141A8196A290300200141B0196A290300200141B8196A1019450D810141004100100541011006000B20014180206A240041030F0B2004450DA0010B41004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D83012002411F4D0D84012003200141D01A6A4120101220012903D01A200141D81A6A290300200141E01A6A290300200141E81A6A290300200141F01A6A1037450D850141004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D8D012002411F4D0D8E012003200141D81C6A412010122002AD2205423F580D8F01200141D81C6A41186A2903002106200141D81C6A41106A2903002107200141D81C6A41086A290300210820012903D81C2109200341206A200141F81C6A41201012200542DF00580D9001200141F81C6A41186A2903002105200141F81C6A41106A290300210A200141F81C6A41086A290300210B20012903F81C210C200341C0006A200141981D6A412010122009200820072006200C200B200A200520012903981D200141A01D6A290300200141A81D6A290300200141B01D6A290300200141B81D6A1027450D910141004100100541011006000B200041606A22042400200410092004290300200441106A29030084200441086A290300200441186A29030084844200520D91012002411F4D0D92012003200141D81D6A412010122002AD2205423F580D9301200141D81D6A41186A2903002106200141D81D6A41106A2903002107200141D81D6A41086A290300210820012903D81D2109200341206A200141F81D6A41201012200542DF00580D9401200141F81D6A41186A290300210A200141F81D6A41106A290300210B200141F81D6A41086A290300210C20012903F81D210D200341C0006A200141981E6A41201012200542FF00580D9501200141981E6A41186A290300210E200141981E6A41106A290300210F200141981E6A41086A290300211020012903981E2111200341E0006A200141B81E6A412010120240024002402005429F01580D00200141B81E6A41186A2903002105200141B81E6A41106A2903002112200141B81E6A41086A290300211320012903B81E211420034180016A200141D81E6A41181012200141D81E6A41086A2903002115200141D81E6A41106A350200211620012903D81E2117200141A81F6A1007200141A81F6A41086A2903002118200141A81F6A41106A350200211920012903A81F211A200141E81F6A10072009200820072006200D200C200B200A20112010200F200E2014201320122005201A2018201920012903E81F200141E81F6A41086A290300200141E81F6A41106A350200201720152016200141C81F6A102622040D01200141F01E6A41186A200141C81F6A41186A290300370300200120012903C81F3703F01E2001200141C81F6A41106A2903003703801F2001200141C81F6A41086A2903003703F81E41000D020C99010B20014180206A240041030F0B2004450D97010B41004100100541011006000B41004100100541011006000B20014180206A240041030F0B200141286A4120101122044120101320044120100541001006000B41004100100541011006000B200141C8006A4120101122044114101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B41004100100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B200141F0016A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B200141E8036A4120101122044120101320044120100541001006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B200141E0046A4120101122044120101320044120100541001006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B200141E0056A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180076A41C0001011220441201013200141A0076A200441206A41201013200441C000100541001006000B41004100100541011006000B200141C0076A4120101122044114101320044120100541001006000B41004100100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014188096A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B412010112204411F6A20012D009F0B3A000020044120100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B200141980C6A4120101122044120101320044120100541001006000B20014180206A240041030F0B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B41004100100541011006000B200141F80F6A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B200141B8106A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B200141F8106A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B412010112204411F6A20012D00EF113A000020044120100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B200141A0136A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B412010112204411F6A20012D00FF133A000020044120100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014198156A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B200141E8166A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B200141B8186A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B200141B8196A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B200141F0196A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B41004100100541011006000B20014180206A240041030F0B200141F01A6A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B200141C01B6A4120101122044120101320044120100541001006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B200141B81C6A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B200141B81D6A4120101122044120101320044120100541001006000B41004100100541011006000B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B20014180206A240041030F0B200141F01E6A4120101122044120101320044120100541001006000B200141D80F6A4120101122044120101320044120100541001006000B200141F80E6A4120101122044120101320044120100541001006000B200141F80D6A4120101122044120101320044120100541001006000B200141C00A6A4120101122044120101320044120100541001006000B412010112204411F6A20012D00CF023A000020044120100541001006000B412010112204411F6A20012D00CF1A3A000020044120100541001006000B200141F80C6A4120101122044120101320044120100541001006000B0B79010041000B73494E56414C49445F4144445245535300536166654D617468237375623A20554E444552464C4F57000000000000000000536166654D617468236164643A204F564552464C4F5700000000000000000000536166654D617468236D756C3A204F564552464C4F57494E56414C49445F56414C554500740970726F647563657273010C70726F6365737365642D62790105636C616E675431302E302E3120286769743A2F2F6769746875622E636F6D2F6C6C766D2F6C6C766D2D70726F6A65637420623661313733343336373838653638333239636335653965653066363531623630336136333765332900DE17046E616D6501D6173C000B6765745F61646472657373010C6C6F61645F73746F72616765020F696E766F6B655F636F6E7472616374030F6765745F72657475726E5F73697A650411636F70795F72657475726E5F76616C7565050A7365745F72657475726E060B73797374656D5F68616C74070A6765745F73656E646572080C736176655F73746F7261676509156765745F7472616E736665727265645F66756E64730A1063727970746F5F6B656363616B3235360B0977726974655F6C6F670C0D6765745F63616C6C5F73697A650D0F636F70795F63616C6C5F76616C75650E085F5F6D656D6370790F085F5F627A65726F38100B5F5F696E69745F6865617011085F5F6D616C6C6F63120B5F5F62653332746F6C654E130B5F5F6C654E746F6265333214075F5F6D756C333215095F5F6173686C74693316095F5F6C736872746933170A766563746F725F6E65771841736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A676574457468546F546F6B656E4F757470757450726963655F5F75696E743235361947736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A6765744F757470757450726963655F5F75696E743235365F75696E743235365F75696E743235361A2E736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A73657475705F5F616464726573731B59736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A746F6B656E546F4574685472616E736665724F75747075745F5F75696E743235365F75696E743235365F75696E743235365F616464726573731C59736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A746F6B656E546F4574684F75747075745F5F75696E743235365F75696E743235365F75696E743235365F616464726573735F616464726573731D5E736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A746F6B656E546F546F6B656E53776170496E7075745F5F75696E743235365F75696E743235365F75696E743235365F75696E743235365F616464726573731E6A736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A746F6B656E546F546F6B656E496E7075745F5F75696E743235365F75696E743235365F75696E743235365F75696E743235365F616464726573735F616464726573735F616464726573731F50736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A657468546F546F6B656E5472616E73666572496E7075745F5F75696E743235365F75696E743235365F616464726573732058736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A657468546F546F6B656E496E7075745F5F75696E743235365F75696E743235365F75696E743235365F616464726573735F616464726573732145736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A6164644C69717569646974795F5F75696E743235365F75696E743235365F75696E74323536222D736F6C3A3A66756E6374696F6E3A3A536166654D6174683A3A7375625F5F75696E743235365F75696E74323536232D736F6C3A3A66756E6374696F6E3A3A536166654D6174683A3A6D756C5F5F75696E743235365F75696E74323536242D736F6C3A3A66756E6374696F6E3A3A536166654D6174683A3A6164645F5F75696E743235365F75696E743235362550736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A72656D6F76654C69717569646974795F5F75696E743235365F75696E743235365F75696E743235365F75696E74323536266B736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A746F6B656E546F546F6B656E4F75747075745F5F75696E743235365F75696E743235365F75696E743235365F75696E743235365F616464726573735F616464726573735F616464726573732746736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A676574496E70757450726963655F5F75696E743235365F75696E743235365F75696E74323536286B736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A746F6B656E546F546F6B656E5472616E736665724F75747075745F5F75696E743235365F75696E743235365F75696E743235365F75696E743235365F616464726573735F616464726573732938736F6C3A3A66756E6374696F6E3A3A45524332303A3A696E637265617365416C6C6F77616E63655F5F616464726573735F75696E743235362A37736F6C3A3A66756E6374696F6E3A3A45524332303A3A5F617070726F76655F5F616464726573735F616464726573735F75696E743235362B58736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A746F6B656E546F4574685472616E73666572496E7075745F5F75696E743235365F75696E743235365F75696E743235365F616464726573732C58736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A746F6B656E546F457468496E7075745F5F75696E743235365F75696E743235365F75696E743235365F616464726573735F616464726573732D38736F6C3A3A66756E6374696F6E3A3A45524332303A3A5F7472616E736665725F5F616464726573735F616464726573735F75696E743235362E40736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A676574546F6B656E546F457468496E70757450726963655F5F75696E743235362F40736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A676574457468546F546F6B656E496E70757450726963655F5F75696E74323536303B736F6C3A3A66756E6374696F6E3A3A45524332303A3A7472616E7366657246726F6D5F5F616464726573735F616464726573735F75696E74323536316D736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A746F6B656E546F45786368616E67655472616E73666572496E7075745F5F75696E743235365F75696E743235365F75696E743235365F75696E743235365F616464726573735F616464726573733238736F6C3A3A66756E6374696F6E3A3A45524332303A3A6465637265617365416C6C6F77616E63655F5F616464726573735F75696E74323536335F736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A746F6B656E546F546F6B656E537761704F75747075745F5F75696E743235365F75696E743235365F75696E743235365F75696E743235365F61646472657373346E736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A746F6B656E546F45786368616E67655472616E736665724F75747075745F5F75696E743235365F75696E743235365F75696E743235365F75696E743235365F616464726573735F61646472657373356A736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A746F6B656E546F546F6B656E5472616E73666572496E7075745F5F75696E743235365F75696E743235365F75696E743235365F75696E743235365F616464726573735F616464726573733628736F6C3A3A66756E6374696F6E3A3A45524332303A3A62616C616E63654F665F5F616464726573733741736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A676574546F6B656E546F4574684F757470757450726963655F5F75696E743235363830736F6C3A3A66756E6374696F6E3A3A45524332303A3A616C6C6F77616E63655F5F616464726573735F616464726573733959736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A657468546F546F6B656E4F75747075745F5F75696E743235365F75696E743235365F75696E743235365F616464726573735F616464726573733A51736F6C3A3A66756E6374696F6E3A3A556E697377617045786368616E67653A3A657468546F546F6B656E5472616E736665724F75747075745F5F75696E743235365F75696E743235365F616464726573733B057374617274"
                    .HexToBytes()
            );
            if (!VirtualMachine.VerifyContract(uniswapExchangeContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // ERC20
            var erc20Address = "0xfd893ce89186fc6861d339cb6ab5d75458e3daf3".HexToBytes().ToUInt160();
            var erc20Contract = new Contract
            (
                erc20Address,
                "0061736D01000000016C0D60037F7F7F0060027F7F0060017F006000017F60000060017F017F60037F7F7F017F60047E7E7E7F017F60097E7E7E7E7E7E7E7E7F017F60087E7E7E7E7E7E7E7F017F600A7E7E7E7E7E7E7E7E7E7E017F60077E7E7E7E7E7E7F017F600B7E7E7E7E7E7E7E7E7E7E7F017F02C8010A03656E761063727970746F5F6B656363616B323536000003656E760C6C6F61645F73746F72616765000103656E760A7365745F72657475726E000103656E760B73797374656D5F68616C74000203656E760A6765745F73656E646572000203656E760C736176655F73746F72616765000103656E760977726974655F6C6F67000103656E76156765745F7472616E736665727265645F66756E6473000203656E760D6765745F63616C6C5F73697A65000303656E760F636F70795F63616C6C5F76616C75650000031110000405000006070809080A0B0A0C09040405017001010105030100020608017F01418080040B071202066D656D6F7279020005737461727400190ACE38102E0002402002450D000340200020012D00003A0000200041016A2100200141016A21012002417F6A22020D000B0B0B2E004100410036028080044100410036028480044100410036028C800441003F0041107441F0FF7B6A36028880040BA60101047F418080042101024003400240200128020C0D002001280208220220004F0D020B200128020022010D000B41002101410028020821020B02402002200041076A41787122036B22024118490D00200120036A41106A22002001280200220436020002402004450D00200420003602040B2000200241706A3602082000410036020C2000200136020420012000360200200120033602080B2001410136020C200141106A0B2D002000411F6A21000340200120002D00003A0000200141016A21012000417F6A21002002417F6A22020D000B0B2D002001411F6A21010340200120002D00003A00002001417F6A2101200041016A21002002417F6A22020D000B0B7E01017F200120006C220141086A100C2203200036020420032000360200200341086A2100024002402002417F460D002001450D010340200020022D00003A0000200041016A2100200241016A21022001417F6A22010D000C020B0B2001450D000340200041003A0000200041016A21002001417F6A22010D000B0B20030BEC0101027F230041E0006B22042400200441406A22052400200541286A2001370300200541206A2000370300200541306A20023E0200200541186A4200370300200542003703102005420037030820054200370300200541382004220441C0006A1000200441206A41186A200441C0006A41186A2903003703002004200441C0006A41106A2903003703302004200441C0006A41086A29030037032820042004290340370320200441206A20041001200341186A200441186A2903003703002003200441106A2903003703102003200441086A29030037030820032004290300370300200441E0006A240041000B940203027F017E027F23002209210A0240200020047C220B200054220C200120057C200CAD7C220420015420042001511B220C200220067C2200200CAD7C2201200254200320077C2000200254AD7C2001200054AD7C220020035420002003511B2001200285200020038584501B0D002008200B3703002008200437030820082001370310200841186A2000370300200A240041000F0B411641014100100F220A280200413F6A41607141246A220D100C220841A0F38DC600360200200941706A220C22092400200C4120360200200C200841046A4104100E200A280200210C200941706A220924002009200C3602002009200841246A4104100E200841C4006A200A41086A200C100A2008200D100241011003000BB80404027F037E017F017E230041C0016B220824002008220941A8016A1004200941A8016A41106A350200210A200941A8016A41086A290300210B20092903A801210C200841406A2208220D2400200841286A200B370300200841206A200C370300200841306A200A3E0200200841186A42003703002008420037031020084200370308200842013703002008413820094188016A100020094188016A41086A290300210A20094188016A41106A290300210B20094188016A41186A290300210C200929038801210E200D41406A2208220D2400200841286A2001370300200841206A2000370300200841306A20023E0200200841186A200C3703002008200B3703102008200A3703082008200E37030020084138200941E8006A1000200941C8006A41186A200941E8006A41186A2903003703002009200941E8006A41106A2903003703582009200941E8006A41086A29030037035020092009290368370348200941C8006A200941286A1001024002402009290328200941286A41086A290300200941286A41106A290300200941286A41186A2903002003200420052006200941086A101322080D00200941086A41186A2903002103200941086A41106A2903002104200941086A41086A290300210520092903082106200D41606A22082400200810042008290300200841086A290300200841106A350200200020012002200620052004200310142208450D01200941C0016A240020080F0B200941C0016A240020080F0B200741013A0000200941C0016A240041000B950201047F23002209210A0240200420005620052001562005200151220B1B2006200256200720035620072003511B2006200285200720038584501B0D002008200020047D3703002008200120057D20002004542209AD7D3703082008200220067D220420092001200554200B1BAD22057D370310200841186A200320077D2002200654AD7D2004200554AD7D370300200A240041000F0B411741014120100F220A280200413F6A41607141246A220C100C220841A0F38DC600360200200941706A220B22092400200B4120360200200B200841046A4104100E200A280200210B200941706A220924002009200B3602002009200841246A4104100E200841C4006A200A41086A200B100A2008200C100241011003000BC80403047F047E017F2300220A210B024002402003200542FFFFFFFF0F8384200484500D002000200242FFFFFFFF0F83842001844200520D0141004100100241011003000B41004100100241011003000B200A41406A220A220C2400200A41286A2001370300200A41206A2000370300200A41306A20023E0200200A41186A4200370300200A4200370310200A4200370308200A4201370300200C41606A220C220D2400200A4138200C1000200C290300210E200C41086A290300210F200C41106A2903002110200C41186A2903002111200D41406A220A220C2400200A41286A2004370300200A41206A2003370300200A41306A20053E0200200A41186A2011370300200A2010370310200A200F370308200A200E370300200C41606A220C220D2400200A4138200C1000200C290300210E200C41086A290300210F200C41106A2903002110200C41186A2903002111200D41606A220A220C2400200A41186A2011370300200A2010370310200A200F370308200A200E370300200C41606A220C220D2400200C41186A2009370300200C2008370310200C2007370308200C2006370300200A200C10054120100C210C200D41606A220A220D2400200A41186A2009370300200A2008370310200A2007370308200A2006370300200A200C4120100E4120100C2112200D41606A220A220D2400200A2001370308200A2000370300200A20023E0210200A20124114100E4120100C2112200D41606A220A2400200A2004370308200A2003370300200A20053E0210200A20124114100E200C41201006200B240041000BF30202037F017E23004180016B22072400200741406A220822092400200841286A2001370300200841206A2000370300200841306A20023E0200200841186A4200370300200842003703102008420037030820084201370300200841382007220741E0006A1000200741E0006A41086A2903002102200741E0006A41106A2903002100200741E0006A41186A29030021012007290360210A200941406A22082400200841286A2004370300200841206A2003370300200841306A20053E0200200841186A200137030020082000370310200820023703082008200A37030020084138200741C0006A1000200741206A41186A200741C0006A41186A2903003703002007200741C0006A41106A2903003703302007200741C0006A41086A29030037032820072007290340370320200741206A20071001200641186A200741186A2903003703002006200741106A2903003703102006200741086A2903003703082006200729030037030020074180016A240041000BE50903047F087E017F2300220A210B0240024002402003200542FFFFFFFF0F8384200484500D00200A41406A220A220C2400200A41286A2001370300200A41206A2000370300200A41306A20023E0200200A41186A4200370300200A4200370310200A4200370308200A4200370300200C41606A220C220D2400200A4138200C1000200C290300210E200C41086A290300210F200C41106A2903002110200C41186A2903002111200D41606A220A220C2400200A41186A2011370300200A2010370310200A200F370308200A200E370300200C41606A220C220D2400200A200C1001200C41186A290300210E200C41106A290300210F200C41086A2903002110200C2903002111200D41606A220A220D240020112010200F200E2006200720082009200A1013220C0D01200A41086A290300210E200A41106A290300210F200A41186A2903002110200A2903002111200D41406A220A220C2400200A41286A2001370300200A41206A2000370300200A41306A20023E0200200A41186A4200370300200A4200370310200A4200370308200A4200370300200C41606A220C220D2400200A4138200C1000200C2903002112200C41086A2903002113200C41106A2903002114200C41186A2903002115200D41606A220A220C2400200A41186A2015370300200A2014370310200A2013370308200A2012370300200C41606A220C220D2400200C41186A2010370300200C200F370310200C200E370308200C2011370300200A200C1005200D41406A220A220C2400200A41286A2004370300200A41206A2003370300200A41306A20053E0200200A41186A4200370300200A4200370310200A4200370308200A4200370300200C41606A220C220D2400200A4138200C1000200C290300210E200C41086A290300210F200C41106A2903002110200C41186A2903002111200D41606A220A220C2400200A41186A2011370300200A2010370310200A200F370308200A200E370300200C41606A220C220D2400200A200C1001200C41186A290300210E200C41106A290300210F200C41086A2903002110200C2903002111200D41606A220A220D240020112010200F200E2006200720082009200A1011220C450D02200B2400200C0F0B41004100100241011003000B200B2400200C0F0B200A41086A290300210E200A41106A290300210F200A41186A2903002110200A2903002111200D41406A220A220C2400200A41286A2004370300200A41206A2003370300200A41306A20053E0200200A41186A4200370300200A4200370310200A4200370308200A4200370300200C41606A220C220D2400200A4138200C1000200C2903002112200C41086A2903002113200C41106A2903002114200C41186A2903002115200D41606A220A220C2400200A41186A2015370300200A2014370310200A2013370308200A2012370300200C41606A220C220D2400200C41186A2010370300200C200F370310200C200E370308200C2011370300200A200C10054120100C210C200D41606A220A220D2400200A41186A2009370300200A2008370310200A2007370308200A2006370300200A200C4120100E4120100C2116200D41606A220A220D2400200A2001370308200A2000370300200A20023E0210200A20164114100E4120100C2116200D41606A220A2400200A2004370308200A2003370300200A20053E0210200A20164114100E200C41201006200B240041000BDA0402047F047E2300220B210C02400240024020002001200220032004200520062007200820091016220D0D00200B41406A220D220B2400200D41286A2001370300200D41206A2000370300200D41306A20023E0200200D41186A4200370300200D4200370310200D4200370308200D4201370300200B41606A220B220E2400200D4138200B1000200B2903002103200B41086A2903002104200B41106A2903002105200B41186A290300210F200E41606A220D220B2400200D1004200D41106A3502002110200D2903002111200D41086A2903002112200B41406A220D220B2400200D41286A2012370300200D41206A2011370300200D41306A20103E0200200D41186A200F370300200D2005370310200D2004370308200D2003370300200B41606A220B220E2400200D4138200B1000200B2903002103200B41086A2903002104200B41106A2903002105200B41186A290300210F200E41606A220D220B2400200D41186A200F370300200D2005370310200D2004370308200D2003370300200B41606A220B220E2400200D200B1001200B41186A2903002103200B41106A2903002104200B41086A2903002105200B290300210F200E41606A220D220E2400200F2005200420032006200720082009200D1013220B0D01200D41186A2903002106200D41106A2903002107200D41086A2903002108200D2903002109200E41606A220D2400200D1004200020012002200D290300200D41086A290300200D41106A35020020092008200720061014220D450D02200C2400200D0F0B200C2400200D0F0B200C2400200B0F0B200A41013A0000200C240041000BB80404027F037E017F017E230041C0016B220824002008220941A8016A1004200941A8016A41106A350200210A200941A8016A41086A290300210B20092903A801210C200841406A2208220D2400200841286A200B370300200841206A200C370300200841306A200A3E0200200841186A42003703002008420037031020084200370308200842013703002008413820094188016A100020094188016A41086A290300210A20094188016A41106A290300210B20094188016A41186A290300210C200929038801210E200D41406A2208220D2400200841286A2001370300200841206A2000370300200841306A20023E0200200841186A200C3703002008200B3703102008200A3703082008200E37030020084138200941E8006A1000200941C8006A41186A200941E8006A41186A2903003703002009200941E8006A41106A2903003703582009200941E8006A41086A29030037035020092009290368370348200941C8006A200941286A1001024002402009290328200941286A41086A290300200941286A41106A290300200941286A41186A2903002003200420052006200941086A101122080D00200941086A41186A2903002103200941086A41106A2903002104200941086A41086A290300210520092903082106200D41606A22082400200810042008290300200841086A290300200841106A350200200020012002200620052004200310142208450D01200941C0016A240020080F0B200941C0016A240020080F0B200741013A0000200941C0016A240041000BD90F02047F077E230041E0046B22002400200010070240024002400240024002400240024002400240024002400240024002400240024002400240024002402000290300200041106A29030084200041086A290300200041186A29030084844200520D00100B41001008220136023C41002001100C22023602404100200120021009024002400240024002400240024002400240200141034D0D004100200228020022033602382001417C6A2101200241046A21020240200341A2F0CAEB7D4A0D000240200341A3AF89BE7D4A0D0020034189BC9D9D7B460D03200341A98BF0DC7B470D0220014120490D162002200041C8026A4118100D2001AD423F580D06200041C8026A41106A3502002104200041C8026A41086A290300210520002903C8022106200241206A200041E0026A4120100D200041E0026A41186A2903002107200041E0026A41106A2903002108200041E0026A41086A290300210920002903E002210A200041C0046A100420002903C004200041C0046A41086A290300200041C0046A41106A350200200620052004200A200920082007101622010D07200041013A00870341000D080C1F0B200341A4AF89BE7D460D0320034198ACB4E87D470D01200041C0046A41186A4200370300200042003703D004200042003703C804200042023703C004200041C0046A200041A0046A1001200041D8016A41186A200041A0046A41186A2903003703002000200041B0046A2903003703E8012000200041A8046A2903003703E001200020002903A0043703D8014100450D1141004100100241011003000B0240200341DCC5B5F7034A0D00200341A3F0CAEB7D460D08200341F0C08A8C03470D0120014120490D0C2002200041E0006A4118100D2000290360200041E8006A290300200041F0006A350200200041F8006A1010450D0D41004100100241011003000B200341DDC5B5F703460D03200341B9A0CD8C05460D080B41004100100241011003000B20014120490D082002200041206A4118100D0240024002402001AD423F580D00200041206A41106A3502002104200041206A41086A290300210520002903202106200241206A200041386A4120100D200041386A41186A2903002107200041386A41106A2903002108200041386A41086A29030021092000290338210A200041C0046A100420002903C004200041C0046A41086A290300200041C0046A41106A350200200620052004200A200920082007101422010D01200041013A005F41000D020C1D0B200041E0046A240041030F0B2001450D1B0B41004100100241011003000B20014120490D0A200220004198016A4118100D2001AD423F580D0B20004198016A41106A350200210420004198016A41086A29030021052000290398012106200241206A200041B0016A4120100D20062005200420002903B001200041B0016A41086A290300200041B0016A41106A290300200041B0016A41186A290300200041D7016A1012450D0C41004100100241011003000B20014120490D0D2002200041F8016A4118100D2001AD423F580D0E200041F8016A41106A3502002104200041F8016A41086A290300210520002903F8012106200241206A20004190026A4118100D20062005200420002903900220004190026A41086A29030020004190026A41106A350200200041A8026A1015450D0F41004100100241011003000B200041E0046A240041030F0B2001450D170B41004100100241011003000B20014120490D0D200220004188036A4118100D2001AD2204423F580D0E20004188036A41106A350200210520004188036A41086A29030021062000290388032107200241206A200041A0036A4118100D200442DF00580D0F200041A0036A41106A3502002104200041A0036A41086A290300210820002903A0032109200241C0006A200041B8036A4120100D20072006200520092008200420002903B803200041C0036A290300200041C8036A290300200041D0036A290300200041DF036A1017450D1041004100100241011003000B20014120490D102002200041E0036A4118100D2001AD423F580D11200041E0036A41106A3502002104200041E0036A41086A290300210520002903E0032106200241206A200041F8036A4120100D20062005200420002903F803200041F8036A41086A290300200041F8036A41106A290300200041F8036A41186A2903002000419F046A1018450D1241004100100241011003000B41004100100241011003000B200041E0046A240041030F0B200041E0046A240041030F0B200041F8006A4120100C22004120100E20004120100241001003000B200041E0046A240041030F0B200041E0046A240041030F0B4120100C2201411F6A20002D00D7013A000020014120100241001003000B200041D8016A4120100C22004120100E20004120100241001003000B200041E0046A240041030F0B200041E0046A240041030F0B200041A8026A4120100C22004120100E20004120100241001003000B200041E0046A240041030F0B200041E0046A240041030F0B200041E0046A240041030F0B200041E0046A240041030F0B4120100C2201411F6A20002D00DF033A000020014120100241001003000B200041E0046A240041030F0B200041E0046A240041030F0B4120100C2201411F6A20002D009F043A000020014120100241001003000B4120100C2201411F6A20002D005F3A000020014120100241001003000B4120100C2201411F6A20002D0087033A000020014120100241001003000B0B3D010041000B37536166654D617468236164643A204F564552464C4F5700000000000000000000536166654D617468237375623A20554E444552464C4F5700740970726F647563657273010C70726F6365737365642D62790105636C616E675431302E302E3120286769743A2F2F6769746875622E636F6D2F6C6C766D2F6C6C766D2D70726F6A65637420623661313733343336373838653638333239636335653965653066363531623630336136333765332900CA05046E616D6501C2051A001063727970746F5F6B656363616B323536010C6C6F61645F73746F72616765020A7365745F72657475726E030B73797374656D5F68616C74040A6765745F73656E646572050C736176655F73746F72616765060977726974655F6C6F6707156765745F7472616E736665727265645F66756E6473080D6765745F63616C6C5F73697A65090F636F70795F63616C6C5F76616C75650A085F5F6D656D6370790B0B5F5F696E69745F686561700C085F5F6D616C6C6F630D0B5F5F62653332746F6C654E0E0B5F5F6C654E746F626533320F0A766563746F725F6E65771028736F6C3A3A66756E6374696F6E3A3A45524332303A3A62616C616E63654F665F5F61646472657373112D736F6C3A3A66756E6374696F6E3A3A536166654D6174683A3A6164645F5F75696E743235365F75696E743235361238736F6C3A3A66756E6374696F6E3A3A45524332303A3A6465637265617365416C6C6F77616E63655F5F616464726573735F75696E74323536132D736F6C3A3A66756E6374696F6E3A3A536166654D6174683A3A7375625F5F75696E743235365F75696E743235361437736F6C3A3A66756E6374696F6E3A3A45524332303A3A5F617070726F76655F5F616464726573735F616464726573735F75696E743235361530736F6C3A3A66756E6374696F6E3A3A45524332303A3A616C6C6F77616E63655F5F616464726573735F616464726573731638736F6C3A3A66756E6374696F6E3A3A45524332303A3A5F7472616E736665725F5F616464726573735F616464726573735F75696E74323536173B736F6C3A3A66756E6374696F6E3A3A45524332303A3A7472616E7366657246726F6D5F5F616464726573735F616464726573735F75696E743235361838736F6C3A3A66756E6374696F6E3A3A45524332303A3A696E637265617365416C6C6F77616E63655F5F616464726573735F75696E7432353619057374617274"
                    .HexToBytes()
            );
            if (!VirtualMachine.VerifyContract(erc20Contract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            var snapshot = stateManager.NewSnapshot();
            snapshot.Contracts.AddContract(UInt160Utils.Zero, uniswapFactoryContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, uniswapExchangeContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, erc20Contract);
            stateManager.Approve();

            for (var i = 0; i < 1; ++i)
            {
                var currentTime = TimeUtils.CurrentTimeMillis();
                var currentSnapshot = stateManager.NewSnapshot();

                var sender = UInt160Utils.Zero;

                currentSnapshot.Balances.AddBalance(UInt160Utils.Zero, 100.ToUInt256().ToMoney());

                var transactionReceipt = new TransactionReceipt();
                transactionReceipt.Transaction = new Transaction();
                transactionReceipt.Transaction.Value = 0.ToUInt256();
                var context = new InvocationContext(sender, currentSnapshot, transactionReceipt);

                {
                    Console.WriteLine($"\nUniswapFactory: createExchange({erc20Address.ToHex()},{uniswapExchangeAddress.ToHex()})");
                    var input = ContractEncoder.Encode("createExchange(address,address)", erc20Address, uniswapExchangeAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(uniswapFactoryContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nUniswapExchange: getInputPrice({10},{100},{1000})");
                    var input = ContractEncoder.Encode("getInputPrice(uint256,uint256,uint256)", 10.ToUInt256(), 100.ToUInt256(), 1000.ToUInt256());
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(uniswapExchangeContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }

                stateManager.Approve();
            exit_mark:
                var elapsedTime = TimeUtils.CurrentTimeMillis() - currentTime;
                Console.WriteLine("Elapsed Time: " + elapsedTime + "ms");
            }
        }

        [Test]
        public void Test_VirtualMachine_InvokeAllFeaturesContract()
        {
            var stateManager = _container.Resolve<IStateManager>();

            var address = UInt160Utils.Zero;
            var contract = new Contract
            (
                address,
                "0061736D01000000013A0A6000017F60037F7F7F0060017F0060027F7F0060057F7F7F7F7F0060017F017F60000060047F7F7F7F0060047F7E7E7F0060047F7F7F7F017F02FA021103656E760D6765745F63616C6C5F73697A65000003656E760F636F70795F63616C6C5F76616C7565000103656E760C6765745F6D736776616C7565000203656E761A6765745F626C6F636B5F636F696E626173655F61646472657373000203656E760A7365745F72657475726E000303656E760B73797374656D5F68616C74000203656E76106765745F626C6F636B5F6E756D626572000203656E760D6765745F74785F6F726967696E000203656E760A6765745F73656E646572000203656E760C6765745F636861696E5F6964000203656E760C6765745F6761735F6C656674000203656E76136765745F626C6F636B5F6761735F6C696D6974000203656E76146765745F65787465726E616C5F62616C616E6365000303656E76106765745F74785F6761735F7072696365000203656E76136765745F626C6F636B5F74696D657374616D70000203656E76146765745F626C6F636B5F646966666963756C7479000203656E760E63727970746F5F7265636F7665720004030D0C0301010101050607080809060405017001010105030100020608017F01418080040B071202066D656D6F72790200057374617274001C0AB8490C240002402001450D00034020004200370300200041086A21002001417F6A22010D000B0B0B2D002000411F6A21000340200120002D00003A0000200141016A21012000417F6A21002002417F6A22020D000B0B29002000417F6A210003402001200020026A2D00003A0000200141016A21012002417F6A22020D000B0B2D002001411F6A21010340200120002D00003A00002001417F6A2101200041016A21002002417F6A22020D000B0B29002001417F6A21010340200120026A20002D00003A0000200041016A21002002417F6A22020D000B0BA60101047F418080042101024003400240200128020C0D002001280208220220004F0D020B200128020022010D000B41002101410028020821020B02402002200041076A41787122036B22024118490D00200120036A41106A22002001280200220436020002402004450D00200420003602040B2000200241706A3602082000410036020C2000200136020420012000360200200120033602080B2001410136020C200141106A0B2E004100410036028080044100410036028480044100410036028C800441003F0041107441F0FF7B6A36028880040B850304077F017E037F027E2003411F752003712104200341027420006A417C6A210520032106024003400240200641014E0D00200421070C020B2006417F6A2106200528020021082005417C6A21052008450D000B200641016A21070B200341027420016A417C6A2105200321060240034020064101480D012006417F6A2106200528020021082005417C6A21052008450D000B200641016A21040B024020034101480D002001417C6A21094100210A4200210B4100210C41002105410021010340200A20044E210D024002402005200520044822086A220E2001200A20074E6A22014B0D004200210F200B21100C010B2000200C200D6A4102746A21062009200520086A4102746A21054200210F200E21080340200F4280808080107C200F200B200535020020063502007E7C2210200B541B210F200641046A21062005417C6A21052010210B2008417F6A220820014A0D000B0B200C200D6A210C2002200A4102746A20103E02002010422088200F84210B200E2105200A41016A220A2003470D000B0B0B5301017E02402003450D000240200341C00071450D0020012003413F71AD862102420021010C010B200141C00020036BAD8820022003AD220486842102200120048621010B20002002370308200020013703000B5301017E02402003450D000240200341C00071450D0020022003413F71AD882101420021020C010B200241C00020036BAD8620012003AD220488842101200220048821020B20002002370308200020013703000B932406017F167E057F1D7E017F017E230041F0026B22042400200041386A2903002105200041306A2903002106200041286A2903002107200041206A2903002108200041186A2903002109200041106A290300210A200041086A290300210B2000290300210C0240024002402001290300220D420156200141086A290300220E420052200E501B200141106A290300220F420052200141186A29030022104200522010501B200F201084501B200141206A2903002211420052200141286A29030022124200522012501B200141306A2903002213420052200141386A29030022144200522014501B2013201484501B2011201384201220148484501B0D00410121000240200DA70E020300030B20024200370320200242003703102002420037030820024200370300200241386A4200370300200241306A4200370300200241286A4200370300200241186A42003703002003200A3703102003200C3703002003200B370308200341186A200937030020032008370320200341286A2007370300200341306A2006370300200341386A20053703000C010B0240200D200C852011200885221584200F200A852216201320068522178484200E200B852012200785221884201020098522192014200585221A8484844200520D0020024200370320200242003703102002420037030820024200370300200241386A4200370300200241306A4200370300200241286A4200370300200241186A4200370300200341306A4200370300200341386A420037030020034200370320200341286A420037030020034200370310200341186A420037030020034201370300200342003703080C010B02400240200C200884200A20068484200B20078420092005848484500D00200C200D5A200B200E5A200B200E511B200A200F5A200920105A20092010511B2016201984501B200820115A200720125A20072012511B200620135A200520145A20052014511B2017201A84501B20152017842018201A8484501B0D010B2002200C3703002002200B3703082002200A370310200241186A200937030020022008370320200241286A2007370300200241306A2006370300200241386A2005370300200341306A4200370300200341386A420037030020034200370320200341286A420037030020034200370310200341186A420037030020034200370300200342003703080C010B41C0032100200521170240024020054200520D00418003210020062117200650450D0041C00221002007211720074200520D0041800221002008211720084200520D0041C00121002009211720094200520D004180012100200A2117200A4200520D0041C0002100200B2117200B4200520D0041002100200C2117200C4200510D010B411F413F20174280808080105422011B221B41706A201B2017422086201720011B221742808080808080C0005422011B221B41786A201B2017421086201720011B2217428080808080808080015422011B221B417C6A201B2017420886201720011B2217428080808080808080105422011B221B417E6A201B2017420486201720011B2217428080808080808080C0005422011B20006A2017420286201720011B423F87A7417F736A21000B41C0032101201421170240024020144200520D00418003210120132117201350450D0041C00221012012211720124200520D0041800221012011211720114200520D0041C00121012010211720104200520D004180012101200F2117200F4200520D0041C0002101200E2117200E4200520D0041002101200D2117200D4200510D010B411F413F201742808080801054221B1B221C41706A201C20174220862017201B1B221742808080808080C00054221B1B221C41786A201C20174210862017201B1B22174280808080808080800154221B1B221C417C6A201C20174208862017201B1B22174280808080808080801054221B1B221C417E6A201C20174204862017201B1B2217428080808080808080C00054221B1B20016A20174202862017201B1B423F87A7417F736A21010B200441D0016A200D200E418003200020016B22006B101A200441C0016A200F2010200041807E6A221D1019200441E0016A200D200E200041807D6A1019200441B0026A2011201241800120006B2201101A200441E0026A2013201420001019200441C0026A20112012200041807F6A221B1019200441A0016A200F201041800220006B221C101A200441F0016A200D200E2000101920044190026A200D200E2001101A20044180026A200F201020001019200441A0026A200D200E201B1019200441F0006A200D200E201C101A20044180016A200F2010418001201C6B101920044190016A200F20102001101A200441D0026A2011201220001019200441B0016A200D200E201D1019200441E0026A41086A290300200441B0026A41086A29030084200441C0026A41086A29030020004180014922011B201420001B200441A0016A41086A2903004200201C41800149221E1B84200441C0016A41086A290300200441D0016A41086A29030084200441E0016A41086A290300201D41800149221F1B2010201D1B200041800249221B1B211A20042903E00220042903B0028420042903C00220011B201320001B20042903A0014200201E1B8420042903C00120042903D0018420042903E001201F1B200F201D1B201B1B2116200441F0016A41086A290300420020011B211820042903F001420020011B211920044180026A41086A29030020044190026A41086A29030084200441A0026A41086A29030020011B201020001B21202004290380022004290390028420042903A00220011B200F20001B2121200441D0026A41086A290300420020011B200441F0006A41086A29030020044180016A41086A2903008420044190016A41086A290300201E1B200E201C1B84200441B0016A41086A2903004200201F1B201B1B212220042903D002420020011B200429037020042903800184200429039001201E1B200D201C1B8420042903B0014200201F1B201B1B212341C0032101200521170240024020054200520D00418003210120062117200650450D0041C00221012007211720074200520D0041800221012008211720084200520D0041C00121012009211720094200520D004180012101200A2117200A4200520D0041C0002101200B2117200B4200520D0041002101200C2117200C4200510D010B411F413F201742808080801054221C1B221D41706A201D20174220862017201C1B221742808080808080C00054221C1B221D41786A201D20174210862017201C1B22174280808080808080800154221C1B221D417C6A201D20174208862017201C1B22174280808080808080801054221C1B221D417E6A201D20174204862017201C1B2217428080808080808080C00054221C1B20016A20174202862017201C1B423F87A7417F736A21010B201A201420001B21152016201320001B212420184200201B1B212520194200201B1B212620204200201B1B211A20214200201B1B21272022201220001B21202023201120001B212841C0032100201421170240024020144200520D00418003210020132117201350450D0041C00221002012211720124200520D0041800221002011211720114200520D0041C00121002010211720104200520D004180012100200F2117200F4200520D0041C0002100200E2117200E4200520D0041002100200D2117200D4200510D010B411F413F201742808080801054221B1B221C41706A201C20174220862017201B1B221742808080808080C00054221B1B221C41786A201C20174210862017201B1B22174280808080808080800154221B1B221C417C6A201C20174208862017201B1B22174280808080808080801054221B1B221C417E6A201C20174204862017201B1B2217428080808080808080C00054221B1B20006A20174202862017201B1B423F87A7417F736A21000B200441306A42014200200120006B220041807D6A1019200441206A4201420041800320006B101A20044201420041800220006B221B101A200441106A42014200200041807E6A221C1019200441D0006A42014200200041807F6A1019200441C0006A4201420041800120006B101A200441E0006A420142002000101920242026200C562025200B562025200B511B2027200A56201A200956201A2009511B2027200A85201A20098584501B2028200856202020075620202007511B2024200656201520055620152005511B202420068522172015200585221684501B2028200885201784202020078520168484501B221EAD2221882015420186201E413F73AD221786842116202820218820204201862017868421182027202188201A42018620178684211920262021882025420186201786842126420020042903202004290330201C41800149221D1B4200201C1B20004180024922011B420020001B22292021884200200441206A41086A290300200441306A41086A290300201D1B4200201C1B20011B420020001B222A42018620178684212220042903004200201B41800149221C1B4201201B1B20042903104200201D1B20011B420020001B222B202188200441086A2903004200201C1B4200201B1B200441106A41086A2903004200201D1B20011B420020001B222C42018620178684212320042903402004290350200041800149221B1B420020001B420020011B222D202188200441C0006A41086A290300200441D0006A41086A290300201B1B420020001B420020011B222E42018620178684212F20042903604200201B1B420020011B202188200441E0006A41086A2903004200201B1B420020011B223042018620178684213120202021882024420186201E417F73413F71AD223286842117201A202188202842018620328684211A20252021882027420186203286842120202C2021882029420186203286842124202E202188202B4201862032868421252030202188202D42018620328684213220152021882115202A20218821274200212E4200213042002133420021344200213542002136420021374200213803404200201A200C202654200B202054200B2020511B200A2019542009201A542009201A511B200A2019852009201A8584501B2008201854200720175420072017511B2006201654200520155420052015511B200620168522212005201585222884501B2008201885202184200720178520288484501B22001B21214200201920001B21294200202020001B212A4200202620001B212D20084200201820001B222B54210120074200201720001B222851211B2007202854211C4200201520001B213920064200201620001B222C54211D4200202720001B20388421384200202220001B20378421374200202420001B20368421364200202320001B20358421354200202520001B20348421344200202F20001B20338421334200203220001B20308421304200203120001B202E84212E20314201882032423F8684213120264201882020423F868421262032420188202F423F8684213220204201882019423F86842120202F4201882025423F8684212F2019420188201A423F8684211920254201882023423F86842125201A4201882018423F8684211A20234201882024423F8684212320184201882017423F8684211820244201882022423F8684212420174201882016423F8684211720224201882027423F8684212220164201882015423F8684211620274201882127201542018821152008202B7D223A200C202D542200200B202A54200B202A511B221E200A202954221F200920215420092021511B200A202985200920218584501BAD223B7D222B2108200720287D2001AD7D223C203A203B54223DAD7D222821072006202C7D223A2001201C201B1BAD223B7D223E203D4100203C501BAD223C7D222C2106200C202D7D220C200D5A200B202A7D2000AD7D220B200E5A200B200E511B200A20297D2229201EAD222A7D220A200F5A200920217D201FAD7D2029202A54AD7D220920105A20092010511B200A200F85200920108584501B202B20115A202820125A20282012511B202C20135A200520397D201DAD7D203A203B54AD7D203E203C54AD7D220520145A20052014511B202C20138522212005201485222984501B202B201185202184202820128520298484501B0D000B2003202E3703002003203037030820032033370310200341186A203437030020032035370320200341286A2036370300200341306A2037370300200341386A2038370300200241306A202C370300200241386A20053703002002202B370320200241286A20283703002002200A370310200241186A20093703002002200C3703002002200B3703080B410021000B200441F0026A240020000BC51D02057F027E230041A0076B2200210120002400101741001000220236020441002002101622033602084100200220031001024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240200241034D0D00410020032802002204360200200441D4EF88E606460D080240200441E38AF8A27D4A0D000240200441C9FFB7C4794A0D00200441B7F4F48978460D03200441B0A9EBCC78460D0D200441A4E9FBDA78470D02200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D19200141E0066A10032001200141E0066A41086A29030037039801200120012903E006370390012001200141E0066A41106A3502003E02A00141010D1A41004100100441011005000B0240200441D7B7ACCF7B4A0D00200441CAFFB7C479460D05200441AFC5B9E77A470D02200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D2C4100200141E0066A41041013200120012802E0063602DC0341010D2D41004100100441011005000B200441D8B7ACCF7B460D03200441A997D9807D470D01200141E0066A1002200141E8016A41186A200141E0066A41186A2903003703002001200141F0066A2903003703F8012001200141E8066A2903003703F001200120012903E0063703E8014100450D1E41004100100441011005000B0240200441AEC28ED7064A0D000240200441C9CC857B4A0D00200441E48AF8A27D460D0B200441CCAAD8DE7D470D02200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D20200141A0046A41386A4200370300200141A0046A41306A4200370300200141A0046A41286A4200370300200141A0046A41186A4200370300200141E0036A41386A4200370300200141E0036A41306A4200370300200141E0036A41286A4200370300200141E0036A41186A4200370300200142003703C004200142003703B004200142003703A80420014288ED3C3703A0042001420037038004200142003703F003200142003703E8032001420A3703E003200141E0036A200141A0046A200141E0046A41101018200141E0056A41386A4200370300200141E0056A41306A4200370300200141E0056A41286A4200370300200141E0056A41186A427F370300200141A0056A41386A200141E0046A41386A290300370300200141A0056A41306A200141E0046A41306A290300370300200141A0056A41286A200141E0046A41286A290300370300200141A0056A41186A200141E0046A41186A29030037030020014200370380062001427F3703F0052001427F3703E8052001427F3703E005200120014180056A2903003703C0052001200141E0046A41086A2903003703A805200120012903E0043703A0052001200141E0046A41106A2903003703B005200141A0056A200141E0056A200141A0066A200141E0066A101B22000D0820014188026A41186A200141E0066A41186A290300370300200120012903E006370388022001200141F0066A290300370398022001200141E8066A2903003703900241000D090C2E0B200441CACC857B460D05200441CEB4A69806470D01200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D10200142938084BFDEC4A5D32D370328200142D2DFC6C18CDEEDCF3F370320200142D0ABD9E7053E023041010D1141004100100441011005000B0240200441D0ACD881074A0D00200441AFC28ED706460D0B200441E9E1A1EF06470D01200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D1C200141E0066A1006200141C8016A41186A4200370300200142003703D801200142003703D001200120012903E0063703C80141010D1D41004100100441011005000B200441D1ACD88107460D0C20044187C6B89207460D050B41004100100441011005000B200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D0B200141E0066A10072001200141E0066A41086A290300370310200120012903E0063703082001200141E0066A41106A3502003E021841010D0C41004100100441011005000B200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D0E200141E0066A10082001200141E0066A41086A290300370340200120012903E0063703382001200141E0066A41106A3502003E024841010D0F41004100100441011005000B200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D0F200141E0066A1009200141D0006A41186A42003703002001420037036020014200370358200120012903E00637035041010D1041004100100441011005000B200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D10200141E0066A100A200141F0006A41186A4200370300200142003703800120014200370378200120012903E00637037041010D1141004100100441011005000B200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D13200141E0066A100B200141A8016A41186A4200370300200142003703B801200142003703B001200120012903E0063703A80141010D1441004100100441011005000B2000450D250B41004100100441011005000B200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D162002417C6A411F4D0D17200341046A200141A8026A41181012200141A8026A41106A350200210520012903A80221062001200141A8026A41086A2903003703A806200120063703A006200120053E02B006200141A0066A200141E0066A100C200141C0026A41186A200141E0066A41186A2903003703002001200141E0066A41106A2903003703D0022001200141E0066A41086A2903003703C802200120012903E0063703C00241010D1841004100100441011005000B200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D18200141E0066A100D200141E0026A41186A200141E0066A41186A2903003703002001200141E0066A41106A2903003703F0022001200141E0066A41086A2903003703E802200120012903E0063703E00241010D1941004100100441011005000B200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D19200141E0066A100E20014180036A41186A420037030020014200370390032001420037038803200120012903E0063703800341010D1A41004100100441011005000B200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D1A200141E0066A100F200141A0036A41186A200141E0066A41186A2903003703002001200141E0066A41106A2903003703B0032001200141E0066A41086A2903003703A803200120012903E0063703A00341010D1B41004100100441011005000B200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D1B200141E0066A41186A4200370300200141A0066A41186A4200370300200141E0056A41186A4200370300200142003703F006200142003703E806200142C9003703E006200142003703B006200142003703A806200142CC003703A006200142003703F005200142003703E805200142D5003703E005200141E0066A4100200141A0066A200141E0056A200141A0056A10102001200141A0056A41086A2903003703C803200120012903A0053703C0032001200141A0056A41106A3502003E02D00341010D1C41004100100441011005000B41004100100441011005000B41201016220041041011200141086A20004114101420004120100441001005000B41004100100441011005000B41201016220041041011200141206A20004114101520004120100441001005000B41004100100441011005000B41201016220041041011200141386A20004114101420004120100441001005000B41004100100441011005000B41201016220041041011200141D0006A20004120101420004120100441001005000B41004100100441011005000B41201016220041041011200141F0006A20004120101420004120100441001005000B41004100100441011005000B4120101622004104101120014190016A20004114101420004120100441001005000B41004100100441011005000B41201016220041041011200141A8016A20004120101420004120100441001005000B41004100100441011005000B41201016220041041011200141C8016A20004120101420004120100441001005000B41201016220041041011200141E8016A20004120101420004120100441001005000B41004100100441011005000B41004100100441011005000B200141A0076A240041020F0B41201016220041041011200141C0026A20004120101420004120100441001005000B41004100100441011005000B41201016220041041011200141E0026A20004120101420004120100441001005000B41004100100441011005000B4120101622004104101120014180036A20004120101420004120100441001005000B41004100100441011005000B41201016220041041011200141A0036A20004120101420004120100441001005000B41004100100441011005000B41201016220041041011200141C0036A20004114101420004120100441001005000B41004100100441011005000B41201016220041041011200141DC036A20004104101520004120100441001005000B4120101622004104101120014188026A20004120101420004120100441001005000B00740970726F647563657273010C70726F6365737365642D62790105636C616E675431312E302E3120286769743A2F2F6769746875622E636F6D2F6C6C766D2F6C6C766D2D70726F6A65637420313365343336396337333335356136633561333166633161313131356663376336393734336164612900B203046E616D6501AA031D000D6765745F63616C6C5F73697A65010F636F70795F63616C6C5F76616C7565020C6765745F6D736776616C7565031A6765745F626C6F636B5F636F696E626173655F61646472657373040A7365745F72657475726E050B73797374656D5F68616C7406106765745F626C6F636B5F6E756D626572070D6765745F74785F6F726967696E080A6765745F73656E646572090C6765745F636861696E5F69640A0C6765745F6761735F6C6566740B136765745F626C6F636B5F6761735F6C696D69740C146765745F65787465726E616C5F62616C616E63650D106765745F74785F6761735F70726963650E136765745F626C6F636B5F74696D657374616D700F146765745F626C6F636B5F646966666963756C7479100E63727970746F5F7265636F76657211085F5F627A65726F38120B5F5F62653332746F6C654E130A5F5F62654E746F6C654E140B5F5F6C654E746F62653332150A5F5F6C654E746F62654E16085F5F6D616C6C6F63170B5F5F696E69745F6865617018075F5F6D756C333219095F5F6173686C7469331A095F5F6C7368727469331B0A756469766D6F643531321C057374617274"
                    .HexToBytes()
            );
            if (!VirtualMachine.VerifyContract(contract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            var snapshot = stateManager.NewSnapshot();
            snapshot.Contracts.AddContract(UInt160Utils.Zero, contract);
            stateManager.Approve();

            for (var i = 0; i < 1; ++i)
            {
                var currentTime = TimeUtils.CurrentTimeMillis();
                var currentSnapshot = stateManager.NewSnapshot();

                var sender = "0x6bc32575acb8754886dc283c2c8ac54b1bd93195".HexToBytes().ToUInt160();

                currentSnapshot.Balances.AddBalance(sender, 100.ToUInt256().ToMoney());

                var transactionReceipt = new TransactionReceipt();
                transactionReceipt.Transaction = new Transaction();
                transactionReceipt.Transaction.Value = 0.ToUInt256();
                transactionReceipt.Transaction.GasPrice = 100;
                var context = new InvocationContext(sender, currentSnapshot, transactionReceipt);

                {
                    Console.WriteLine($"\nAllFeatures: testMulmod()");
                    var input = ContractEncoder.Encode("testMulmod()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nAllFeatures: testMsgSender()");
                    var input = ContractEncoder.Encode("testMsgSender()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    transactionReceipt.Transaction.Value = 100.ToUInt256();
                    Console.WriteLine($"\nAllFeatures: testMsgValue()");
                    var input = ContractEncoder.Encode("testMsgValue()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    transactionReceipt.Transaction.Value = 0.ToUInt256();
                    Console.WriteLine($"\nAllFeatures: testMsgSig()");
                    var input = ContractEncoder.Encode("testMsgSig()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nAllFeatures: testGas()");
                    var input = ContractEncoder.Encode("testGas()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nAllFeatures: testTxGasprice()");
                    var input = ContractEncoder.Encode("testTxGasprice()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nAllFeatures: testTxOrigin()");
                    var input = ContractEncoder.Encode("testTxOrigin()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nAllFeatures: testBlockCoinbase()");
                    var input = ContractEncoder.Encode("testBlockCoinbase()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nAllFeatures: testBlockDifficulty()");
                    var input = ContractEncoder.Encode("testBlockDifficulty()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nAllFeatures: testBlockGaslimit()");
                    var input = ContractEncoder.Encode("testBlockGaslimit()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nAllFeatures: testBlockNumber()");
                    var input = ContractEncoder.Encode("testBlockNumber()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }

                stateManager.Approve();
            exit_mark:
                var elapsedTime = TimeUtils.CurrentTimeMillis() - currentTime;
                Console.WriteLine("Elapsed Time: " + elapsedTime + "ms");
            }
        }

        [Test]
        public void Test_VirtualMachine_InvokeContractWithValue()
        {
            var stateManager = _container.Resolve<IStateManager>();

            // A
            var aAddress = UInt160Utils.Zero;
            var aContract = new Contract
            (
                aAddress,
                "0061736D01000000012C0860017F0060027F7F0060057F7F7F7F7F017F6000017F60037F7F7F0060000060017F017F60047F7E7E7F0002AE010903656E760A6765745F73656E646572000003656E760C736176655F73746F72616765000103656E760C6765745F6D736776616C7565000003656E760C6C6F61645F73746F72616765000103656E760F696E766F6B655F636F6E7472616374000203656E760A7365745F72657475726E000103656E760B73797374656D5F68616C74000003656E760D6765745F63616C6C5F73697A65000303656E760F636F70795F63616C6C5F76616C75650004030C0B01050604040404070703050405017001010105030100020608017F01418080040B071202066D656D6F7279020005737461727400130AF81E0B1C00034020004200370300200041086A21002001417F6A22010D000B0B2E004100410036028080044100410036028480044100410036028C800441003F0041107441F0FF7B6A36028880040BA60101047F418080042101024003400240200128020C0D002001280208220220004F0D020B200128020022010D000B41002101410028020821020B02402002200041076A41787122036B22024118490D00200120036A41106A22002001280200220436020002402004450D00200420003602040B2000200241706A3602082000410036020C2000200136020420012000360200200120033602080B2001410136020C200141106A0B2D002000411F6A21000340200120002D00003A0000200141016A21012000417F6A21002002417F6A22020D000B0B29002000417F6A210003402001200020026A2D00003A0000200141016A21012002417F6A22020D000B0B2D002001411F6A21010340200120002D00003A00002001417F6A2101200041016A21002002417F6A22020D000B0B29002001417F6A21010340200120026A20002D00003A0000200041016A21002002417F6A22020D000B0B5301017E02402003450D000240200341C00071450D0020012003413F71AD862102420021010C010B200141C00020036BAD8820022003AD220486842102200120048621010B20002002370308200020013703000B5301017E02402003450D000240200341C00071450D0020022003413F71AD882101420021020C010B200241C00020036BAD8620012003AD220488842101200220048821020B20002002370308200020013703000BF31007027F037E017F057E027F097E017F230041E0036B22002400200022014188036A1000200141E8026A41186A4200370300200142003703F802200142003703F002200142033703E80220014188036A41106A350200210220014188036A41086A29030021032001290388032104200141C8026A41041009200120033703D002200120043703C802200120023E02D802200141E8026A200141C8026A1001200141A8026A100220014188026A41186A4200370300200141E8016A41186A200141A8026A41186A2903003703002001420037039802200142003703900220014202370388022001200141A8026A41106A2903003703F8012001200141A8026A41086A2903003703F001200120012903A8023703E80120014188026A200141E8016A10014100200141E4016A4104100D200141C0016A41186A4200370300200142003703D001200142003703C801200142043703C00120012802E4012105200141A0016A41041009200120053602A001200141C0016A200141A0016A100120014180016A41186A420037030020014200370390012001420037038801200142023703800120014180016A200141E0006A10030240024020012903602206420385200141E0006A41106A290300220784200141E0006A41086A2903002208200141E0006A41186A290300220984844200520D00200141C0036A41186A4200370300200141A0036A41186A4200370300200142003703D003200142003703C803200142013703C003200142003703B003200142003703A803200142003703A0030C010B02400240200642025620084200522008501B200742005220094200522009501B20072009842202501B0D00200141C0036A41186A4200370300200141A0036A41186A2009370300200142003703D003200142003703C803200142003703C003200120063703A003200120083703A8030C010B4200210A200141206A4203420041FE0020097920077942C0007C20094200521B20087920067942C0007C20084200521B4280017C20024200521BA722056B220B1010200141106A4203420041800141FE0120056B22056B220C101120014203420020051010200141C0006A42014200200B1010200141306A42014200200C1011200141D0006A4201420020051010200141306A41086A290300200141C0006A41086A290300200541800149220B1B420020051B210D20012903302001290340200B1B420020051B210E200141D0006A41086A2903004200200B1B210F20012903504200200B1B2110024020012903004200200B1B2211200658200141086A2903004200200B1B220320085820032008511B20012903102001290320200B1B420020051B2204200758200141106A41086A290300200141206A41086A290300200B1B420020051B220220095820022009511B2004200785200220098584501B0D002010420188200F423F8684211020114201882003423F86842111200F420188200E423F8684210F20034201882004423F86842103200E420188200D423F8684210E20044201882002423F86842104200D420188210D200242018821020B42002112420021134200211402400340200642035441002008501B41002007200984501B0D010240200620115A200820035A200820035122051B200720045A200920025A20092002511B2007200485200920028584501B450D00200920027D2007200454AD7D200720047D22072006201154220B200820035420051BAD221554AD7D2109200820037D200BAD7D2108200A201084210A200620117D21062012200F8421122013200E8421132014200D842114200720157D21070B2010420188200F423F8684211020114201882003423F86842111200F420188200E423F8684210F20034201882004423F86842103200E420188200D423F8684210E20044201882002423F86842104200D420188210D200242018821020C000B0B200141C0036A41186A2014370300200141A0036A41186A20093703002001200A3703C003200120063703A003200120123703C803200120083703A803200120133703D0030B200120073703B0030B02400240024041000D00200141C0036A41086A2903002102200141C0036A41106A2903002103200141C0036A41186A290300210820012903C0032104200041606A2205220B2400200541186A4200370300200542003703102005420037030820054202370300200B41606A220B220024002005200B1003200B41186A2903002106200B41106A2903002107200B2903002111200B41086A29030021094104100B220C4199D4D19E7F360200200041606A2205220B2400200541186A4200370300200542003703102005420037030820054200370300200B41606A220B220024002005200B1003200B41106A350200210D200B290300210E200B41086A290300210F200041606A220B22052400200B200F370308200B200E370300200B200D3E0210200541606A220522002400200541186A2008370300200520033703102005200237030820052004370300200041706A22002216240020004200370300200B4104200C2005200010040D014104100B220C4199D4D19E7F360200201641606A2205220B2400200541186A4200370300200542003703102005420037030820054201370300200B41606A220B220024002005200B1003200B41106A350200210D200B290300210E200B41086A290300210F200041606A220B22052400200B200F370308200B200E370300200B200D3E0210200541606A220522002400200541186A200620087D2007200354AD7D200720037D220320112004542216200920025420092002511BAD220854AD7D3703002005200320087D3703102005200920027D2016AD7D3703082005201120047D370300200041706A2200240020004200370300200B4104200C200520001004450D0241004100100541011006000B200141E0036A240041000F0B41004100100541011006000B200141E0036A240041000BB40902057F067E230041F0016B2200210120002400100A41001007220236020441002002100B220336020841002002200310080240200241034D0D0041002003280200220436020002400240024002400240024002400240024002400240024002400240200441F0B582B201460D0002402004419FACCAAA054A0D00200441D1DBC414460D02200441D4B98CD500460D030C0F0B0240200441DE82ACD705460D00200441A0ACCAAA05470D0F200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D04200141D0016A41186A4200370300200142003703E001200142003703D801200142023703D001200141D0016A200141B0016A1003200141186A200141B0016A41186A2903003703002001200141B0016A41106A2903003703102001200141B0016A41086A290300370308200120012903B00137030041010D0541004100100541011006000B200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D05200141D0016A41186A4200370300200142003703E001200142003703D801200142033703D001200141D0016A200141B0016A10032001200141B0016A41086A290300370328200120012903B0013703202001200141B0016A41106A3502003E023041010D0641004100100541011006000B200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D062002417C6A2200411F4D0D07200341046A2202200141386A4118100C2000AD423F580D08200141386A41106A3502002105200141386A41086A290300210620012903382107200241206A200141D0006A4118100C200141D0006A41106A3502002108200141D0006A41086A29030021092001290350210A200141D0016A41186A4200370300200142003703E001200142003703D801200142003703D001200141B0016A41041009200120063703B801200120073703B001200120053E02C001200141D0016A200141B0016A100120014190016A41186A4200370300200142003703A00120014200370398012001420137039001200141F0006A41041009200120093703782001200A370370200120083E02800120014190016A200141F0006A100141010D0941004100100541011006000B200041606A22002400200010022000290300200041106A29030084200041086A290300200041186A29030084844200520D09200141D0016A41186A4200370300200142003703E001200142003703D801200142043703D001200141D0016A200141B0016A1003200120012802B00136026C41010D0A41004100100541011006000B1012450D0A41004100100541011006000B41004100100541011006000B20014120100B22004120100E20004120100541001006000B41004100100541011006000B200141206A4120100B22004114100E20004120100541001006000B41004100100541011006000B200141F0016A240041030F0B200141F0016A240041030F0B41004100100541001006000B41004100100541011006000B200141EC006A4120100B22004104100F20004120100541001006000B41004100100541001006000B41004100100541011006000B00740970726F647563657273010C70726F6365737365642D62790105636C616E675431302E302E3120286769743A2F2F6769746875622E636F6D2F6C6C766D2F6C6C766D2D70726F6A656374206236613137333433363738386536383332396363356539656530663635316236303361363337653329009A02046E616D6501920214000A6765745F73656E646572010C736176655F73746F72616765020C6765745F6D736776616C7565030C6C6F61645F73746F72616765040F696E766F6B655F636F6E7472616374050A7365745F72657475726E060B73797374656D5F68616C74070D6765745F63616C6C5F73697A65080F636F70795F63616C6C5F76616C756509085F5F627A65726F380A0B5F5F696E69745F686561700B085F5F6D616C6C6F630C0B5F5F62653332746F6C654E0D0A5F5F62654E746F6C654E0E0B5F5F6C654E746F626533320F0A5F5F6C654E746F62654E10095F5F6173686C74693311095F5F6C736872746933121F736F6C3A3A66756E6374696F6E3A3A413A3A7265636569766556616C75654113057374617274"
                    .HexToBytes()
            );
            if (!VirtualMachine.VerifyContract(aContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // B
            var bCode = "0061736D01000000011C066000017F60037F7F7F0060017F0060027F7F0060000060017F017F0298010803656E760D6765745F63616C6C5F73697A65000003656E760F636F70795F63616C6C5F76616C7565000103656E760C6765745F6D736776616C7565000203656E760C6C6F61645F73746F72616765000303656E760A7365745F72657475726E000303656E760B73797374656D5F68616C74000203656E760A6765745F73656E646572000203656E760C736176655F73746F726167650003030807030405010101040405017001010105030100020608017F01418080040B071202066D656D6F72790200057374617274000E0AE00B071C00034020004200370300200041086A21002001417F6A22010D000B0B2E004100410036028080044100410036028480044100410036028C800441003F0041107441F0FF7B6A36028880040BA60101047F418080042101024003400240200128020C0D002001280208220220004F0D020B200128020022010D000B41002101410028020821020B02402002200041076A41787122036B22024118490D00200120036A41106A22002001280200220436020002402004450D00200420003602040B2000200241706A3602082000410036020C2000200136020420012000360200200120033602080B2001410136020C200141106A0B29002000417F6A210003402001200020026A2D00003A0000200141016A21012002417F6A22020D000B0B2D002001411F6A21010340200120002D00003A00002001417F6A2101200041016A21002002417F6A22020D000B0B29002001417F6A21010340200120026A20002D00003A0000200041016A21002002417F6A22020D000B0BE70802047F037E230041C0026B2200210120002400100941001000220236020441002002100A220336020841002002200310010240200241034D0D0041002003280200220236020002400240024002400240024002400240024002402002419FACCAAA054A0D0020024199D4D19E7F460D02200241D1DBC414460D010C0A0B0240200241DE82ACD705460D00200241A0ACCAAA05470D0A200041606A22022400200210022002290300200241106A29030084200241086A290300200241186A29030084844200520D0320014188026A41186A420037030020014200370398022001420037039002200142003703880220014188026A200141E8016A1003200141186A200141E8016A41186A2903003703002001200141E8016A41106A2903003703102001200141E8016A41086A290300370308200120012903E80137030041010D0441004100100441011005000B200041606A22022400200210022002290300200241106A29030084200241086A290300200241186A29030084844200520D0420014188026A41186A420037030020014200370398022001420037039002200142013703880220014188026A200141E8016A10032001200141E8016A41086A290300370328200120012903E8013703202001200141E8016A41106A3502003E023041010D0541004100100441011005000B200041606A22022400200210022002290300200241106A29030084200241086A290300200241186A29030084844200520D0520014188026A41186A420037030020014200370398022001420037039002200142023703880220014188026A200141E8016A1003200120012802E80136023C41010D0641004100100441011005000B200141A8026A100620014188026A41186A4200370300200142003703980220014200370390022001420137038802200141A8026A41106A3502002104200141A8026A41086A290300210520012903A8022106200141E8016A41041008200120053703F001200120063703E801200120043E02F80120014188026A200141E8016A1007200141C8016A1002200141A8016A41186A420037030020014188016A41186A200141C8016A41186A290300370300200142003703B801200142003703B001200142003703A8012001200141C8016A41106A290300370398012001200141C8016A41086A29030037039001200120012903C80137038801200141A8016A20014188016A1007410020014184016A4104100B200141E0006A41186A42003703002001420037037020014200370368200142023703602001280284012102200141C0006A4104100820012002360240200141E0006A200141C0006A10074100450D0641004100100441011005000B41004100100441011005000B20014120100A22024120100C20024120100441001005000B41004100100441011005000B200141206A4120100A22014114100C20014120100441001005000B41004100100441011005000B2001413C6A4120100A22014104100D20014120100441001005000B41004100100441001005000B41004100100441011005000B00740970726F647563657273010C70726F6365737365642D62790105636C616E675431302E302E3120286769743A2F2F6769746875622E636F6D2F6C6C766D2F6C6C766D2D70726F6A65637420623661313733343336373838653638333239636335653965653066363531623630336136333765332900C501046E616D6501BD010F000D6765745F63616C6C5F73697A65010F636F70795F63616C6C5F76616C7565020C6765745F6D736776616C7565030C6C6F61645F73746F72616765040A7365745F72657475726E050B73797374656D5F68616C74060A6765745F73656E646572070C736176655F73746F7261676508085F5F627A65726F38090B5F5F696E69745F686561700A085F5F6D616C6C6F630B0A5F5F62654E746F6C654E0C0B5F5F6C654E746F626533320D0A5F5F6C654E746F62654E0E057374617274"
                    .HexToBytes();

            var b1Address = "0xfd893ce89186fc6861d339cb6ab5d75458e3daf3".HexToBytes().ToUInt160();
            var b1Contract = new Contract
            (
                b1Address,
                bCode
            );
            if (!VirtualMachine.VerifyContract(b1Contract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            var b2Address = "0x6bc32575acb8754886dc283c2c8ac54b1bd93195".HexToBytes().ToUInt160();
            var b2Contract = new Contract
            (
                b2Address,
                bCode
            );
            if (!VirtualMachine.VerifyContract(b2Contract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            var snapshot = stateManager.NewSnapshot();
            snapshot.Contracts.AddContract(UInt160Utils.Zero, aContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, b1Contract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, b2Contract);
            stateManager.Approve();

            for (var i = 0; i < 1; ++i)
            {
                var currentTime = TimeUtils.CurrentTimeMillis();
                var currentSnapshot = stateManager.NewSnapshot();

                var sender = UInt160Utils.Zero;

                currentSnapshot.Balances.AddBalance(UInt160Utils.Zero, 100.ToUInt256().ToMoney());

                var transactionReceipt = new TransactionReceipt();
                transactionReceipt.Transaction = new Transaction();
                transactionReceipt.Transaction.Value = 0.ToUInt256();
                var context = new InvocationContext(sender, currentSnapshot, transactionReceipt);

                {
                    Console.WriteLine($"\nA: init({b1Address.ToHex()},{b2Address.ToHex()})");
                    var input = ContractEncoder.Encode("init(address,address)", b1Address, b2Address);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    transactionReceipt.Transaction.Value = 9.ToUInt256();
                    context = new InvocationContext(sender, context.Snapshot, transactionReceipt);

                    Console.WriteLine($"\nA: receiveValueA()");
                    var input = ContractEncoder.Encode("receiveValueA()", 1.ToUInt256());
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    transactionReceipt.Transaction.Value = 0.ToUInt256();
                    context = new InvocationContext(sender, context.Snapshot, transactionReceipt);

                    Console.WriteLine("\nA: getSig()");
                    var input = ContractEncoder.Encode("getSig()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine("\nB1: getSig()");
                    var input = ContractEncoder.Encode("getSig()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(b1Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine("\nB2: getSig()");
                    var input = ContractEncoder.Encode("getSig()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(b2Contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }

                stateManager.Approve();
            exit_mark:
                var elapsedTime = TimeUtils.CurrentTimeMillis() - currentTime;
                Console.WriteLine("Elapsed Time: " + elapsedTime + "ms");
            }
        }

        [Test]
        public void Test_VirtualMachine_InvokeCreateContract()
        {
            var stateManager = _container.Resolve<IStateManager>();

            // A
            var assembly = Assembly.GetExecutingAssembly();
            var resourceA = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.CreateContract.wasm");
            var aCode = new byte[resourceA!.Length];
            resourceA!.Read(aCode, 0, (int)resourceA!.Length);

            var aAddress = "0xfd893ce89186fc6861d339cb6ab5d75458e3daf3".HexToBytes().ToUInt160();
            var aContract = new Contract
            (
                aAddress,
                aCode
            );
            if (!VirtualMachine.VerifyContract(aContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            var snapshot = stateManager.NewSnapshot();
            snapshot.Contracts.AddContract(UInt160Utils.Zero, aContract);
            stateManager.Approve();

            for (var i = 0; i < 1; ++i)
            {
                var currentTime = TimeUtils.CurrentTimeMillis();
                var currentSnapshot = stateManager.NewSnapshot();

                var sender = UInt160Utils.Zero;

                currentSnapshot.Balances.AddBalance(UInt160Utils.Zero, 100.ToUInt256().ToMoney());
                currentSnapshot.Balances.AddBalance(aAddress, 100.ToUInt256().ToMoney());

                var transactionReceipt = new TransactionReceipt();
                transactionReceipt.Transaction = new Transaction();
                transactionReceipt.Transaction.Value = 0.ToUInt256();
                var context = new InvocationContext(sender, currentSnapshot, transactionReceipt);

                {
                    Console.WriteLine($"\nA: init()");
                    var input = ContractEncoder.Encode("init()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nA: assignValue()");
                    var input = ContractEncoder.Encode("assignValue()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine("\nA: getValue()");
                    var input = ContractEncoder.Encode("getValue()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }

                stateManager.Approve();
            exit_mark:
                var elapsedTime = TimeUtils.CurrentTimeMillis() - currentTime;
                Console.WriteLine("Elapsed Time: " + elapsedTime + "ms");
            }
        }

        [Test]
        public void Test_VirtualMachine_InvokeStringContract()
        {
            var stateManager = _container.Resolve<IStateManager>();

            // A
            var aCode = "0061736D0100000001230760017F006000017F60037F7F7F0060027F7F0060017F017F60037F7F7F017F60000002D9010A03656E760C6765745F6D736776616C7565000003656E760D6765745F63616C6C5F73697A65000103656E760F636F70795F63616C6C5F76616C7565000203656E760C6C6F61645F73746F72616765000303656E760A7365745F72657475726E000303656E760B73797374656D5F68616C74000003656E76176765745F73746F726167655F737472696E675F73697A65000403656E76136C6F61645F73746F726167655F737472696E67000303656E7613736176655F73746F726167655F737472696E67000203656E760C736176655F73746F726167650003030807020302050406060405017001010105030100020608017F01418080040B071202066D656D6F7279020005737461727400100AAE0C072E0002402002450D000340200020012D00003A0000200041016A2100200141016A21012002417F6A22020D000B0B0B240002402001450D00034020004200370300200041086A21002001417F6A22010D000B0B0B2D002001411F6A21010340200120002D00003A00002001417F6A2101200041016A21002002417F6A22020D000B0B7E01017F200120006C220141086A100E2203200036020420032000360200200341086A2100024002402002417F460D002001450D010340200020022D00003A0000200041016A2100200241016A21022001417F6A22010D000C020B0B2001450D000340200041003A0000200041016A21002001417F6A22010D000B0B20030BA60101047F418080042101024003400240200128020C0D002001280208220220004F0D020B200128020022010D000B41002101410028020821020B02402002200041076A41787122036B22024118490D00200120036A41106A22002001280200220436020002402004450D00200420003602040B2000200241706A3602082000410036020C2000200136020420012000360200200120033602080B2001410136020C200141106A0B2E004100410036028080044100410036028480044100410036028C800441003F0041107441F0FF7B6A36028880040BD30801057F230041D0016B22002400200041086A10000240024002400240024002402000290308200041186A29030084200041106A290300200041206A29030084844200520D00100F41001001220136024041002001100E22023602444100200120021002200141034D0D0541002002280200220136023C02400240024002402001419FACCAAA054A0D00200141CFCAE650460D02200141B0E684DA03460D010C090B200141A0ACCAAA05460D02200141DDE688FD07470D08200041B0016A41186A4200370300200042003703C001200042003703B801200042013703B001200041B0016A20004190016A1003200041286A41186A20004190016A41186A2903003703002000200041A0016A290300370338200020004198016A29030037033020002000290390013703284100450D0441004100100441011005000B200041C8016A4200370300200042003703C001200042003703B801200042003703B001200041B0016A1006220241086A100E22012002360200200141046A2002360200200041B0016A200141086A10072000200136024C4100450D0441004100100441011005000B20004190016A41186A4200370300200042003703A0012000420037039801200042003703900120004190016A410041301008200041B0016A41186A4200370300200041F0006A41186A4200370300200042003703C001200042003703B801200042043703B00120004200370380012000420037037820004201370370200041F0006A200041B0016A10092000410941014130100D3602584100450D0441004100100441011005000B200041C8016A4200370300200042003703C001200042003703B801200042003703B001200041B0016A1006220241086A100E22012002360200200141046A2002360200200041B0016A200141086A1007200020013602644100450D0441004100100441011005000B41004100100441011005000B4120100E22014104100B200041286A20014120100C20014120100441001005000B200028024C2201280200410020011B413F6A41607141206A2202100E22012002410376100B20004120360250200041D0006A20014104100C2000200028024C2203280200410020031B2204360254200041D4006A200141206A4104100C200141C0006A200341086A2004100A20012002100441001005000B20002802582201280200410020011B413F6A41607141206A2202100E22012002410376100B2000412036025C200041DC006A20014104100C200020002802582203280200410020031B2204360260200041E0006A200141206A4104100C200141C0006A200341086A2004100A20012002100441001005000B20002802642201280200410020011B413F6A41607141206A2202100E22012002410376100B20004120360268200041E8006A20014104100C200020002802642203280200410020031B220436026C200041EC006A200141206A4104100C200141C0006A200341086A2004100A20012002100441001005000B41004100100441011005000B0B3F010041000B3961616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161736473616473616400740970726F647563657273010C70726F6365737365642D62790105636C616E675431312E302E3120286769743A2F2F6769746875622E636F6D2F6C6C766D2F6C6C766D2D70726F6A65637420313365343336396337333335356136633561333166633161313131356663376336393734336164612900FA01046E616D6501F20111000C6765745F6D736776616C7565010D6765745F63616C6C5F73697A65020F636F70795F63616C6C5F76616C7565030C6C6F61645F73746F72616765040A7365745F72657475726E050B73797374656D5F68616C7406176765745F73746F726167655F737472696E675F73697A6507136C6F61645F73746F726167655F737472696E670813736176655F73746F726167655F737472696E67090C736176655F73746F726167650A085F5F6D656D6370790B085F5F627A65726F380C0B5F5F6C654E746F626533320D0A766563746F725F6E65770E085F5F6D616C6C6F630F0B5F5F696E69745F6865617010057374617274"
                    .HexToBytes();

            var aAddress = "0xfd893ce89186fc6861d339cb6ab5d75458e3daf3".HexToBytes().ToUInt160();
            var aContract = new Contract
            (
                aAddress,
                aCode
            );
            if (!VirtualMachine.VerifyContract(aContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            var snapshot = stateManager.NewSnapshot();
            snapshot.Contracts.AddContract(UInt160Utils.Zero, aContract);
            stateManager.Approve();

            for (var i = 0; i < 1; ++i)
            {
                var currentTime = TimeUtils.CurrentTimeMillis();
                var currentSnapshot = stateManager.NewSnapshot();

                var sender = UInt160Utils.Zero;

                currentSnapshot.Balances.AddBalance(UInt160Utils.Zero, 100.ToUInt256().ToMoney());
                currentSnapshot.Balances.AddBalance(aAddress, 100.ToUInt256().ToMoney());

                var transactionReceipt = new TransactionReceipt();
                transactionReceipt.Transaction = new Transaction();
                transactionReceipt.Transaction.Value = 0.ToUInt256();
                var context = new InvocationContext(sender, currentSnapshot, transactionReceipt);

                {
                    Console.WriteLine("\nA: setValue()");
                    var input = ContractEncoder.Encode("setValue()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine("\nA: getValue()");
                    var input = ContractEncoder.Encode("getValue()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }

                stateManager.Approve();
            exit_mark:
                var elapsedTime = TimeUtils.CurrentTimeMillis() - currentTime;
                Console.WriteLine("Elapsed Time: " + elapsedTime + "ms");
            }
        }

        [Test]
        public void Test_VirtualMachine_InvokeDelegateContract()
        {
            var stateManager = _container.Resolve<IStateManager>();

            // A
            var aAddress = UInt160Utils.Zero;
            var aContract = new Contract
            (
                aAddress,
                "0061736D01000000012C0860017F006000017F60037F7F7F0060027F7F0060057F7F7F7F7F017F60000060017F017F60037F7F7F017F02D4010A03656E760C6765745F6D736776616C7565000003656E760D6765745F63616C6C5F73697A65000103656E760F636F70795F63616C6C5F76616C7565000203656E760C6C6F61645F73746F72616765000303656E7618696E766F6B655F64656C65676174655F636F6E7472616374000403656E760F6765745F72657475726E5F73697A65000103656E7611636F70795F72657475726E5F76616C7565000203656E760A7365745F72657475726E000303656E760B73797374656D5F68616C74000003656E760C736176655F73746F726167650003030807030506020207050405017001010105030100020608017F01418080040B071202066D656D6F7279020005737461727400100AE209071C00034020004200370300200041086A21002001417F6A22010D000B0B2E004100410036028080044100410036028480044100410036028C800441003F0041107441F0FF7B6A36028880040BA60101047F418080042101024003400240200128020C0D002001280208220220004F0D020B200128020022010D000B41002101410028020821020B02402002200041076A41787122036B22024118490D00200120036A41106A22002001280200220436020002402004450D00200420003602040B2000200241706A3602082000410036020C2000200136020420012000360200200120033602080B2001410136020C200141106A0B2D002000411F6A21000340200120002D00003A0000200141016A21012000417F6A21002002417F6A22020D000B0B2D002001411F6A21010340200120002D00003A00002001417F6A2101200041016A21002002417F6A22020D000B0B7E01017F200120006C220141086A100C2203200036020420032000360200200341086A2100024002402002417F460D002001450D010340200020022D00003A0000200041016A2100200241016A21022001417F6A22010D000C020B0B2001450D000340200041003A0000200041016A21002001417F6A22010D000B0B20030B900602047F037E230041E0016B22002400200041086A10000240024002400240024002402000290308200041186A29030084200041106A290300200041206A29030084844200520D00100B41001001220136020841002001100C220236020C4100200120021002200141034D0D01410020022802002203360204024020034199D696E203460D000240200341A0ACCAAA05460D0020034183D7DBA67E470D034108100C1A200041C0016A41186A4200370300200042003703D001200042003703C801200042013703C001200041C0016A200041A0016A1003200041B0016A3502002104200041A0016A41086A290300210520002903A0012106410441014100100F22012802002102200041E8006A41186A420037030020004200370378200042003703702000420037036820002005370394012000200637038C01200020043E029C01200042003703602000418C016A2002200141086A200041E8006A200041E0006A10041A1005220041086A100C22012000360200200141046A2000360200200141086A4100200010064100450D0441004100100741011008000B200041C0016A41186A4200370300200042003703D001200042003703C801200042003703C001200041C0016A200041A0016A1003200041286A41186A200041A0016A41186A2903003703002000200041B0016A2903003703382000200041A8016A290300370330200020002903A0013703284100450D0441004100100741011008000B2001417C6A4120490D04200241046A200041C8006A4118100D200041D8006A3502002104200041D0006A290300210520002903482106200041C0016A41186A4200370300200042003703D001200042003703C801200042013703C001200041A0016A4104100A200020053703A801200020063703A001200020043E02B001200041C0016A200041A0016A100941010D0541004100100741011008000B41004100100741011008000B41004100100741011008000B41004100100741001008000B200041286A4120100C22004120100E20004120100741001008000B200041E0016A240041030F0B41004100100741001008000B0B0A010041000B0483EBD6E400740970726F647563657273010C70726F6365737365642D62790105636C616E675431302E302E3120286769743A2F2F6769746875622E636F6D2F6C6C766D2F6C6C766D2D70726F6A65637420623661313733343336373838653638333239636335653965653066363531623630336136333765332900F801046E616D6501F00111000C6765745F6D736776616C7565010D6765745F63616C6C5F73697A65020F636F70795F63616C6C5F76616C7565030C6C6F61645F73746F726167650418696E766F6B655F64656C65676174655F636F6E7472616374050F6765745F72657475726E5F73697A650611636F70795F72657475726E5F76616C7565070A7365745F72657475726E080B73797374656D5F68616C74090C736176655F73746F726167650A085F5F627A65726F380B0B5F5F696E69745F686561700C085F5F6D616C6C6F630D0B5F5F62653332746F6C654E0E0B5F5F6C654E746F626533320F0A766563746F725F6E657710057374617274"
                    .HexToBytes()
            );
            if (!VirtualMachine.VerifyContract(aContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // B
            var bCode = "0061736D01000000011C0660017F006000017F60037F7F7F0060027F7F0060000060017F017F0287010703656E760C6765745F6D736776616C7565000003656E760D6765745F63616C6C5F73697A65000103656E760F636F70795F63616C6C5F76616C7565000203656E760C736176655F73746F72616765000303656E760A7365745F72657475726E000303656E760B73797374656D5F68616C74000003656E760C6C6F61645F73746F726167650003030504040502040405017001010105030100020608017F01418080040B071202066D656D6F72790200057374617274000A0AB205042E004100410036028080044100410036028480044100410036028C800441003F0041107441F0FF7B6A36028880040BA60101047F418080042101024003400240200128020C0D002001280208220220004F0D020B200128020022010D000B41002101410028020821020B02402002200041076A41787122036B22024118490D00200120036A41106A22002001280200220436020002402004450D00200420003602040B2000200241706A3602082000410036020C2000200136020420012000360200200120033602080B2001410136020C200141106A0B2D002001411F6A21010340200120002D00003A00002001417F6A2101200041016A21002002417F6A22020D000B0BAA0301037F23004180016B220024002000100002400240024002402000290300200041106A29030084200041086A290300200041186A29030084844200520D00100741001001220136020441002001100822023602084100200120021002200141034D0D014100200228020022013602000240200141A0ACCAAA05460D0020014183D7DBA67E470D02200041E0006A41186A4200370300200041C0006A41186A4200370300200042003703702000420037036820004200370360200042003703502000420037034820004205370340200041E0006A200041C0006A10034100450D0341004100100441011005000B200041E0006A41186A4200370300200042003703702000420037036820004200370360200041E0006A200041C0006A1006200041206A41186A200041C0006A41186A2903003703002000200041D0006A2903003703302000200041C8006A290300370328200020002903403703204100450D0341004100100441011005000B41004100100441011005000B41004100100441011005000B41004100100441001005000B200041206A4120100822004120100920004120100441001005000B00740970726F647563657273010C70726F6365737365642D62790105636C616E675431302E302E3120286769743A2F2F6769746875622E636F6D2F6C6C766D2F6C6C766D2D70726F6A656374206236613137333433363738386536383332396363356539656530663635316236303361363337653329009701046E616D65018F010B000C6765745F6D736776616C7565010D6765745F63616C6C5F73697A65020F636F70795F63616C6C5F76616C7565030C736176655F73746F72616765040A7365745F72657475726E050B73797374656D5F68616C74060C6C6F61645F73746F72616765070B5F5F696E69745F6865617008085F5F6D616C6C6F63090B5F5F6C654E746F626533320A057374617274"
                    .HexToBytes();

            var bAddress = "0xfd893ce89186fc6861d339cb6ab5d75458e3daf3".HexToBytes().ToUInt160();
            var bContract = new Contract
            (
                bAddress,
                bCode
            );
            if (!VirtualMachine.VerifyContract(bContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            var snapshot = stateManager.NewSnapshot();
            snapshot.Contracts.AddContract(UInt160Utils.Zero, aContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, bContract);
            stateManager.Approve();

            for (var i = 0; i < 1; ++i)
            {
                var currentTime = TimeUtils.CurrentTimeMillis();
                var currentSnapshot = stateManager.NewSnapshot();

                var sender = UInt160Utils.Zero;

                currentSnapshot.Balances.AddBalance(UInt160Utils.Zero, 100.ToUInt256().ToMoney());

                var transactionReceipt = new TransactionReceipt();
                transactionReceipt.Transaction = new Transaction();
                transactionReceipt.Transaction.Value = 0.ToUInt256();
                var context = new InvocationContext(sender, currentSnapshot, transactionReceipt);

                {
                    Console.WriteLine($"\nA: init({bAddress.ToHex()})");
                    var input = ContractEncoder.Encode("init(address)", bAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nA: assignValue()");
                    var input = ContractEncoder.Encode("assignValue()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    transactionReceipt.Transaction.Value = 0.ToUInt256();
                    context = new InvocationContext(sender, context.Snapshot, transactionReceipt);

                    Console.WriteLine("\nA: getValue()");
                    var input = ContractEncoder.Encode("getValue()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine("\nB: getValue()");
                    var input = ContractEncoder.Encode("getValue()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(bContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }

                stateManager.Approve();
            exit_mark:
                var elapsedTime = TimeUtils.CurrentTimeMillis() - currentTime;
                Console.WriteLine("Elapsed Time: " + elapsedTime + "ms");
            }
        }

        [Test]
        public void Test_VirtualMachine_InvokeStaticContract()
        {
            var stateManager = _container.Resolve<IStateManager>();

            // A
            var aAddress = UInt160Utils.Zero;
            var aContract = new Contract
            (
                aAddress,
                "0061736D01000000012C0860017F006000017F60037F7F7F0060027F7F0060057F7F7F7F7F017F60000060017F017F60037F7F7F017F02D2010A03656E760C6765745F6D736776616C7565000003656E760D6765745F63616C6C5F73697A65000103656E760F636F70795F63616C6C5F76616C7565000203656E760C6C6F61645F73746F72616765000303656E760A7365745F72657475726E000303656E760B73797374656D5F68616C74000003656E7616696E766F6B655F7374617469635F636F6E7472616374000403656E760F6765745F72657475726E5F73697A65000103656E7611636F70795F72657475726E5F76616C7565000203656E760C736176655F73746F726167650003030807030506020207050405017001010105030100020608017F01418080040B071202066D656D6F7279020005737461727400100AE209071C00034020004200370300200041086A21002001417F6A22010D000B0B2E004100410036028080044100410036028480044100410036028C800441003F0041107441F0FF7B6A36028880040BA60101047F418080042101024003400240200128020C0D002001280208220220004F0D020B200128020022010D000B41002101410028020821020B02402002200041076A41787122036B22024118490D00200120036A41106A22002001280200220436020002402004450D00200420003602040B2000200241706A3602082000410036020C2000200136020420012000360200200120033602080B2001410136020C200141106A0B2D002000411F6A21000340200120002D00003A0000200141016A21012000417F6A21002002417F6A22020D000B0B2D002001411F6A21010340200120002D00003A00002001417F6A2101200041016A21002002417F6A22020D000B0B7E01017F200120006C220141086A100C2203200036020420032000360200200341086A2100024002402002417F460D002001450D010340200020022D00003A0000200041016A2100200241016A21022001417F6A22010D000C020B0B2001450D000340200041003A0000200041016A21002001417F6A22010D000B0B20030B900602047F037E230041E0016B22002400200041086A10000240024002400240024002402000290308200041186A29030084200041106A290300200041206A29030084844200520D00100B41001001220136020841002001100C220236020C4100200120021002200141034D0D01410020022802002203360204024020034199D696E203460D00024020034183D7DBA67E460D00200341A0ACCAAA05470D03200041C0016A41186A4200370300200042003703D001200042003703C801200042003703C001200041C0016A200041A0016A1003200041286A41186A200041A0016A41186A2903003703002000200041B0016A2903003703382000200041A8016A290300370330200020002903A0013703284100450D0441004100100441011005000B4108100C1A200041C0016A41186A4200370300200042003703D001200042003703C801200042013703C001200041C0016A200041A0016A1003200041B0016A3502002104200041A0016A41086A290300210520002903A0012106410441014100100F22012802002102200041E8006A41186A420037030020004200370378200042003703702000420037036820002005370394012000200637038C01200020043E029C01200042003703602000418C016A2002200141086A200041E8006A200041E0006A10061A1007220041086A100C22012000360200200141046A2000360200200141086A4100200010084100450D0441004100100441011005000B2001417C6A4120490D04200241046A200041C8006A4118100D200041D8006A3502002104200041D0006A290300210520002903482106200041C0016A41186A4200370300200042003703D001200042003703C801200042013703C001200041A0016A4104100A200020053703A801200020063703A001200020043E02B001200041C0016A200041A0016A100941010D0541004100100441011005000B41004100100441011005000B41004100100441011005000B200041286A4120100C22004120100E20004120100441001005000B41004100100441001005000B200041E0016A240041030F0B41004100100441001005000B0B0A010041000B0483EBD6E400740970726F647563657273010C70726F6365737365642D62790105636C616E675431302E302E3120286769743A2F2F6769746875622E636F6D2F6C6C766D2F6C6C766D2D70726F6A65637420623661313733343336373838653638333239636335653965653066363531623630336136333765332900F601046E616D6501EE0111000C6765745F6D736776616C7565010D6765745F63616C6C5F73697A65020F636F70795F63616C6C5F76616C7565030C6C6F61645F73746F72616765040A7365745F72657475726E050B73797374656D5F68616C740616696E766F6B655F7374617469635F636F6E7472616374070F6765745F72657475726E5F73697A650811636F70795F72657475726E5F76616C7565090C736176655F73746F726167650A085F5F627A65726F380B0B5F5F696E69745F686561700C085F5F6D616C6C6F630D0B5F5F62653332746F6C654E0E0B5F5F6C654E746F626533320F0A766563746F725F6E657710057374617274"
                    .HexToBytes()
            );
            if (!VirtualMachine.VerifyContract(aContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // B
            var bCode = "0061736D01000000012C0860017F006000017F60037F7F7F0060027F7F0060057F7F7F7F7F017F60000060017F017F60037F7F7F017F02D4010A03656E760C6765745F6D736776616C7565000003656E760D6765745F63616C6C5F73697A65000103656E760F636F70795F63616C6C5F76616C7565000203656E760C6C6F61645F73746F72616765000303656E760A7365745F72657475726E000303656E760B73797374656D5F68616C74000003656E7618696E766F6B655F64656C65676174655F636F6E7472616374000403656E760F6765745F72657475726E5F73697A65000103656E7611636F70795F72657475726E5F76616C7565000203656E760C736176655F73746F726167650003030807030506020207050405017001010105030100020608017F01418080040B071202066D656D6F7279020005737461727400100AE209071C00034020004200370300200041086A21002001417F6A22010D000B0B2E004100410036028080044100410036028480044100410036028C800441003F0041107441F0FF7B6A36028880040BA60101047F418080042101024003400240200128020C0D002001280208220220004F0D020B200128020022010D000B41002101410028020821020B02402002200041076A41787122036B22024118490D00200120036A41106A22002001280200220436020002402004450D00200420003602040B2000200241706A3602082000410036020C2000200136020420012000360200200120033602080B2001410136020C200141106A0B2D002000411F6A21000340200120002D00003A0000200141016A21012000417F6A21002002417F6A22020D000B0B2D002001411F6A21010340200120002D00003A00002001417F6A2101200041016A21002002417F6A22020D000B0B7E01017F200120006C220141086A100C2203200036020420032000360200200341086A2100024002402002417F460D002001450D010340200020022D00003A0000200041016A2100200241016A21022001417F6A22010D000C020B0B2001450D000340200041003A0000200041016A21002001417F6A22010D000B0B20030B900602047F037E230041E0016B22002400200041086A10000240024002400240024002402000290308200041186A29030084200041106A290300200041206A29030084844200520D00100B41001001220136020841002001100C220236020C4100200120021002200141034D0D01410020022802002203360204024020034199D696E203460D00024020034183D7DBA67E460D00200341A0ACCAAA05470D03200041C0016A41186A4200370300200042003703D001200042003703C801200042003703C001200041C0016A200041A0016A1003200041286A41186A200041A0016A41186A2903003703002000200041B0016A2903003703382000200041A8016A290300370330200020002903A0013703284100450D0441004100100441011005000B4108100C1A200041C0016A41186A4200370300200042003703D001200042003703C801200042013703C001200041C0016A200041A0016A1003200041B0016A3502002104200041A0016A41086A290300210520002903A0012106410441014100100F22012802002102200041E8006A41186A420037030020004200370378200042003703702000420037036820002005370394012000200637038C01200020043E029C01200042003703602000418C016A2002200141086A200041E8006A200041E0006A10061A1007220041086A100C22012000360200200141046A2000360200200141086A4100200010084100450D0441004100100441011005000B2001417C6A4120490D04200241046A200041C8006A4118100D200041D8006A3502002104200041D0006A290300210520002903482106200041C0016A41186A4200370300200042003703D001200042003703C801200042013703C001200041A0016A4104100A200020053703A801200020063703A001200020043E02B001200041C0016A200041A0016A100941010D0541004100100441011005000B41004100100441011005000B41004100100441011005000B200041286A4120100C22004120100E20004120100441001005000B41004100100441001005000B200041E0016A240041030F0B41004100100441001005000B0B0A010041000B0483EBD6E400740970726F647563657273010C70726F6365737365642D62790105636C616E675431302E302E3120286769743A2F2F6769746875622E636F6D2F6C6C766D2F6C6C766D2D70726F6A65637420623661313733343336373838653638333239636335653965653066363531623630336136333765332900F801046E616D6501F00111000C6765745F6D736776616C7565010D6765745F63616C6C5F73697A65020F636F70795F63616C6C5F76616C7565030C6C6F61645F73746F72616765040A7365745F72657475726E050B73797374656D5F68616C740618696E766F6B655F64656C65676174655F636F6E7472616374070F6765745F72657475726E5F73697A650811636F70795F72657475726E5F76616C7565090C736176655F73746F726167650A085F5F627A65726F380B0B5F5F696E69745F686561700C085F5F6D616C6C6F630D0B5F5F62653332746F6C654E0E0B5F5F6C654E746F626533320F0A766563746F725F6E657710057374617274"
                    .HexToBytes();

            var bAddress = "0xfd893ce89186fc6861d339cb6ab5d75458e3daf3".HexToBytes().ToUInt160();
            var bContract = new Contract
            (
                bAddress,
                bCode
            );
            if (!VirtualMachine.VerifyContract(bContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            // C
            var cCode = "0061736D01000000011C0660017F006000017F60037F7F7F0060027F7F0060000060017F017F0287010703656E760C6765745F6D736776616C7565000003656E760D6765745F63616C6C5F73697A65000103656E760F636F70795F63616C6C5F76616C7565000203656E760C736176655F73746F72616765000303656E760A7365745F72657475726E000303656E760B73797374656D5F68616C74000003656E760C6C6F61645F73746F726167650003030504040502040405017001010105030100020608017F01418080040B071202066D656D6F72790200057374617274000A0AB205042E004100410036028080044100410036028480044100410036028C800441003F0041107441F0FF7B6A36028880040BA60101047F418080042101024003400240200128020C0D002001280208220220004F0D020B200128020022010D000B41002101410028020821020B02402002200041076A41787122036B22024118490D00200120036A41106A22002001280200220436020002402004450D00200420003602040B2000200241706A3602082000410036020C2000200136020420012000360200200120033602080B2001410136020C200141106A0B2D002001411F6A21010340200120002D00003A00002001417F6A2101200041016A21002002417F6A22020D000B0BAA0301037F23004180016B220024002000100002400240024002402000290300200041106A29030084200041086A290300200041186A29030084844200520D00100741001001220136020441002001100822023602084100200120021002200141034D0D014100200228020022013602000240200141A0ACCAAA05460D0020014183D7DBA67E470D02200041E0006A41186A4200370300200041C0006A41186A4200370300200042003703702000420037036820004200370360200042003703502000420037034820004205370340200041E0006A200041C0006A10034100450D0341004100100441011005000B200041E0006A41186A4200370300200042003703702000420037036820004200370360200041E0006A200041C0006A1006200041206A41186A200041C0006A41186A2903003703002000200041D0006A2903003703302000200041C8006A290300370328200020002903403703204100450D0341004100100441011005000B41004100100441011005000B41004100100441011005000B41004100100441001005000B200041206A4120100822004120100920004120100441001005000B00740970726F647563657273010C70726F6365737365642D62790105636C616E675431302E302E3120286769743A2F2F6769746875622E636F6D2F6C6C766D2F6C6C766D2D70726F6A656374206236613137333433363738386536383332396363356539656530663635316236303361363337653329009701046E616D65018F010B000C6765745F6D736776616C7565010D6765745F63616C6C5F73697A65020F636F70795F63616C6C5F76616C7565030C736176655F73746F72616765040A7365745F72657475726E050B73797374656D5F68616C74060C6C6F61645F73746F72616765070B5F5F696E69745F6865617008085F5F6D616C6C6F63090B5F5F6C654E746F626533320A057374617274"
                    .HexToBytes();

            var cAddress = "0x6bc32575acb8754886dc283c2c8ac54b1bd93195".HexToBytes().ToUInt160();
            var cContract = new Contract
            (
                cAddress,
                cCode
            );
            if (!VirtualMachine.VerifyContract(cContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            var snapshot = stateManager.NewSnapshot();
            snapshot.Contracts.AddContract(UInt160Utils.Zero, aContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, bContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, cContract);
            stateManager.Approve();

            for (var i = 0; i < 1; ++i)
            {
                var currentTime = TimeUtils.CurrentTimeMillis();
                var currentSnapshot = stateManager.NewSnapshot();

                var sender = UInt160Utils.Zero;

                currentSnapshot.Balances.AddBalance(UInt160Utils.Zero, 100.ToUInt256().ToMoney());

                var transactionReceipt = new TransactionReceipt();
                transactionReceipt.Transaction = new Transaction();
                transactionReceipt.Transaction.Value = 0.ToUInt256();
                var context = new InvocationContext(sender, currentSnapshot, transactionReceipt);

                {
                    Console.WriteLine($"\nA: init({bAddress.ToHex()})");
                    var input = ContractEncoder.Encode("init(address)", bAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nB: init({cAddress.ToHex()})");
                    var input = ContractEncoder.Encode("init(address)", cAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(bContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    Console.WriteLine($"\nA: assignValue()");
                    var input = ContractEncoder.Encode("assignValue()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }

                stateManager.Approve();
            exit_mark:
                var elapsedTime = TimeUtils.CurrentTimeMillis() - currentTime;
                Console.WriteLine("Elapsed Time: " + elapsedTime + "ms");
            }
        }

        [Test]
        public void Test_VirtualMachine_InvokeERC20Contract()
        {
            var stateManager = _container.Resolve<IStateManager>();

            var hash = UInt160Utils.Zero;
            var contract = new Contract
            (
                hash,
                "0061736D01000000016A0D60017F0060037F7F7F0060027F7F006000017F60037F7F7F017F60017F017F60000060087E7E7E7E7E7E7E7F017F600A7E7E7E7E7E7E7E7E7E7E017F60077E7E7E7E7E7E7F017F600B7E7E7E7E7E7E7E7E7E7E7F017F60077E7E7E7E7E7E7E017F60047E7E7E7F017F02BF010A03656E760A6765745F73656E646572000003656E761063727970746F5F6B656363616B323536000103656E760C6C6F61645F73746F72616765000203656E760A7365745F72657475726E000203656E760B73797374656D5F68616C74000003656E760C736176655F73746F72616765000203656E760977726974655F6C6F67000203656E760C6765745F6D736776616C7565000003656E760D6765745F63616C6C5F73697A65000303656E760F636F70795F63616C6C5F76616C756500010313120102010101040506070809080A070B0B0C060405017001010105030100020608017F01418080040B071202066D656D6F72790200057374617274001B0AE759122E0002402002450D000340200020012D00003A0000200041016A2100200141016A21012002417F6A22020D000B0B0B240002402001450D00034020004200370300200041086A21002001417F6A22010D000B0B0B2D002000411F6A21000340200120002D00003A0000200141016A21012000417F6A21002002417F6A22020D000B0B2D002001411F6A21010340200120002D00003A00002001417F6A2101200041016A21002002417F6A22020D000B0B29002001417F6A21010340200120026A20002D00003A0000200041016A21002002417F6A22020D000B0B7E01017F200120006C220141086A10102203200036020420032000360200200341086A2100024002402002417F460D002001450D010340200020022D00003A0000200041016A2100200241016A21022001417F6A22010D000C020B0B2001450D000340200041003A0000200041016A21002001417F6A22010D000B0B20030BA60101047F418080042101024003400240200128020C0D002001280208220220004F0D020B200128020022010D000B41002101410028020821020B02402002200041076A41787122036B22024118490D00200120036A41106A22002001280200220436020002402004450D00200420003602040B2000200241706A3602082000410036020C2000200136020420012000360200200120033602080B2001410136020C200141106A0B2E004100410036028080044100410036028480044100410036028C800441003F0041107441F0FF7B6A36028880040B990504047F037E017F047E230041306B220824002008220941186A10002009200941186A41086A220A290300370308200920092903183703002009200941186A41106A220B3502003E021002400240024041000D00200941086A290300210C200941106A350200210D2009290300210E200841606A2208220F2400200941186A10002008200A290300370308200820092903183703002008200B3502003E02104101450D01200841106A3502002110200841086A290300211120082903002112200F41406A2208220A2400200841286A2011370300200841206A2012370300200841306A20103E0200200841186A4200370300200842003703102008420037030820084201370300200A41606A220A220B240020084138200A1001200A2903002110200A41086A2903002111200A41106A2903002112200A41186A2903002113200B41406A2208220A2400200841286A2001370300200841206A2000370300200841306A20023E0200200841186A2013370300200820123703102008201137030820082010370300200A41606A220A220B240020084138200A1001200A2903002110200A41086A2903002111200A41106A2903002112200A41186A2903002113200B41606A2208220A2400200841186A2013370300200820123703102008201137030820082010370300200A41606A220A24002008200A1002200E200C200D200020012002200A290300221020037C2211200A41086A290300220320047C20112010542208AD7C2204200A41106A290300221020057C22052008200420035420042003511BAD7C2203200A41186A29030020067C2005201054AD7C2003200554AD7C10132208450D02200941306A240020080F0B200941306A240041000F0B200941306A240041000F0B200741013A0000200941306A240041000BD80602057F047E23004190016B220A210B200A2400024002402000200242FFFFFFFF0F8384200184500D002003200542FFFFFFFF0F83842004844200520D014122410141A003100F220C2802004100200C1B413F6A41607141246A220D1010210A200B41A0F38DC60036028401200B4184016A200A4104100E200A41046A220E200D410376100B200B412036028801200B4188016A200E4104100D200B200C2802004100200C1B220E36028C01200B418C016A200A41246A4104100D200A41C4006A200C41086A200E100A200A200D100341011004000B4124410141F002100F220C2802004100200C1B413F6A41607141246A220D1010210A200B41A0F38DC600360204200B41046A200A4104100E200A41046A220E200D410376100B200B4120360208200B41086A200E4104100D200B200C2802004100200C1B220E36020C200B410C6A200A41246A4104100D200A41C4006A200C41086A200E100A200A200D100341011004000B200A41406A220A220C2400200A41286A2001370300200A41206A2000370300200A41306A20023E0200200A41186A4200370300200A4200370310200A4200370308200A4201370300200C41606A220C220D2400200A4138200C1001200C290300210F200C41086A2903002110200C41106A2903002111200C41186A2903002112200D41406A220A220C2400200A41286A2004370300200A41206A2003370300200A41306A20053E0200200A41186A2012370300200A2011370310200A2010370308200A200F370300200C41606A220C220D2400200A4138200C1001200C290300210F200C41086A2903002110200C41106A2903002111200C41186A2903002112200D41606A220A2400200B41106A41186A2009370300200A41186A2012370300200A2011370310200A2010370308200A200F370300200B2008370320200B2007370318200B2006370310200A200B41106A100541201010220A4104100B200B41306A41186A2009370300200B2008370340200B2007370338200B2006370330200B41306A200A4120100D41201010220C4104100B200B2001370358200B2000370350200B20023E0260200B41D0006A200C4114100D41201010220C4104100B200B2004370370200B2003370368200B20053E0278200B41E8006A200C4114100D200A41201006200B4190016A240041000BF30202037F017E23004180016B22072400200741406A220822092400200841286A2001370300200841206A2000370300200841306A20023E0200200841186A4200370300200842003703102008420037030820084201370300200841382007220741E0006A1001200741E0006A41086A2903002102200741E0006A41106A2903002100200741E0006A41186A29030021012007290360210A200941406A22082400200841286A2004370300200841206A2003370300200841306A20053E0200200841186A200137030020082000370310200820023703082008200A37030020084138200741C0006A1001200741206A41186A200741C0006A41186A2903003703002007200741C0006A41106A2903003703302007200741C0006A41086A29030037032820072007290340370320200741206A20071002200641186A200741186A2903003703002006200741106A2903003703102006200741086A2903003703082006200729030037030020074180016A240041000BB60D04047F047E017F047E230041C0016B220A210B200A240002400240024002402000200242FFFFFFFF0F8384200184500D002003200542FFFFFFFF0F83842004844200510D014101450D02200A41406A220A220C2400200A41286A2001370300200A41206A2000370300200A41306A20023E0200200A41186A4200370300200A4200370310200A4200370308200A4200370300200C41606A220C220D2400200A4138200C1001200C290300210E200C41086A290300210F200C41106A2903002110200C41186A2903002111200D41606A220A220C2400200A41186A2011370300200A2010370310200A200F370308200A200E370300200C41606A220C220D2400200A200C1002200C290300221120065A200C41086A290300220E20075A200E20075122121B200C41106A290300220F20085A200C41186A290300221020095A20102009511B200F200885201020098584501B0D034126410141E000100F220C2802004100200C1B413F6A41607141246A220D1010210A200B41A0F38DC6003602B401200B41B4016A200A4104100E200A41046A2212200D410376100B200B41203602B801200B41B8016A20124104100D200B200C2802004100200C1B22123602BC01200B41BC016A200A41246A4104100D200A41C4006A200C41086A2012100A200A200D100341011004000B412541014100100F220C2802004100200C1B413F6A41607141246A220D1010210A200B41A0F38DC600360208200B41086A200A4104100E200A41046A2212200D410376100B200B412036020C200B410C6A20124104100D200B200C2802004100200C1B2212360210200B41106A200A41246A4104100D200A41C4006A200C41086A2012100A200A200D100341011004000B412341014130100F220C2802004100200C1B413F6A41607141246A220D1010210A200B41A0F38DC600360214200B41146A200A4104100E200A41046A2212200D410376100B200B4120360218200B41186A20124104100D200B200C2802004100200C1B221236021C200B411C6A200A41246A4104100D200A41C4006A200C41086A2012100A200A200D100341011004000B200B41C0016A240041000F0B200D41406A220A220C2400200A41286A2001370300200A41206A2000370300200A41306A20023E0200200A41186A4200370300200A4200370310200A4200370308200A4200370300200C41606A220C220D2400200A4138200C1001200C2903002113200C41086A2903002114200C41106A2903002115200C41186A2903002116200D41606A220A220C2400200B41206A41186A201020097D200F200854AD7D200F20087D220F2011200654220D200E20075420121BAD221054AD7D370300200A41186A2016370300200A2015370310200A2014370308200A2013370300200B200F20107D370330200B200E20077D200DAD7D370328200B201120067D370320200A200B41206A1005200C41406A220A220C2400200A41286A2004370300200A41206A2003370300200A41306A20053E0200200A41186A4200370300200A4200370310200A4200370308200A4200370300200C41606A220C220D2400200A4138200C1001200C290300210E200C41086A290300210F200C41106A2903002110200C41186A2903002111200D41606A220A220C2400200A41186A2011370300200A2010370310200A200F370308200A200E370300200C41606A220C220D2400200A200C1002200C41186A2903002113200C41106A290300210F200C41086A290300210E200C2903002110200D41406A220A220C2400200A41286A2004370300200A41206A2003370300200A41306A20053E0200200A41186A4200370300200A4200370310200A4200370308200A4200370300200C41606A220C220D2400200A4138200C1001200C2903002111200C41086A2903002114200C41106A2903002115200C41186A2903002116200D41606A220A2400200A41186A2016370300200A2015370310200A2014370308200A2011370300200B201020067C2211370340200B200E20077C2011201054220CAD7C2210370348200B200F20087C2211200C2010200E542010200E511BAD7C220E370350200B41C0006A41186A201320097C2011200F54AD7C200E201154AD7C370300200A200B41C0006A100541201010220A4104100B200B41E0006A41186A2009370300200B2008370370200B2007370368200B2006370360200B41E0006A200A4120100D41201010220C4104100B200B200137038801200B200037038001200B20023E029001200B4180016A200C4114100D41201010220C4104100B200B20043703A001200B200337039801200B20053E02A801200B4198016A200C4114100D200A41201006200B41C0016A240041000B890703047F047E017F230041306B220B210C200B24000240024002400240024020002001200220032004200520062007200820091015220D0D00200B41606A220B220D2400200C41186A1000200B200C41186A41086A290300370308200B200C290318370300200B200C41186A41106A3502003E02104101450D01200B41106A3502002103200B41086A2903002104200B2903002105200D41406A220B220D2400200B41286A2001370300200B41206A2000370300200B41306A20023E0200200B41186A4200370300200B4200370310200B4200370308200B4201370300200D41606A220D220E2400200B4138200D1001200D290300210F200D41086A2903002110200D41106A2903002111200D41186A2903002112200E41406A220B220D2400200B41286A2004370300200B41206A2005370300200B41306A20033E0200200B41186A2012370300200B2011370310200B2010370308200B200F370300200D41606A220D220E2400200B4138200D1001200D2903002103200D41086A2903002104200D41106A2903002105200D41186A290300210F200E41606A220B220D2400200B41186A200F370300200B2005370310200B2004370308200B2003370300200D41606A220D220E2400200B200D1002200D290300220F20065A200D41086A290300220420075A200420075122131B200D41106A290300220520085A200D41186A290300220320095A20032009511B2005200885200320098584501B450D02200E41606A220B2400200C41186A1000200B200C41186A41086A290300370308200B200C290318370300200B200C41186A41106A3502003E02104101450D03200020012002200B290300200B41086A290300200B41106A350200200F20067D200420077D200F200654220BAD7D200520087D2206200B200420075420131BAD22077D200320097D2005200854AD7D2006200754AD7D1013220B450D04200C41306A2400200B0F0B200C41306A2400200D0F0B200C41306A240041000F0B41284101419001100F220D2802004100200D1B413F6A41607141246A220A1010210B200C41A0F38DC60036020C200C410C6A200B4104100E200B41046A220E200A410376100B200C4120360210200C41106A200E4104100D200C200D2802004100200D1B220E360214200C41146A200B41246A4104100D200B41C4006A200D41086A200E100A200B200A100341011004000B200C41306A240041000F0B200A41013A0000200C41306A240041000BDD0605027F037E027F017E017F230041C0006B220824002008220941286A10002009200941286A41086A290300370308200920092903283703002009200941286A41106A3502003E0210024002400240024041000D00200941106A350200210A200941086A290300210B2009290300210C200841406A2208220D2400200841286A200B370300200841206A200C370300200841306A200A3E0200200841186A4200370300200842003703102008420037030820084201370300200D41606A220D220E240020084138200D1001200D290300210A200D41086A290300210B200D41106A290300210C200D41186A290300210F200E41406A2208220D2400200841286A2001370300200841206A2000370300200841306A20023E0200200841186A200F3703002008200C3703102008200B3703082008200A370300200D41606A220D220E240020084138200D1001200D290300210A200D41086A290300210B200D41106A290300210C200D41186A290300210F200E41606A2208220D2400200841186A200F3703002008200C3703102008200B3703082008200A370300200D41606A220D220E24002008200D1002200D290300220F20035A200D41086A290300220B20045A200B20045122101B200D41106A290300220C20055A200D41186A290300220A20065A200A2006511B200C200585200A20068584501B450D01200E41606A22082400200941286A10002008200941286A41086A290300370308200820092903283703002008200941286A41106A3502003E02104101450D022008290300200841086A290300200841106A350200200020012002200F20037D200B20047D200F2003542208AD7D200C20057D22032008200B20045420101BAD22047D200A20067D200C200554AD7D2003200454AD7D10132208450D03200941C0006A240020080F0B200941C0006A240041000F0B4125410141C001100F220D2802004100200D1B413F6A41607141246A220710102108200941A0F38DC60036021C2009411C6A20084104100E200841046A220E2007410376100B20094120360220200941206A200E4104100D2009200D2802004100200D1B220E360224200941246A200841246A4104100D200841C4006A200D41086A200E100A20082007100341011004000B200941C0006A240041000F0B200741013A0000200941C0006A240041000BC90802057F087E230041A0016B2207210820072400024002402000200242FFFFFFFF0F8384200184500D0041010D01200841A0016A240041000F0B411F410141F001100F2209280200410020091B413F6A41607141246A220A10102107200841A0F38DC6003602940120084194016A20074104100E200741046A220B200A410376100B200841203602980120084198016A200B4104100D20082009280200410020091B220B36029C012008419C016A200741246A4104100D200741C4006A200941086A200B100A2007200A100341011004000B200741606A220722092400200741186A4200370300200742003703102007420037030820074202370300200941606A2209220A2400200720091002200941186A290300210C200941106A290300210D200941086A290300210E2009290300210F200A41606A220722092400200741186A42003703002007420037031020074200370308200742023703002008200F20037C22103703002008200E20047C2010200F54220AAD7C220F3703082008200D20057C2210200A200F200E54200F200E511BAD7C220E370310200841186A200C20067C2010200D54AD7C200E201054AD7C370300200720081005200941406A220722092400200741286A2001370300200741206A2000370300200741306A20023E0200200741186A4200370300200742003703102007420037030820074200370300200941606A2209220A240020074138200910012009290300210E200941086A290300210D200941106A290300210F200941186A2903002110200A41606A220722092400200741186A20103703002007200F3703102007200D3703082007200E370300200941606A2209220A2400200720091002200941186A290300210C200941106A290300210D200941086A290300210E2009290300210F200A41406A220722092400200741286A2001370300200741206A2000370300200741306A20023E0200200741186A4200370300200742003703102007420037030820074200370300200941606A2209220A2400200741382009100120092903002110200941086A2903002111200941106A2903002112200941186A2903002113200A41606A22072400200741186A20133703002007201237031020072011370308200720103703002008200F20037C22103703202008200E20047C2010200F542209AD7C220F3703282008200D20057C22102009200F200E54200F200E511BAD7C220E370330200841206A41186A200C20067C2010200D54AD7C200E201054AD7C3703002007200841206A10054120101022074104100B200841C0006A41186A2006370300200820053703502008200437034820082003370340200841C0006A20074120100D4120101022094104100B2008420037036820084200370360200842003E0270200841E0006A20094114100D4120101022094104100B200820013703800120082000370378200820023E028801200841F8006A20094114100D200741201006200841A0016A240041000B880A04047F047E017F047E230041B0016B22072108200724000240024002402000200242FFFFFFFF0F8384200184500D004101450D01200741406A220722092400200741286A2001370300200741206A2000370300200741306A20023E0200200741186A4200370300200742003703102007420037030820074200370300200941606A2209220A240020074138200910012009290300210B200941086A290300210C200941106A290300210D200941186A290300210E200A41606A220722092400200741186A200E3703002007200D3703102007200C3703082007200B370300200941606A2209220A24002007200910022009290300220E20035A200941086A290300220B20045A200B200451220F1B200941106A290300220C20055A200941186A290300220D20065A200D2006511B200C200585200D20068584501B0D024122410141C002100F2209280200410020091B413F6A41607141246A220A10102107200841A0F38DC6003602A401200841A4016A20074104100E200741046A220F200A410376100B200841203602A801200841A8016A200F4104100D20082009280200410020091B220F3602AC01200841AC016A200741246A4104100D200741C4006A200941086A200F100A2007200A100341011004000B41214101419002100F2209280200410020091B413F6A41607141246A220A10102107200841A0F38DC600360204200841046A20074104100E200741046A220F200A410376100B20084120360208200841086A200F4104100D20082009280200410020091B220F36020C2008410C6A200741246A4104100D200741C4006A200941086A200F100A2007200A100341011004000B200841B0016A240041000F0B200A41406A220722092400200741286A2001370300200741206A2000370300200741306A20023E0200200741186A4200370300200742003703102007420037030820074200370300200941606A2209220A2400200741382009100120092903002110200941086A2903002111200941106A2903002112200941186A2903002113200A41606A220722092400200841106A41186A200D20067D200C200554AD7D200C20057D220C200E200354220A200B200454200F1BAD220D54AD7D370300200741186A20133703002007201237031020072011370308200720103703002008200C200D7D3703202008200B20047D200AAD7D3703182008200E20037D3703102007200841106A1005200941606A220722092400200741186A4200370300200742003703102007420037030820074202370300200941606A2209220A2400200720091002200941186A290300210E200941106A290300210C2009290300210D200941086A290300210B200A41606A22072400200741186A4200370300200742003703102007420037030820074202370300200841306A41186A200E20067D200C200554AD7D200C20057D220C200D2003542209200B200454200B2004511BAD220E54AD7D3703002008200C200E7D3703402008200B20047D2009AD7D3703382008200D20037D3703302007200841306A10054120101022074104100B200841D0006A41186A2006370300200820053703602008200437035820082003370350200841D0006A20074120100D4120101022094104100B2008200137037820082000370370200820023E028001200841F0006A20094114100D4120101022094104100B20084200370390012008420037038801200842003E02980120084188016A20094114100D200741201006200841B0016A240041000BEC0101027F230041E0006B22042400200441406A22052400200541286A2001370300200541206A2000370300200541306A20023E0200200541186A4200370300200542003703102005420037030820054200370300200541382004220441C0006A1001200441206A41186A200441C0006A41186A2903003703002004200441C0006A41106A2903003703302004200441C0006A41086A29030037032820042004290340370320200441206A20041002200341186A200441186A2903003703002003200441106A2903003703102003200441086A29030037030820032004290300370300200441E0006A240041000BE51602047F0A7E230041B0066B22002400200041086A1007024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002400240024002402000290308200041186A29030084200041106A290300200041206A29030084844200520D0010114100100822013602C80341002001101022023602CC034100200120021009024002400240024002400240024002400240024002400240200141034D0D004100200228020022033602C4032001417C6A2101200241046A2102024020034185FAFB1E4A0D000240200341A3AF89BE7D4A0D002003419D85FFE47A460D0A20034189BC9D9D7B460D05200341A98BF0DC7B470D0220014120490D1A2002200041B0026A4118100C2001AD423F580D1B200041B0026A41106A3502002104200041B0026A41086A290300210520002903B0022106200241206A200041C8026A4120100C200041C8026A41186A2903002107200041C8026A41106A2903002108200041C8026A41086A290300210920002903C802210A20004190066A1000200020004190066A41086A290300220B3703F8052000200029039006220C3703F005200020004190066A41106A350200220D3E028006200C200B200D200620052004200A200920082007101522010D07200041013A00EF0241000D080C2D0B200341A4AF89BE7D460D0B200341A3F0CAEB7D460D0A20034198ACB4E87D470D0120004190066A41186A4200370300200042003703A0062000420037039806200042023703900620004190066A200041F0056A1002200041E8006A41186A200041F0056A41186A290300370300200020004180066A2903003703782000200041F8056A290300370370200020002903F0053703684100450D1141004100100341011004000B0240200341DCC5B5F7034A0D00200341C082BFC801460D05200341F0C08A8C03460D0C20034186FAFB1E470D01200041A0016A4280DC95DBF68DDDA0CC003703002000420037039801200042003703900120004200370388014100450D1241004100100341011004000B0240200341B8A0CD8C054A0D00200341DDC5B5F703460D0820034195B1EF8C04470D01200041F0046A42808080808080C0A0CC00370300200042003703E804200042003703E004200042003703D8044100450D2541004100100341011004000B200341B9A0CD8C05460D01200341B1F894BF06460D020B41004100100341011004000B20014120490D0B2002200041286A4118100C2001AD423F580D0C200041286A41106A3502002104200041286A41086A290300210520002903282106200241206A200041C0006A4120100C2006200520042000290340200041C0006A41086A290300200041C0006A41106A290300200041C0006A41186A290300200041E7006A1012450D0D41004100100341011004000B200041123A00AF014100450D0F41004100100341011004000B20014120490D0F2002200041B0016A4118100C2001AD423F580D10200041B0016A41106A3502002104200041B0016A41086A290300210520002903B0012106200241206A200041C8016A4120100C200041C8016A41186A2903002107200041C8016A41106A2903002108200041C8016A41086A290300210920002903C801210A20004190066A1000200020004190066A41086A290300220B3703F8052000200029039006220C3703F005200020004190066A41106A350200220D3E02800602400240200C200B200D200620052004200A200920082007101322010D00200041013A00EF0141000D010C280B2001450D270B41004100100341011004000B20014120490D102002200041F0016A4118100C2001AD423F580D11200041F0016A41106A3502002104200041F0016A41086A290300210520002903F0012106200241206A20004188026A4120100C024020062005200420002903880220004188026A41086A29030020004188026A41106A29030020004188026A41186A290300101822010D00200041013A00AF02410021010B2001450D1241004100100341011004000B2001450D250B41004100100341011004000B20014120490D122002200041F0026A4118100C2001AD423F580D13200041F0026A41106A3502002104200041F0026A41086A290300210520002903F0022106200241206A20004188036A4118100C20062005200420002903880320004188036A41086A29030020004188036A41106A350200200041A0036A1014450D1441004100100341011004000B20014120490D142002200041C0036A4118100C2001AD423F580D15200041C0036A41106A3502002104200041C0036A41086A290300210520002903C0032106200241206A200041D8036A4120100C024020062005200420002903D803200041D8036A41086A290300200041D8036A41106A290300200041D8036A41186A290300101922010D00200041013A00FF03410021010B2001450D1641004100100341011004000B20014120490D16200220004180046A4118100C2001AD2204423F580D1720004180046A41106A350200210520004180046A41086A29030021062000290380042107200241206A20004198046A4118100C200442DF00580D1820004198046A41106A350200210420004198046A41086A29030021082000290398042109200241C0006A200041B0046A4120100C20072006200520092008200420002903B004200041B8046A290300200041C0046A290300200041C8046A290300200041D7046A1016450D1941004100100341011004000B20014120490D1A2002200041F8046A4118100C2001AD423F580D1B200041F8046A41106A3502002104200041F8046A41086A290300210520002903F8042106200241206A20004190056A4120100C20062005200420002903900520004190056A41086A29030020004190056A41106A29030020004190056A41186A290300200041B7056A1017450D1C41004100100341011004000B20014120490D1C2002200041B8056A4118100C20002903B805200041C0056A290300200041C8056A350200200041D0056A101A450D1D41004100100341011004000B41004100100341011004000B200041B0066A240041020F0B200041B0066A240041020F0B4120101022014104100B2001411F6A20002D00673A000020014120100341001004000B4120101022014104100B200041E8006A20014120100D20014120100341001004000B4120101022014104100B20004188016A20014120100E20014120100341001004000B4120101022014104100B2001411F6A20002D00AF013A000020014120100341001004000B200041B0066A240041020F0B200041B0066A240041020F0B200041B0066A240041020F0B200041B0066A240041020F0B4120101022014104100B2001411F6A20002D00AF023A000020014120100341001004000B200041B0066A240041020F0B200041B0066A240041020F0B200041B0066A240041020F0B200041B0066A240041020F0B4120101022014104100B200041A0036A20014120100D20014120100341001004000B200041B0066A240041020F0B200041B0066A240041020F0B4120101022014104100B2001411F6A20002D00FF033A000020014120100341001004000B200041B0066A240041020F0B200041B0066A240041020F0B200041B0066A240041020F0B4120101022014104100B2001411F6A20002D00D7043A000020014120100341001004000B4120101022014104100B200041D8046A20014120100E20014120100341001004000B200041B0066A240041020F0B200041B0066A240041020F0B4120101022014104100B2001411F6A20002D00B7053A000020014120100341001004000B200041B0066A240041020F0B4120101022014104100B200041D0056A20014120100D20014120100341001004000B4120101022014104100B2001411F6A20002D00EF013A000020014120100341001004000B4120101022014104100B2001411F6A20002D00EF023A000020014120100341001004000B0BC903010041000BC20345524332303A207472616E736665722066726F6D20746865207A65726F2061646472657373000000000000000000000045524332303A207472616E7366657220746F20746865207A65726F20616464726573730000000000000000000000000045524332303A207472616E7366657220616D6F756E7420657863656564732062616C616E63650000000000000000000045524332303A207472616E7366657220616D6F756E74206578636565647320616C6C6F77616E6365000000000000000045524332303A2064656372656173656420616C6C6F77616E63652062656C6F77207A65726F000000000000000000000045524332303A206D696E7420746F20746865207A65726F20616464726573730045524332303A206275726E2066726F6D20746865207A65726F206164647265737300000000000000000000000000000045524332303A206275726E20616D6F756E7420657863656564732062616C616E6365000000000000000000000000000045524332303A20617070726F76652066726F6D20746865207A65726F206164647265737300000000000000000000000045524332303A20617070726F766520746F20746865207A65726F206164647265737300740970726F647563657273010C70726F6365737365642D62790105636C616E675431312E302E3120286769743A2F2F6769746875622E636F6D2F6C6C766D2F6C6C766D2D70726F6A65637420313365343336396337333335356136633561333166633161313131356663376336393734336164612900E705046E616D6501DF051C000A6765745F73656E646572011063727970746F5F6B656363616B323536020C6C6F61645F73746F72616765030A7365745F72657475726E040B73797374656D5F68616C74050C736176655F73746F72616765060977726974655F6C6F67070C6765745F6D736776616C7565080D6765745F63616C6C5F73697A65090F636F70795F63616C6C5F76616C75650A085F5F6D656D6370790B085F5F627A65726F380C0B5F5F62653332746F6C654E0D0B5F5F6C654E746F626533320E0A5F5F6C654E746F62654E0F0A766563746F725F6E657710085F5F6D616C6C6F63110B5F5F696E69745F68656170123A45524332303A3A45524332303A3A66756E6374696F6E3A3A696E637265617365416C6C6F77616E63655F5F616464726573735F75696E74323536133945524332303A3A45524332303A3A66756E6374696F6E3A3A5F617070726F76655F5F616464726573735F616464726573735F75696E74323536143245524332303A3A45524332303A3A66756E6374696F6E3A3A616C6C6F77616E63655F5F616464726573735F61646472657373153A45524332303A3A45524332303A3A66756E6374696F6E3A3A5F7472616E736665725F5F616464726573735F616464726573735F75696E74323536163D45524332303A3A45524332303A3A66756E6374696F6E3A3A7472616E7366657246726F6D5F5F616464726573735F616464726573735F75696E74323536173A45524332303A3A45524332303A3A66756E6374696F6E3A3A6465637265617365416C6C6F77616E63655F5F616464726573735F75696E74323536182E45524332303A3A45524332303A3A66756E6374696F6E3A3A5F6D696E745F5F616464726573735F75696E74323536192E45524332303A3A45524332303A3A66756E6374696F6E3A3A5F6275726E5F5F616464726573735F75696E743235361A2A45524332303A3A45524332303A3A66756E6374696F6E3A3A62616C616E63654F665F5F616464726573731B057374617274"
                    .HexToBytes()
            );
            if (!VirtualMachine.VerifyContract(contract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");

            var snapshot = stateManager.NewSnapshot();
            snapshot.Contracts.AddContract(UInt160Utils.Zero, contract);
            stateManager.Approve();

            Console.WriteLine("Contract Hash: " + hash.ToHex());

            for (var i = 0; i < 1; ++i)
            {
                var currentTime = TimeUtils.CurrentTimeMillis();
                var currentSnapshot = stateManager.NewSnapshot();

                var sender = "0x6bc32575acb8754886dc283c2c8ac54b1bd93195".HexToBytes().ToUInt160();
                var to = "0xfd893ce89186fc6861d339cb6ab5d75458e3daf3".HexToBytes().ToUInt160();

                var transactionReceipt = new TransactionReceipt();
                transactionReceipt.Transaction = new Transaction();
                transactionReceipt.Transaction.Value = 0.ToUInt256();
                var context = new InvocationContext(sender, currentSnapshot, transactionReceipt);

                {
                    /* ERC-20: name &*/
                    Console.WriteLine("\nERC-20: name()");
                    var input = ContractEncoder.Encode("name()");
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {"LAtoken"}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: symbol &*/
                    Console.WriteLine("\nERC-20: symbol()");
                    var input = ContractEncoder.Encode("symbol()");
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {"LA"}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: decimals &*/
                    Console.WriteLine("\nERC-20: decimals()");
                    var input = ContractEncoder.Encode("decimals()");
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {18}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: totalSupply &*/
                    Console.WriteLine("\nERC-20: totalSupply()");
                    var input = ContractEncoder.Encode("totalSupply()");
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {0}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* mint &*/
                    Console.WriteLine($"\nERC-20: mint({sender.ToHex()},{Money.FromDecimal(100)})");
                    var input = ContractEncoder.Encode("mint(address,uint256)", sender, Money.FromDecimal(100));
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {true}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: totalSupply &*/
                    Console.WriteLine("\nERC-20: totalSupply()");
                    var input = ContractEncoder.Encode("totalSupply()");
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {100}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: balanceOf &*/
                    Console.WriteLine($"\nERC-20: balanceOf({sender.ToHex()}");
                    var input = ContractEncoder.Encode("balanceOf(address)", sender);
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {100}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: transfer &*/
                    Console.WriteLine($"\nERC-20: transfer({to.ToHex()},{Money.FromDecimal(50)})");
                    var input = ContractEncoder.Encode("transfer(address,uint256)", to, Money.FromDecimal(50));
                    // Console.WriteLine($"ABI: {input.ToHex()}");
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {true}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: balanceOf &*/
                    Console.WriteLine($"\nERC-20: balanceOf({sender.ToHex()}");
                    var input = ContractEncoder.Encode("balanceOf(address)", sender);
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {50}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: balanceOf &*/
                    Console.WriteLine($"\nERC-20: balanceOf({to.ToHex()}");
                    var input = ContractEncoder.Encode("balanceOf(address)", to);
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {50}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: approve &*/
                    Console.WriteLine($"\nERC-20: approve({to.ToHex()},{Money.FromDecimal(50)})");
                    var input = ContractEncoder.Encode("approve(address,uint256)", to, Money.FromDecimal(50));
                    // Console.WriteLine($"ABI: {input.ToHex()}");
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {true}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: allowance &*/
                    Console.WriteLine($"\nERC-20: allowance({sender.ToHex()},{to.ToHex()})");
                    var input = ContractEncoder.Encode("allowance(address,address)", sender, to);
                    // Console.WriteLine($"ABI: {input.ToHex()}");
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {50}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: increaseAllowance &*/
                    Console.WriteLine($"\nERC-20: increaseAllowance({to.ToHex()},{Money.FromDecimal(10)})");
                    var input = ContractEncoder.Encode("increaseAllowance(address,uint256)", to, Money.FromDecimal(10));
                    // Console.WriteLine($"ABI: {input.ToHex()}");
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {true}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: allowance &*/
                    Console.WriteLine($"\nERC-20: allowance({sender.ToHex()},{to.ToHex()})");
                    var input = ContractEncoder.Encode("allowance(address,address)", sender, to);
                    // Console.WriteLine($"ABI: {input.ToHex()}");
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {60}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: decreaseAllowance &*/
                    Console.WriteLine($"\nERC-20: decreaseAllowance({to.ToHex()},{Money.FromDecimal(10)})");
                    var input = ContractEncoder.Encode("decreaseAllowance(address,uint256)", to, Money.FromDecimal(10));
                    // Console.WriteLine($"ABI: {input.ToHex()}");
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {true}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: allowance &*/
                    Console.WriteLine($"\nERC-20: allowance({sender.ToHex()},{to.ToHex()})");
                    var input = ContractEncoder.Encode("allowance(address,address)", sender, to);
                    // Console.WriteLine($"ABI: {input.ToHex()}");
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {50}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: transferFrom &*/
                    Console.WriteLine($"\nERC-20: transferFrom({sender.ToHex()},{to.ToHex()},{Money.FromDecimal(50)})");
                    var input = ContractEncoder.Encode("transferFrom(address,address,uint256)", sender, to,
                        Money.FromDecimal(50));
                    // Console.WriteLine($"ABI: {input.ToHex()}");

                    // change sender
                    context = new InvocationContext(to, context.Snapshot, transactionReceipt);

                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {true}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: balanceOf &*/
                    Console.WriteLine($"\nERC-20: balanceOf({sender.ToHex()}");
                    var input = ContractEncoder.Encode("balanceOf(address)", sender);
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {0}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: balanceOf &*/
                    Console.WriteLine($"\nERC-20: balanceOf({to.ToHex()}");
                    var input = ContractEncoder.Encode("balanceOf(address)", to);
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {100}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* burn &*/
                    Console.WriteLine($"\nERC-20: burn({to.ToHex()},{Money.FromDecimal(30)})");
                    var input = ContractEncoder.Encode("burn(address,uint256)", to, Money.FromDecimal(30));
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {true}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: totalSupply &*/
                    Console.WriteLine("\nERC-20: totalSupply()");
                    var input = ContractEncoder.Encode("totalSupply()");
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {70}, {status.ReturnValue!.ToHex()}");
                }
                {
                    /* ERC-20: balanceOf &*/
                    Console.WriteLine($"\nERC-20: balanceOf({to.ToHex()}");
                    var input = ContractEncoder.Encode("balanceOf(address)", to);
                    // Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(contract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }

                    Console.WriteLine($"Result: {70}, {status.ReturnValue!.ToHex()}");
                }

                stateManager.Approve();
            exit_mark:
                var elapsedTime = TimeUtils.CurrentTimeMillis() - currentTime;
                Console.WriteLine("Elapsed Time: " + elapsedTime + "ms");
            }
        }

        [Test]
        public void Test_VirtualMachine_InvokeCallingContract()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceA = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.A.wasm");
            var resourceB = assembly.GetManifestResourceStream("Lachain.CoreTest.Resources.scripts.B.wasm");
            if (resourceA is null)
                Assert.Fail("Failed t read script from resources");
            var aCode = new byte[resourceA!.Length];
            resourceA!.Read(aCode, 0, (int)resourceA!.Length);
            if (resourceB is null)
                Assert.Fail("Failed t read script from resources");
            var bCode = new byte[resourceB!.Length];
            resourceB!.Read(bCode, 0, (int)resourceB!.Length);
            var stateManager = _container.Resolve<IStateManager>();
            // A
            var aAddress = UInt160Utils.Zero;
            var aContract = new Contract
            (
                aAddress,
                aCode
            );
            if (!VirtualMachine.VerifyContract(aContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");
            // B
            var bAddress = "0xfd893ce89186fc6861d339cb6ab5d75458e3daf3".HexToBytes().ToUInt160();
            var bContract = new Contract
            (
                bAddress,
                bCode
            );
            if (!VirtualMachine.VerifyContract(bContract.ByteCode, true))
                throw new Exception("Unable to validate smart-contract code");
            var snapshot = stateManager.NewSnapshot();
            snapshot.Contracts.AddContract(UInt160Utils.Zero, aContract);
            snapshot.Contracts.AddContract(UInt160Utils.Zero, bContract);
            stateManager.Approve();
            for (var i = 0; i < 1; ++i)
            {
                var currentTime = TimeUtils.CurrentTimeMillis();
                var currentSnapshot = stateManager.NewSnapshot();
                var sender = UInt160Utils.Zero;
                currentSnapshot.Balances.AddBalance(UInt160Utils.Zero, 100.ToUInt256().ToMoney());
                var transactionReceipt = new TransactionReceipt();
                transactionReceipt.Transaction = new Transaction();
                transactionReceipt.Transaction.Value = 0.ToUInt256();
                var context = new InvocationContext(sender, currentSnapshot, transactionReceipt);
                {
                    Console.WriteLine($"\nA: init({bAddress.ToHex()})");
                    var input = ContractEncoder.Encode("init(address)", bAddress);
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                {
                    transactionReceipt.Transaction.Value = 0.ToUInt256();
                    context = new InvocationContext(sender, context.Snapshot, transactionReceipt);
                    Console.WriteLine("\nA: getA()");
                    var input = ContractEncoder.Encode("getA()");
                    Console.WriteLine("ABI: " + input.ToHex());
                    var status = VirtualMachine.InvokeWasmContract(aContract, context, input, 100_000_000_000_000UL);
                    if (status.Status != ExecutionStatus.Ok)
                    {
                        stateManager.Rollback();
                        Console.WriteLine("Contract execution failed: " + status.Status);
                        Console.WriteLine($"Result: {status.ReturnValue?.ToHex()}");
                        goto exit_mark;
                    }
                    Console.WriteLine($"Result: {status.ReturnValue!.ToHex()}");
                }
                stateManager.Approve();
            exit_mark:
                var elapsedTime = TimeUtils.CurrentTimeMillis() - currentTime;
                Console.WriteLine("Elapsed Time: " + elapsedTime + "ms");
            }
        }

    }
}